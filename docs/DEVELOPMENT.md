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

## v0.3.7 portable bundle validation

Before treating the full v0.3.7 implementation as release-ready, validate both automated tests and a two-environment smoke flow:

```text
Machine/profile A
→ create/capture a profile
→ Share Profile → Full portable bundle
→ verify a .htfbundle is created

Receiver environment B
→ open/import the .htfbundle
→ confirm the embedded .htfprofile is inspected before any Mod installation
→ confirm already-healthy Mods are not offered for reinstall
→ confirm Missing + bundled exact Mods can enter Package Inspector
→ confirm VersionMismatch is warning-only and is not automatically replaced
→ verify a manifest-less managed BepInEx local ZIP/DLL with a unique BepInPlugin GUID/version is eligible for Full share
→ verify duplicate/ambiguous intrinsic identities are not auto-matched/bundled
→ install one bundled Missing Mod through Package Inspector
→ confirm profile health/restore state refreshes
→ confirm applying the profile remains a separate explicit action
```

The final v0.3.7 automated suite is expected to contain 54 tests after the intrinsic-local-identity release enhancement. Generated `*.htfprofile` and `*.htfbundle` files are local artifacts and should not be committed accidentally. Bundle security/unit tests must remain part of the normal `dotnet test` run.

## Creating a handoff archive

Once the repository is committed, run:

```powershell
.\build\export-handoff.ps1
```

This uses `git archive`, so only tracked repository content is included. Build output and ignored local files are excluded automatically.
