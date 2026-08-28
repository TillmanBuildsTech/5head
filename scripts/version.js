#!/usr/bin/env node
// Bumps the release version in every file that pins it.
//
// The canonical version source is the Host csproj <Version> tag. This script
// reads it, computes the next version, and writes it back to every csproj.
//
// Usage:
//   node scripts/version.js [patch|minor|major]   (default: patch)
//
// Prints the new version on stdout. Requires Node >= 18.
"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, ".."); // repo root
const HOST_CSPROJ = path.join(root, "src", "FiveHead.Mcp.Host", "FiveHead.Mcp.Host.csproj");
const csprojPaths = [
  HOST_CSPROJ,
  path.join(root, "src", "FiveHead.Mcp.Core", "FiveHead.Mcp.Core.csproj"),
];

const versionRe = /<Version>(\d+)\.(\d+)\.(\d+)<\/Version>/;
const host = fs.readFileSync(HOST_CSPROJ, "utf8");
const m = versionRe.exec(host);
if (!m) {
  console.error("[5head-mcp] No <Version>x.y.z</Version> found in the Host csproj.");
  process.exit(1);
}
const [maj, min, pat] = [Number(m[1]), Number(m[2]), Number(m[3])];

const bump = (process.argv[2] || "patch").toLowerCase();
let next;
if (bump === "major") next = `${maj + 1}.0.0`;
else if (bump === "minor") next = `${maj}.${min + 1}.0`;
else if (bump === "patch") next = `${maj}.${min}.${pat + 1}`;
else {
  console.error(`[5head-mcp] Unknown bump type "${bump}" (expected patch|minor|major)`);
  process.exit(1);
}

const re = /<Version>\d+\.\d+\.\d+<\/Version>/;
for (const p of csprojPaths) {
  let s = fs.readFileSync(p, "utf8");
  if (!re.test(s)) {
    console.error(`[5head-mcp] No <Version> tag found in ${path.relative(root, p)}`);
    process.exit(1);
  }
  fs.writeFileSync(p, s.replace(re, `<Version>${next}</Version>`));
}

console.log(next);
