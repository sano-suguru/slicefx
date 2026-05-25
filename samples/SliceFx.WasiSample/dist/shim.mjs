// Cloudflare Workers fetch handler for the Slice wasi:http/incoming-handler component.
// The wasm component exports wasi:http/incoming-handler; jco transpiles it to an
// instantiate() function. We bridge Cloudflare's fetch(Request) ↔ wasi:http.

import { getCoreModule } from "./component/modules.mjs";
import { instantiate } from "./component/slice-wasi-sample.js";

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

// ── wasi:http/types host implementation ────────────────────────────────────
// We write our own instead of reusing preview2-shim/http because the Node.js
// version relies on internal factories (deleted after module init) and the
// browser version has no-op stubs for server-side resources.

const te = new TextEncoder();
const td = new TextDecoder();
const MAX_REQUEST_BODY_BYTES = 1024 * 1024;

// Fields – mutable header / trailer map.
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

// IncomingBody – wraps the raw request body bytes.
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
          if (n <= 0) return new Uint8Array(0); // EOF → empty → C# breaks loop
          const chunk = data.slice(pos, pos + n);
          pos += n;
          return chunk;
        },
      }),
    };
  }
  static finish(_body) {}
}

// IncomingRequest – constructed by the host from the Cloudflare Request.
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

// OutgoingBody – accumulates response body bytes written by the wasm component.
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

// OutgoingResponse – built by the wasm component and handed back via ResponseOutparam.
class OutgoingResponse {
  #status = 200; #headers; #body;
  constructor(headers) { this.#headers = headers; }
  setStatusCode(code) { this.#status = Number(code); }
  statusCode() { return this.#status; }
  headers() { return this.#headers; }
  body() { this.#body = new OutgoingBody(); return this.#body; }
  getBody() { return this.#body; }
}

// ResponseOutparam – one-shot slot; wasm calls ResponseOutparam.set() once.
class ResponseOutparam {
  #resolve; #reject;
  constructor(resolve, reject) { this.#resolve = resolve; this.#reject = reject; }
  static set(param, result) {
    if (result.tag === 'ok') param.#resolve(result.val);
    else param.#reject(new Error(`wasi:http error: ${JSON.stringify(result.val)}`));
  }
}

// ── Helper: convert Cloudflare method string → wasi:http Method variant ───

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

// ── Instantiate the component once at module load ──────────────────────────

_setArgs(["SliceFx.WasiSample"]);

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

// ── Cloudflare Workers fetch handler ──────────────────────────────────────

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
