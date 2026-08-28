#!/usr/bin/env node
// One-line installer for 5head-mcp — no npm, no registry, no git clone.
//
//   curl -sL https://github.com/TillmanBuildsTech/5head/releases/latest/download/install.js | node
//
// Downloads the pre-built native binary for this platform from the latest
// GitHub Release and installs it to ~/.5head/bin/. Nothing else is installed;
// the binary IS the executable, so point your MCP client straight at it.
//
// This file is fully self-contained on purpose: it is served as a single
// Release asset and must not require any sibling files or a package.json.
"use strict";

const { createWriteStream, mkdirSync, chmodSync } = require("fs");
const { homedir } = require("os");
const { join, resolve } = require("path");
const http = require("http");
const https = require("https");

const REPO = (process.env.FIVEHEAD_MCP_RELEASE_REPO || "TillmanBuildsTech/5head").trim();
const TAG = (process.env.FIVEHEAD_MCP_RELEASE_TAG || "latest").trim();
const BIN_DIR = process.env.FIVEHEAD_MCP_INSTALL_DIR
  ? resolve(process.env.FIVEHEAD_MCP_INSTALL_DIR)
  : join(homedir(), ".5head", "bin");

function getRid() {
  const p = process.platform;
  const a = process.arch;
  if (p === "darwin" && a === "arm64") return "osx-arm64";
  if (p === "darwin") return "osx-x64";
  if (p === "linux" && a === "arm64") return "linux-arm64";
  if (p === "linux") return "linux-x64";
  if (p === "win32") return "win-x64";
  throw new Error(`Unsupported platform: ${p}/${a}`);
}

function download(url, dest, cb) {
  const mod = url.startsWith("https:") ? https : http;
  const file = createWriteStream(dest);
  mod.get(url, (res) => {
    if (res.statusCode === 301 || res.statusCode === 302) {
      file.close();
      return download(res.headers.location, dest, cb);
    }
    if (res.statusCode !== 200) {
      file.close();
      return cb(new Error(`HTTP ${res.statusCode}`));
    }
    res.pipe(file);
    file.on("finish", () => file.close(cb));
  }).on("error", cb);
}

const rid = getRid();
const isWin = process.platform === "win32";
const ext = isWin ? ".exe" : "";
const binPath = join(BIN_DIR, `5head-mcp${ext}`);
const url =
  process.env.FIVEHEAD_MCP_RELEASE_URL ||
  `https://github.com/${REPO}/releases/download/${TAG}/5head-mcp-${rid}${ext}`;

console.log(`[5head-mcp] Platform: ${rid}`);
console.log(`[5head-mcp] Downloading ${url}`);
mkdirSync(BIN_DIR, { recursive: true });

download(url, binPath, (err) => {
  if (err) {
    console.error(`[5head-mcp] Download failed: ${err.message}`);
    console.error(`[5head-mcp]   ${url}`);
    console.error("[5head-mcp] Re-run to retry, or grab the binary manually from the GitHub Release.");
    process.exit(1);
  }
  if (!isWin) chmodSync(binPath, 0o755);
  console.log(`[5head-mcp] Installed to ${binPath}`);
  console.log("");
  console.log("Add it to your MCP client config:");
  console.log('  "command": "' + binPath.replace(/\\/g, "\\\\") + '", "args": []');
});
