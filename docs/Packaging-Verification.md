# Packaging Verification

## Goal

Prove checklist item 8 end-to-end for the current V1 packaging shape:

- local native binary run flow works
- npm wrapper flow works
- release artifact assumptions are explicit
- install docs match the current implementation

## Current Packaging Contract

- Native host assembly name: `5head-mcp`
- npm package entrypoint: `npm/bin/run.js`
- npm-installed binary target: `npm/bin/5head-mcp` or `npm/bin/5head-mcp.exe`
- npm installer: `npm/scripts/install.js`
- expected release repo: `brandon/5head`
- expected release asset names:
  - `5head-mcp-osx-arm64`
  - `5head-mcp-osx-x64`
  - `5head-mcp-linux-x64`
  - `5head-mcp-linux-arm64`
  - `5head-mcp-win-x64.exe`

The npm package is a thin wrapper around a prebuilt native AOT binary. It does not build from source during install.
The published npm tarball should not ship a platform binary ahead of time.

## Verified Locally

Environment used:

- Node `v25.2.1`
- npm `11.6.2`
- .NET SDK `10.0.100`
- host platform: macOS arm64

Commands run:

```sh
dotnet publish src/FiveHead.Mcp.Host/FiveHead.Mcp.Host.csproj -c Release -r osx-arm64 --self-contained true /p:PublishAot=true -o ./publish/osx-arm64
mkdir -p tmp/npm-e2e
npm install ../../npm
FIVEHEAD_MCP_LOCAL_BINARY="/Users/brandon/Projects/5head/publish/osx-arm64/5head-mcp" npm install ../../npm
./node_modules/.bin/5head-mcp
./node_modules/.bin/5head-mcp --help
./publish/osx-arm64/5head-mcp > /tmp/5head-stdout.log 2> /tmp/5head-stderr.log & pid=$!; sleep 2; kill $pid; wait $pid
```

Observed results:

- `dotnet publish` produced `publish/osx-arm64/5head-mcp`
- npm postinstall successfully copied the local binary into installed package `bin/`
- `./node_modules/.bin/5head-mcp` launched the native server process through the Node wrapper
- the process stayed alive as expected for an MCP stdio server until terminated by timeout
- `--help` is not a supported CLI mode today; the process still starts the server rather than printing usage
- `npm pack` now produces a thin tarball with `bin/run.js` and `scripts/install.js`, rather than embedding a native binary from the source tree
- the published native binary stayed silent on startup: `stdout=0 bytes`, `stderr=0 bytes` during a launch/terminate probe, which matches MCP stdio expectations

## Wrapper And Installer Assumptions

- `npm/bin/run.js` assumes the native binary already exists in the package `bin/` directory.
- `npm/scripts/install.js` is responsible for making that true.
- For local verification or development, `FIVEHEAD_MCP_LOCAL_BINARY` can point at a published binary and bypass release downloads.
- For real releases, installer downloads are derived from the npm package version and GitHub repo metadata.

## Release Artifact Assumptions

The release process must publish one asset per RID using the exact filenames above.

Current CI evidence:

- `.github/workflows/ci.yml` publishes Native AOT outputs for `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`, and `osx-x64`
- artifact names in CI match the npm installer naming convention

Current gap:

- CI uploads build artifacts but this repo does not yet show a release workflow that attaches them to a GitHub Release tag `v<package.json version>`

So packaging is locally provable now, but successful public `npx 5head-mcp` installs still depend on that release publication step existing in practice.

## Doc Reality Check

README now matches the implementation more closely:

- install depends on npm postinstall, not runtime caching logic in the wrapper
- release repo is `brandon/5head`, not a placeholder
- local proof path is documented
- expected artifact filenames are documented explicitly

## V1 Verdict

Packaging is credible for V1. The local binary flow, npm wrapper flow, and stdio-silence requirement are now all proven locally. The remaining release risk is operational: GitHub Releases still need to publish the expected assets to `brandon/5head` tags that match npm package versions.
