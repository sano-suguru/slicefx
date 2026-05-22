using System.Text.Encodings.Web;
using System.Text.Json;

namespace Slice.Cli.Templates;

internal sealed record WasiCloudflareSpec(
    string ComponentName,
    string AppName,
    string ProjectPath,
    string WasmInputPath);

internal sealed record TemplateFile(string RelativePath, string Content);

internal static class WasiCloudflareTemplate
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static TemplateFile[] Render(WasiCloudflareSpec spec)
        =>
        [
            new("package.json", ReplaceTokens(PackageJson, spec)),
            new("wrangler.toml", ReplaceTokens(WranglerToml, spec)),
            new("wrangler.deploy.toml", ReplaceTokens(WranglerDeployToml, spec)),
            new("generate-module-map.mjs", ReplaceTokens(GenerateModuleMap, spec)),
            new("shim.mjs", ReplaceTokens(Shim, spec)),
            new(Path.Combine("stubs", "tcp.js"), TcpStub),
            new(Path.Combine("stubs", "udp.js"), UdpStub),
        ];

    private static string ReplaceTokens(string template, WasiCloudflareSpec spec)
        => template
            .Replace("__COMPONENT_NAME__", spec.ComponentName, StringComparison.Ordinal)
            .Replace("__APP_NAME_JSON__", JsonStringLiteral(spec.AppName), StringComparison.Ordinal)
            .Replace("__PROJECT_PATH_ARG__", JsonCommandArgument(spec.ProjectPath), StringComparison.Ordinal)
            .Replace("__WASM_INPUT_PATH_ARG__", JsonCommandArgument(spec.WasmInputPath), StringComparison.Ordinal);

    private static string JsonCommandArgument(string value)
        => @"\""" + JsonStringContent(value) + @"\""";

    private static string JsonStringLiteral(string value)
        => JsonSerializer.Serialize(value, JsonOptions);

    private static string JsonStringContent(string value)
    {
        var literal = JsonStringLiteral(value);
        return literal[1..^1];
    }

    // lang=json
    private const string PackageJson =
        """
        {
          "name": "__COMPONENT_NAME__-host",
          "private": true,
          "type": "module",
          "scripts": {
            "build": "dotnet publish __PROJECT_PATH_ARG__ -r wasi-wasm -c Release && npm run transpile",
            "transpile": "jco transpile __WASM_INPUT_PATH_ARG__ -o component --name __COMPONENT_NAME__ --tla-compat --no-namespaced-exports --instantiation async --map 'wasi:sockets/tcp@0.2.0=../stubs/tcp.js' --map 'wasi:sockets/udp@0.2.0=../stubs/udp.js' && wasm-opt -Oz component/__COMPONENT_NAME__.core.wasm -o component/__COMPONENT_NAME__.core.wasm && node generate-module-map.mjs",
            "deploy": "wrangler deploy --config wrangler.deploy.toml"
          },
          "dependencies": {
            "@bytecodealliance/preview2-shim": "0.17.9"
          },
          "devDependencies": {
            "@bytecodealliance/jco": "1.19.0",
            "binaryen": "129.0.0",
            "wrangler": "^4.93.1"
          }
        }
        """;

    private const string WranglerToml =
        """
        name = "__COMPONENT_NAME__"
        main = "shim.mjs"
        compatibility_date = "2024-09-23"
        compatibility_flags = ["nodejs_compat"]

        [build]
        command = "npm run build"
        cwd = "."

        [[rules]]
        type = "CompiledWasm"
        globs = ["component/*.wasm"]
        fallthrough = true
        """;

    private const string WranglerDeployToml =
        """
        name = "__COMPONENT_NAME__"
        main = "shim.mjs"
        compatibility_date = "2024-09-23"
        compatibility_flags = ["nodejs_compat"]

        [[rules]]
        type = "CompiledWasm"
        globs = ["component/*.wasm"]
        fallthrough = true
        """;

    private const string GenerateModuleMap =
        """
        import { readdir, writeFile } from "node:fs/promises";

        const componentDir = new URL("./component/", import.meta.url);
        const files = (await readdir(componentDir))
          .filter(file => /^__COMPONENT_NAME__\.core\d*\.wasm$/.test(file))
          .sort(compareCoreModuleNames);

        if (files.length === 0) {
          throw new Error("No jco core WASM modules found in component/.");
        }

        const imports = files.map((file, index) => `import core${index} from "./${file}";`).join("\n");
        const entries = files.map((file, index) => `  ["${file}", core${index}],`).join("\n");

        await writeFile(
          new URL("modules.mjs", componentDir),
          `${imports}

        const modules = new Map([
        ${entries}
        ]);

        export function getCoreModule(name) {
          const module = modules.get(name);
          if (module === undefined) {
            throw new Error(\`Unknown WASM module: \${name}\`);
          }

          return module;
        }
        `,
        );

        function compareCoreModuleNames(left, right) {
          return coreModuleIndex(left) - coreModuleIndex(right);
        }

        function coreModuleIndex(file) {
          const match = /^__COMPONENT_NAME__\.core(\d*)\.wasm$/.exec(file);
          if (match === null) {
            return Number.MAX_SAFE_INTEGER;
          }

          return match[1] === "" ? 1 : Number.parseInt(match[1], 10);
        }
        """;

    private const string Shim =
        """
        // Cloudflare Workers fetch handler for a Slice wasi:http/incoming-handler component.
        // The wasm component exports wasi:http/incoming-handler; jco transpiles it to an
        // instantiate() function. This file bridges Cloudflare's fetch(Request) to wasi:http.

        import { getCoreModule } from "./component/modules.mjs";
        import { instantiate } from "./component/__COMPONENT_NAME__.js";

        import {
          _setArgs,
          environment,
          exit as exitNs,
          stderr as stderrNs,
          stdin as stdinNs,
          stdout as stdoutNs,
          terminalInput, terminalOutput, terminalStderr, terminalStdin, terminalStdout,
        } from "@bytecodealliance/preview2-shim/cli";
        import { monotonicClock, wallClock } from "@bytecodealliance/preview2-shim/clocks";
        import { preopens, types as fsTypes } from "@bytecodealliance/preview2-shim/filesystem";
        import { error as ioError, poll as ioPoll, streams } from "@bytecodealliance/preview2-shim/io";
        import { random } from "@bytecodealliance/preview2-shim/random";
        import { TcpSocket } from "./stubs/tcp.js";
        import { UdpSocket, IncomingDatagramStream, OutgoingDatagramStream } from "./stubs/udp.js";

        const te = new TextEncoder();
        const td = new TextDecoder();
        const MAX_REQUEST_BODY_BYTES = 1024 * 1024;

        class Fields {
          #entries;
          constructor(entries) {
            this.#entries = (entries || []).map(([k, v]) =>
              [String(k), v instanceof Uint8Array ? v : te.encode(String(v))]
            );
          }
          entries() { return this.#entries.map(e => [e[0], e[1]]); }
          get(name) {
            const lc = name.toLowerCase();
            return this.#entries.filter(([k]) => k.toLowerCase() === lc).map(([, v]) => v);
          }
          static fromList(entries) { return new Fields(entries); }
        }

        class IncomingBody {
          #data;
          constructor(data) { this.#data = data instanceof Uint8Array ? data : new Uint8Array(0); }
          stream() {
            const data = this.#data;
            let pos = 0;
            return {
              tag: 'ok',
              val: new streams.InputStream({
                blockingRead(len) {
                  const n = Math.min(Number(len), data.length - pos);
                  if (n <= 0) return new Uint8Array(0);
                  const chunk = data.slice(pos, pos + n);
                  pos += n;
                  return chunk;
                },
              }),
            };
          }
          static finish(_body) {}
        }

        class IncomingRequest {
          #method; #pathWithQuery; #headers; #body;
          constructor(method, pathWithQuery, headers, body) {
            this.#method = method;
            this.#pathWithQuery = pathWithQuery;
            this.#headers = headers;
            this.#body = body;
          }
          method() { return this.#method; }
          pathWithQuery() { return this.#pathWithQuery; }
          headers() { return this.#headers; }
          authority() { return undefined; }
          scheme() { return undefined; }
          consume() { return { tag: 'ok', val: new IncomingBody(this.#body) }; }
        }

        class OutgoingBody {
          #chunks = [];
          write() {
            const chunks = this.#chunks;
            return new streams.OutputStream({ write(buf) { chunks.push(new Uint8Array(buf)); } });
          }
          collect() {
            const total = this.#chunks.reduce((s, c) => s + c.length, 0);
            const buf = new Uint8Array(total);
            let off = 0;
            for (const c of this.#chunks) { buf.set(c, off); off += c.length; }
            return buf;
          }
          static finish(_body, _trailers) {}
        }

        class OutgoingResponse {
          #status = 200; #headers; #body;
          constructor(headers) { this.#headers = headers; }
          setStatusCode(code) { this.#status = Number(code); }
          statusCode() { return this.#status; }
          headers() { return this.#headers; }
          body() { this.#body = new OutgoingBody(); return this.#body; }
          getBody() { return this.#body; }
        }

        class ResponseOutparam {
          #resolve; #reject;
          constructor(resolve, reject) { this.#resolve = resolve; this.#reject = reject; }
          static set(param, result) {
            if (result.tag === 'ok') param.#resolve(result.val);
            else param.#reject(new Error(`wasi:http error: ${JSON.stringify(result.val)}`));
          }
        }

        function methodTag(m) {
          switch (m.toUpperCase()) {
            case 'GET':     return { tag: 'get' };
            case 'HEAD':    return { tag: 'head' };
            case 'POST':    return { tag: 'post' };
            case 'PUT':     return { tag: 'put' };
            case 'DELETE':  return { tag: 'delete' };
            case 'CONNECT': return { tag: 'connect' };
            case 'OPTIONS': return { tag: 'options' };
            case 'TRACE':   return { tag: 'trace' };
            case 'PATCH':   return { tag: 'patch' };
            default:        return { tag: 'other', val: m };
          }
        }

        _setArgs([__APP_NAME_JSON__]);

        const instancePromise = instantiate(getCoreModule, {
          '../stubs/tcp.js': { TcpSocket },
          '../stubs/udp.js': { UdpSocket, IncomingDatagramStream, OutgoingDatagramStream },
          'wasi:cli/environment':    environment,
          'wasi:cli/exit':           exitNs,
          'wasi:cli/stderr':         stderrNs,
          'wasi:cli/stdin':          stdinNs,
          'wasi:cli/stdout':         stdoutNs,
          'wasi:cli/terminal-input':  terminalInput,
          'wasi:cli/terminal-output': terminalOutput,
          'wasi:cli/terminal-stderr': terminalStderr,
          'wasi:cli/terminal-stdin':  terminalStdin,
          'wasi:cli/terminal-stdout': terminalStdout,
          'wasi:clocks/monotonic-clock': monotonicClock,
          'wasi:clocks/wall-clock':      wallClock,
          'wasi:filesystem/preopens':    preopens,
          'wasi:filesystem/types':       fsTypes,
          'wasi:http/types': {
            Fields,
            IncomingRequest,
            IncomingBody,
            OutgoingBody,
            OutgoingResponse,
            ResponseOutparam,
          },
          'wasi:io/error':   ioError,
          'wasi:io/poll':    ioPoll,
          'wasi:io/streams': streams,
          'wasi:random/random': random,
        });

        export default {
          async fetch(cfRequest) {
            try {
              const { incomingHandler } = await instancePromise;
              const url = new URL(cfRequest.url);
              const pathWithQuery = url.pathname + (url.search || '');
              const headerEntries = [...cfRequest.headers.entries()].map(([k, v]) => [k, te.encode(v)]);
              const bodyBytes = await readRequestBody(cfRequest);
              const incomingRequest = new IncomingRequest(
                methodTag(cfRequest.method),
                pathWithQuery,
                new Fields(headerEntries),
                bodyBytes,
              );

              const outgoingResponse = await new Promise((resolve, reject) => {
                const responseOutparam = new ResponseOutparam(resolve, reject);
                try {
                  incomingHandler.handle(incomingRequest, responseOutparam);
                } catch (err) {
                  reject(err);
                }
              });

              const status = outgoingResponse.statusCode();
              const respHeaders = new Headers();
              for (const [k, v] of outgoingResponse.headers().entries()) {
                try { respHeaders.set(k, td.decode(v)); } catch { /* skip forbidden headers */ }
              }
              const respBody = outgoingResponse.getBody()?.collect() ?? new Uint8Array(0);
              return new Response(respBody.length > 0 ? respBody : null, { status, headers: respHeaders });
            } catch (err) {
              if (err instanceof PayloadTooLargeError) {
                return new Response('Payload Too Large', { status: 413 });
              }

              console.error(err);
              return new Response('Internal Server Error', {
                status: 500,
                headers: { 'Content-Type': 'text/plain; charset=utf-8' },
              });
            };
          },
        };

        async function readRequestBody(cfRequest) {
          if (cfRequest.method === 'GET' || cfRequest.method === 'HEAD') {
            return new Uint8Array(0);
          }

          const contentLength = cfRequest.headers.get('content-length');
          if (contentLength !== null) {
            const declaredLength = Number(contentLength);
            if (Number.isFinite(declaredLength) && declaredLength > MAX_REQUEST_BODY_BYTES) {
              throw new PayloadTooLargeError();
            }
          }

          if (cfRequest.body === null) {
            return new Uint8Array(0);
          }

          const reader = cfRequest.body.getReader();
          const chunks = [];
          let total = 0;
          try {
            while (true) {
              const { done, value } = await reader.read();
              if (done) {
                break;
              }

              const chunk = value instanceof Uint8Array ? value : new Uint8Array(value);
              total += chunk.byteLength;
              if (total > MAX_REQUEST_BODY_BYTES) {
                await reader.cancel();
                throw new PayloadTooLargeError();
              }

              chunks.push(chunk);
            }
          } finally {
            reader.releaseLock();
          }

          if (chunks.length === 0) {
            return new Uint8Array(0);
          }

          if (chunks.length === 1) {
            return chunks[0];
          }

          const bytes = new Uint8Array(total);
          let offset = 0;
          for (const chunk of chunks) {
            bytes.set(chunk, offset);
            offset += chunk.byteLength;
          }

          return bytes;
        }

        class PayloadTooLargeError extends Error {}
        """;

    private const string TcpStub =
        """
        // Stub for wasi:sockets/tcp@0.2.0.
        // .NET NativeAOT imports this interface at the WASM ABI level but the app never calls TCP sockets.
        // The real preview2-shim/sockets depends on Node.js worker-thread APIs unavailable in Cloudflare Workers.
        export class TcpSocket {}
        """;

    private const string UdpStub =
        """
        // Stub for wasi:sockets/udp@0.2.0.
        // .NET NativeAOT imports this interface at the WASM ABI level but the app never calls UDP sockets.
        // The real preview2-shim/sockets depends on Node.js worker-thread APIs unavailable in Cloudflare Workers.
        export class UdpSocket {}
        export class IncomingDatagramStream {}
        export class OutgoingDatagramStream {}
        """;
}
