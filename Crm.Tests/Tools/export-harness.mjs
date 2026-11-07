// Runs tools/crm-browser-export.js UNCHANGED under node, against a fake CRM that answers from a scenario file,
// and prints the document the script would have downloaded. Test-only: it reads no network and is never shipped.
//
//   node export-harness.mjs <script> <scenario.json>
//
// The scenario is { "routes": { "<request path prefix>": <JSON body> } }, matched longest prefix first against
// the path after the Web API root, query string included. Anything unrouted answers an empty collection, which is
// what CRM answers for a table with no rows; WhoAmI and the signed-in user have answers of their own.
//
// The fake answers ONLY a GET. A request carrying any other method fails the run: the script's read-only promise
// ("search this file for method: there is none") is checked on every request it makes, not by reading it.
import { readFileSync } from "node:fs";

const [scriptPath, scenarioPath] = process.argv.slice(2);
const routes = JSON.parse(readFileSync(scenarioPath, "utf8")).routes ?? {};
const keys = Object.keys(routes).sort((left, right) => right.length - left.length);
const origin = "https://crm.test";
const root = origin + "/api/data/v8.2/";

const defaults = {
  "WhoAmI()": { UserId: "00000000-0000-0000-0000-00000000aaaa" },
  "systemusers(": { fullname: "Test Kullanıcısı", domainname: "TEST\\kullanici" }
};

let failure = null;
let downloaded = null;

globalThis.location = { origin };
globalThis.CompressionStream = undefined;   // the plain file, so the harness need not gunzip it
globalThis.fetch = async (url, options) => {
  if (options?.method && options.method !== "GET") {
    failure = `a ${options.method} was sent to ${url}`;
    throw new Error(failure);
  }
  const path = String(url).startsWith(root) ? String(url).slice(root.length) : String(url);
  const key = keys.find(candidate => path.startsWith(candidate));
  let body;
  if (key !== undefined) {
    body = routes[key];
  } else if (path.endsWith("$count")) {
    body = 0;
  } else {
    const fallback = Object.keys(defaults).find(candidate => path.startsWith(candidate));
    body = fallback !== undefined ? defaults[fallback] : { value: [] };
  }
  return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
};
URL.createObjectURL = blob => {
  downloaded = blob;
  return "blob:harness";
};
globalThis.document = {
  createElement: () => ({ click() {}, remove() {} }),
  body: { appendChild() {} }
};
console.log = (...args) => process.stderr.write(args.filter(arg => !String(arg).startsWith("color:")).join(" ") + "\n");
console.error = (...args) => {
  failure = args.join(" ");
  process.stderr.write(failure + "\n");
};

// The script is one expression statement: an async IIFE with a .catch. Its value is that promise.
await (0, eval)(readFileSync(scriptPath, "utf8"));

if (failure !== null) {
  process.stderr.write("HARNESS: " + failure + "\n");
  process.exit(1);
}
if (downloaded === null) {
  process.stderr.write("HARNESS: the script ended without saving a file\n");
  process.exit(1);
}
process.stdout.write(await downloaded.text());
