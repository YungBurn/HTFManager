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
dotnet build HTFManager.slnx
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

## Feature patch workflow

Prefer small, baseline-specific patches. A feature patch should identify the version it expects and avoid copying unrelated generated files.

Before distributing a patch:

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx
```

Then inspect the UI path affected by the patch.

## Creating a handoff archive

Once the repository is committed, run:

```powershell
.\build\export-handoff.ps1
```

This uses `git archive`, so only tracked repository content is included. Build output and ignored local files are excluded automatically.
