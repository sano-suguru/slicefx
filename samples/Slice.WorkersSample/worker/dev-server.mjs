import { spawn } from "node:child_process";
import { createServer } from "node:http";
import { existsSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const wasmPath = resolve(__dirname, "slice-workers-sample.wasm");
const port = Number.parseInt(process.env.PORT ?? "8787", 10);

if (!existsSync(wasmPath)) {
  throw new Error(`WASM component not found at ${wasmPath}. Run: npm run build`);
}

const server = createServer(async (req, res) => {
  try {
    const requestLine = `${JSON.stringify(await toIpcRequest(req))}\n`;
    const responseLine = await runWasiCommand(requestLine);
    const response = JSON.parse(responseLine);
    const body = response.body ? Buffer.from(response.body, "base64") : Buffer.alloc(0);

    res.writeHead(response.status ?? 500, response.headers ?? {});
    res.end(body);
  } catch (error) {
    console.error(error);
    res.writeHead(500, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Internal Server Error");
  }
});

server.listen(port, () => {
  console.log(`Slice Workers dev server listening on http://localhost:${port}`);
});

async function toIpcRequest(req) {
  const url = new URL(req.url ?? "/", `http://${req.headers.host ?? "localhost"}`);
  const body = req.method === "GET" || req.method === "HEAD"
    ? null
    : (await readRequestBody(req)).toString("base64");

  return {
    method: req.method ?? "GET",
    path: url.pathname,
    headers: Object.fromEntries(Object.entries(req.headers).map(([key, value]) => [
      key,
      Array.isArray(value) ? value.join(", ") : value ?? "",
    ])),
    query: url.search.length > 1 ? url.search.slice(1) : null,
    body,
  };
}

function readRequestBody(req) {
  return new Promise((resolveBody, reject) => {
    const chunks = [];

    req.on("data", (chunk) => chunks.push(chunk));
    req.on("end", () => resolveBody(Buffer.concat(chunks)));
    req.on("error", reject);
  });
}

function runWasiCommand(stdin) {
  return new Promise((resolveRun, reject) => {
    const child = spawn("npx", ["jco", "run", wasmPath], {
      cwd: __dirname,
      shell: process.platform === "win32",
      stdio: ["pipe", "pipe", "pipe"],
    });

    let stdout = "";
    let stderr = "";

    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (chunk) => {
      stdout += chunk;
    });
    child.stderr.on("data", (chunk) => {
      stderr += chunk;
    });
    child.on("error", reject);
    child.on("close", (code) => {
      if (code !== 0) {
        reject(new Error(`jco run exited with ${code}: ${stderr}`));
        return;
      }

      const line = stdout.split(/\r?\n/).find((value) => value.length > 0);
      if (!line) {
        reject(new Error(`jco run produced no response. stderr: ${stderr}`));
        return;
      }

      resolveRun(line);
    });

    child.stdin.end(stdin);
  });
}
