#!/usr/bin/env node
// Downloads the correct pre-built native binary from GitHub Releases on first install.
"use strict";

const { copyFileSync, existsSync, mkdirSync, chmodSync, rmSync } = require("fs");
const { basename, join, resolve } = require("path");
const https = require("https");
const fs = require("fs");

const VERSION = require("../package.json").version;
const PACKAGE = require("../package.json");
const REPO = resolveRepo();
const BIN_DIR = join(__dirname, "..", "bin");

function getRid() {
  const p = process.platform;
  const a = process.arch;
  if (p === "darwin" && a === "arm64") return "osx-arm64";
  if (p === "darwin")                  return "osx-x64";
  if (p === "linux"  && a === "arm64") return "linux-arm64";
  if (p === "linux")                   return "linux-x64";
  if (p === "win32")                   return "win-x64";
  throw new Error(`Unsupported platform: ${p}/${a}`);
}

const rid      = getRid();
const ext      = process.platform === "win32" ? ".exe" : "";
const binName  = `5head-mcp${ext}`;
const binPath  = join(BIN_DIR, binName);
const assetName = `5head-mcp-${rid}${ext}`;
const url      = REPO ? `https://github.com/${REPO}/releases/download/v${VERSION}/${assetName}` : null;

function resolveRepo() {
  const envRepo = process.env.FIVEHEAD_MCP_RELEASE_REPO?.trim();
  if (envRepo) {
    return envRepo.replace(/^https:\/\/github\.com\//, "").replace(/\.git$/, "");
  }

  const repositoryUrl = PACKAGE.repository?.url;
  const match = typeof repositoryUrl === "string"
    ? repositoryUrl.match(/github\.com[:/]([^/]+\/[^/.]+)(?:\.git)?$/)
    : null;

  return match?.[1] ?? null;
}

function finalize(dest) {
  if (process.platform !== "win32") chmodSync(dest, 0o755);
  console.log("[5head-mcp] Ready.");
}

mkdirSync(BIN_DIR, { recursive: true });

const localBinary = process.env.FIVEHEAD_MCP_LOCAL_BINARY?.trim();
if (localBinary) {
  const sourcePath = resolve(localBinary);
  if (!existsSync(sourcePath)) {
    console.error(`[5head-mcp] FIVEHEAD_MCP_LOCAL_BINARY not found: ${sourcePath}`);
    process.exit(1);
  }

  copyFileSync(sourcePath, binPath);
  console.log(`[5head-mcp] Copied local binary from ${sourcePath}`);
  finalize(binPath);
  process.exit(0);
}

if (!url) {
  console.error(
    "[5head-mcp] Could not determine the GitHub release repository. " +
    "Set FIVEHEAD_MCP_RELEASE_REPO=owner/repo before publishing or installing."
  );
  process.exit(1);
}

console.log(`[5head-mcp] Downloading binary for ${rid} from:\n  ${url}`);

function download(url, dest, cb) {
  const file = fs.createWriteStream(dest);
  https.get(url, (res) => {
    if (res.statusCode === 302 || res.statusCode === 301) {
      file.close();
      return download(res.headers.location, dest, cb);
    }
    if (res.statusCode !== 200) {
      file.close();
      rmSync(dest, { force: true });
      return cb(new Error(`HTTP ${res.statusCode}`));
    }
    res.pipe(file);
    file.on("finish", () => file.close(cb));
  }).on("error", cb);
}

download(url, binPath, (err) => {
  if (err) {
    console.error("[5head-mcp] Download failed:", err.message);
    process.exit(1);
  }
  finalize(binPath);
});
