import { readdir, writeFile } from "node:fs/promises";
import { join } from "node:path";

const componentDir = new URL("./component/", import.meta.url);
const files = (await readdir(componentDir))
  .filter(file => /^slice-wasi-sample\.core\d*\.wasm$/.test(file))
  .sort(compareCoreModuleNames);

if (files.length === 0) {
  throw new Error("No jco core WASM modules found in component/.");
}

const imports = files.map((file, index) => `import core${index} from "./${file}";`).join("\n");
const entries = files.map((file, index) => `  ["${file}", core${index}],`).join("\n");

await writeFile(
  join(componentDir.pathname, "modules.mjs"),
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
  const match = /^slice-wasi-sample\.core(\d*)\.wasm$/.exec(file);
  if (match === null) {
    return Number.MAX_SAFE_INTEGER;
  }

  return match[1] === "" ? 1 : Number.parseInt(match[1], 10);
}
