import { _setArgs, _setStderr, _setStdin, _setStdout } from "@bytecodealliance/preview2-shim/cli";
import { $init, run } from "./component/slice-workers-sample.js";

const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder();
const initialized = $init;

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
  await initialized;

  const stdinBytes = textEncoder.encode(requestLine);
  const stdoutChunks = [];
  const stderrChunks = [];

  _setArgs(["Slice.WorkersSample"]);
  _setStdin(createInputHandler(stdinBytes));
  _setStdout(createOutputHandler(stdoutChunks));
  _setStderr(createOutputHandler(stderrChunks));

  run.run();

  const stderr = decodeChunks(stderrChunks);
  if (stderr) {
    console.error(stderr);
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
