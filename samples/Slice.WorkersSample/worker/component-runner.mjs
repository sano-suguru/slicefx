import {
  _setArgs, _setStderr, _setStdin, _setStdout,
  environment, exit as exitNamespace, stderr as stderrNamespace,
  stdin as stdinNamespace, stdout as stdoutNamespace,
  terminalInput, terminalOutput, terminalStderr, terminalStdin, terminalStdout,
} from "@bytecodealliance/preview2-shim/cli";
import { monotonicClock, wallClock } from "@bytecodealliance/preview2-shim/clocks";
import { preopens, types } from "@bytecodealliance/preview2-shim/filesystem";
import { error, poll, streams } from "@bytecodealliance/preview2-shim/io";
import { random } from "@bytecodealliance/preview2-shim/random";
import { TcpSocket } from "./stubs/tcp.js";
import { getCoreModule } from "./component/modules.mjs";
import { instantiate } from "./component/slice-workers-sample.js";

const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder();

const wasiImports = {
  "../stubs/tcp.js": { TcpSocket },
  "wasi:cli/environment": environment,
  "wasi:cli/exit": exitNamespace,
  "wasi:cli/stderr": stderrNamespace,
  "wasi:cli/stdin": stdinNamespace,
  "wasi:cli/stdout": stdoutNamespace,
  "wasi:cli/terminal-input": terminalInput,
  "wasi:cli/terminal-output": terminalOutput,
  "wasi:cli/terminal-stderr": terminalStderr,
  "wasi:cli/terminal-stdin": terminalStdin,
  "wasi:cli/terminal-stdout": terminalStdout,
  "wasi:clocks/monotonic-clock": monotonicClock,
  "wasi:clocks/wall-clock": wallClock,
  "wasi:filesystem/preopens": preopens,
  "wasi:filesystem/types": types,
  "wasi:io/error": error,
  "wasi:io/poll": poll,
  "wasi:io/streams": streams,
  "wasi:random/random": random,
};

const initialized = instantiate(getCoreModule, wasiImports).then(component => component.run);

let runQueue = Promise.resolve();

export function runSliceIpc(requestLine) {
  const work = runQueue.then(
    () => runSliceIpcCore(requestLine),
    () => runSliceIpcCore(requestLine),
  );
  runQueue = work.catch(() => {});
  return work;
}

async function runSliceIpcCore(requestLine) {
  const run = await initialized;

  const stdinBytes = textEncoder.encode(requestLine);
  const stdoutChunks = [];
  const stderrChunks = [];

  _setArgs(["Slice.WorkersSample"]);
  _setStdin(createInputHandler(stdinBytes));
  _setStdout(createOutputHandler(stdoutChunks));
  _setStderr(createOutputHandler(stderrChunks));

  run.run();

  const stderrText = decodeChunks(stderrChunks);
  if (stderrText) {
    console.error(stderrText);
  }

  const line = firstLine(decodeChunks(stdoutChunks));
  if (!line) {
    throw new Error("WASI component produced no response.");
  }

  return line;
}

function createInputHandler(bytes) {
  let offset = 0;

  return {
    blockingRead(length) {
      if (offset >= bytes.length) {
        return new Uint8Array();
      }

      const end = Math.min(offset + Number(length), bytes.length);
      const chunk = bytes.slice(offset, end);
      offset = end;
      return chunk;
    },
  };
}

function createOutputHandler(chunks) {
  return {
    write(contents) {
      chunks.push(contents.slice());
    },
    blockingFlush() {
    },
  };
}

function decodeChunks(chunks) {
  return textDecoder.decode(concatUint8Arrays(chunks));
}

function firstLine(text) {
  return text.split(/\r?\n/).find((line) => line.length > 0) ?? "";
}

function concatUint8Arrays(chunks) {
  const length = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
  const result = new Uint8Array(length);
  let offset = 0;

  for (const chunk of chunks) {
    result.set(chunk, offset);
    offset += chunk.length;
  }

  return result;
}
