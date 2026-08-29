# Development Guide

## Prerequisites

- Windows 10/11
- .NET 10 SDK
- Git
- VS Code or another C#/.NET IDE

## Verify the baseline

```powershell
dotnet --version
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet test --project tests/HTFManager.Tests/HTFManager.Tests.csproj --configuration Release --no-build --no-restore
```

Run:

```powershell
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

## Repository rules

Do not commit:

- `bin/` or `obj/`;
- publish/test output;
- local HTF Manager runtime data;
- game executables/assemblies/assets;
- BepInEx or MelonLoader runtime binaries downloaded for testing;
- third-party Mod DLL/ZIP files unless redistribution is explicitly permitted and intentionally part of a test fixture.

The `.gitignore` covers normal generated output, but review `git status` before every commit.

## Version handoff files

When a stable baseline changes, update:

- `VERSION`
- `PROJECT_STATE.json`
- `SESSION_HANDOFF.md`
- `README.md`
- relevant patch notes
- visible application version when appropriate

## Incremental feature package workflow

For local-first development, prefer small baseline-specific ZIP overlays containing only new or changed repository files. Do not include `bin/`, `obj/`, caches, game/runtime data, or unchanged source files. A package should identify the baseline it expects.

Before distributing a package:

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet test --project tests/HTFManager.Tests/HTFManager.Tests.csproj --configuration Release --no-build --no-restore
```

Then inspect the UI path affected by the patch.

## v0.3.8 release validation

Treat v0.3.8 as release-ready only after a **clean** Windows/.NET 10 build. ZIP overlay timestamps can make an incremental build reuse stale outputs, so if new model members appear missing, delete project `bin/`/`obj/` and rebuild before diagnosing the source.

```powershell
dotnet clean HTFManager.slnx --configuration Release
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet test --project tests/HTFManager.Tests/HTFManager.Tests.csproj --configuration Release --no-build --no-restore
```

The v0.3.8 implementation candidate contains **71 tests**.

### Version reconciliation smoke flow

Validate at least one deterministic provider package and one managed local `IntrinsicId` package:

```text
Profile expects 1.0.0
→ machine has 2.0.0
→ Health = VersionMismatch
→ Restore expected version
→ exact bundle/history/Thunderstore source only
→ Package Inspector appears before replacement
→ install transaction completes
→ Health = Healthy
```

Also verify **Accept installed version** requires explicit confirmation, rewrites only the intended profile expectation, and does not fabricate/take over a different identity. If only a non-exact remote version exists, reconciliation must remain unavailable/manual; do not reuse the missing-Mod `VersionFallback` behavior.

For historical-artifact coverage, install/update two versions of the same managed deterministic Mod and confirm `%LOCALAPPDATA%\HTFManager\packages\history` can satisfy the earlier exact version after the current installation changes. A Full `.htfbundle` export may include that exact historical expected version even while current health is `VersionMismatch`.

### Build and smoke-test the published EXE

After tests pass:

```powershell
.\build\publish-win-x64.ps1
```

Expected files:

```text
artifacts/release/v0.3.8/
├─ HTFManager.exe
├─ update-manifest.json
└─ SHA256SUMS.txt
```

Run `HTFManager.exe` directly. Do not use `dotnet run` as the executable-release smoke test. Confirm icon/startup/local-data/profile paths work and Settings reports current version `0.3.8`.

Check the generated manifest asset name, byte size and SHA-256 against the EXE. `artifacts/` is ignored and must not be committed.

### Update smoke flow

The network updater can only perform a real newer-version flow after a stable GitHub Release newer than the running executable exists. Before that, unit tests cover parsing/download/hash failures and Settings should report the current GitHub stable release as not newer.

For an actual future `0.3.8 → 0.3.9` test:

```text
published HTFManager.exe 0.3.8
→ Check now
→ 0.3.9 stable manifest found
→ Download update
→ SHA-256 verified
→ Restart and update
→ old EXE backed up/replaced
→ new EXE starts as 0.3.9
```

Do not test self-replacement from `dotnet run`/folder framework output; the v0.3.8 applier intentionally enables automatic replacement only for the published single-file executable in a writable directory.

### Tag/release automation

`release.yml` requires a tag matching `VERSION`. Before pushing `v0.3.8`, make sure `VERSION` is `0.3.8`, local build/tests/publish are green, and `PATCH_NOTES_v0.3.8.md` is final. The workflow reruns restore/build/tests and generates the release assets.

## Creating a handoff archive

Once the repository is committed, run:

```powershell
.\build\export-handoff.ps1
```

This uses `git archive`, so only tracked repository content is included. Build output and ignored local files are excluded automatically.
