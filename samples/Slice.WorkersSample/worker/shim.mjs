import { runSliceIpc } from "./component-runner.mjs";

export default {
  async fetch(request) {
    try {
      const requestLine = `${JSON.stringify(await toIpcRequest(request))}\n`;
      const response = JSON.parse(await runSliceIpc(requestLine));
      return fromIpcResponse(response);
    } catch (error) {
      console.error(error);
      return new Response("Internal Server Error", {
        status: 500,
        headers: { "Content-Type": "text/plain; charset=utf-8" },
      });
    }
  },
};

async function toIpcRequest(request) {
  const url = new URL(request.url);
  const body = request.method === "GET" || request.method === "HEAD"
    ? null
    : arrayBufferToBase64(await request.arrayBuffer());

  return {
    method: request.method,
    path: url.pathname,
    headers: Object.fromEntries(request.headers.entries()),
    query: url.search.length > 1 ? url.search.slice(1) : null,
    body,
  };
}

function fromIpcResponse(message) {
  const headers = new Headers(message.headers ?? {});
  const body = message.body ? base64ToUint8Array(message.body) : null;
  return new Response(body, {
    status: message.status ?? 500,
    headers,
  });
}

function arrayBufferToBase64(buffer) {
  const bytes = new Uint8Array(buffer);
  let binary = "";

  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary);
}

function base64ToUint8Array(value) {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);

  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }

  return bytes;
}
