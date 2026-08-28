# HTF Manager Development Handoff

This document is the preferred entry point when continuing development on a new workstation.

## Current baseline

- Project: **HTF Manager**
- Current version: **v0.3.5.1**
- Last verified state: `dotnet build HTFManager.slnx` succeeds and the application enters the UI.
- Solution: `HTFManager.slnx`
- App project: `src/HTFManager.App/HTFManager.App.csproj`
- Target framework: `.NET 10` (`net10.0`)
- UI: Avalonia `12.1.1`
- Primary platform: Windows 10/11
- Game: How to Fish
- Repository: `https://github.com/YungBurn/HTFManager`
- Main branch: `main`

## Build and run

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

Do not diagnose a new feature until the current baseline builds successfully.

## Current completed systems

### Application shell

The UI uses a persistent three-column layout:

- Left navigation: Home, Mods, Discover, Profiles, Configuration, Tools, Settings.
- Center: active page/workflow.
- Right: game status, Play, active profile, quick paths, operation status and loader health.

The application always starts on Home. Language is Chinese/English and persists between runs.

### Loader support

The manager detects both:

- BepInEx 5
- MelonLoader

Loader setup is not handled as a normal Mod installation. Loader installation has its own download, validation, staging, backup, commit, rollback and ownership flow. Existing unknown bootstrap DLLs must not be silently overwritten.

Mixed-loader environments are not assumed to be safe merely because both loaders can exist on disk.

### Mod lifecycle

Supported install inputs include local DLL/ZIP packages and supported Thunderstore downloads. The shared install flow is:

```text
Package
→ Package Inspector
→ Installation Plan
→ Dependency / conflict checks
→ Staging
→ Transactional installation
→ Ownership recording
```

Managed Mods can be safely updated/uninstalled because HTF Manager tracks their owned files. External/manual Mods remain visible but are not silently taken over or deleted.

### Package Inspector

Package Inspector is the confirmation/safety boundary for normal Mod installs. It identifies loader/component type, source, destination, dependencies, conflicts and risk. It prevents path traversal and unsafe target writes.

Never weaken these protections for Developer Mode.

### Configuration Center

Configuration Center supports BepInEx `.cfg` files and MelonLoader loader configuration. It preserves source formatting as much as possible and creates backups before saves.

Chinese mode only uses reviewed local mappings for known sources such as BepInEx and MelonLoader. Unknown third-party Mod configuration is not AI translated and is not guessed.

Configuration safety acknowledgement is stored locally and Developer Mode controls detailed information visibility only; it does not disable safety checks.

### Profiles

Profiles store desired Mod enabled/disabled state. Removing a Mod from a profile does not uninstall the Mod. Deleting a profile does not delete Mod files.

v0.3.4 added optional Mod configuration snapshots. Profile application backs up current configuration before restoring snapshot content and can roll back configuration/state changes on failure.

Loader-wide settings such as `BepInEx.cfg` or MelonLoader `Loader.cfg` are intentionally excluded from normal profile Mod snapshots.

### Portable profiles

v0.3.5 added `.htfprofile` export/import. Portable profiles contain:

- Mod references
- desired enabled states
- source / loader / component / version metadata
- optional profile configuration snapshots

They do not contain Mod DLLs, Mod archives, loader binaries or game files.

Import validates ZIP paths, schema, limits, hashes and configuration-to-Mod associations before creating a local profile. Missing Mods stay as unresolved requirements. A profile with unresolved requirements cannot be applied until the requirements are resolved or explicitly removed.

v0.3.5.1 fixes the `PortableProfileManifest.ExportedWithVersion` compile-time self-reference introduced in v0.3.5.

## Data locations

HTF Manager state is stored under:

```text
%LOCALAPPDATA%\HTFManager\
```

Important categories include:

- `settings.json`
- `profiles/*.json`
- Mod ownership/install registry
- Loader ownership registry
- package/download cache
- configuration backups
- profile snapshots
- profile recovery transactions

Do not copy these runtime files into the source repository unless a specific bug investigation explicitly requires a sanitized sample.

## Safety invariants

Future changes must preserve these rules:

1. Never treat `How to Fish.exe`, `UnityPlayer.dll`, game managed assemblies, or BepInEx core files as ordinary Mod targets.
2. Never silently overwrite unknown loader/bootstrap DLLs such as `winhttp.dll` or `version.dll`.
3. Never uninstall external/manual Mods through ownership operations that HTF Manager cannot prove.
4. Never write archive paths outside the validated destination root (ZipSlip/path traversal protection).
5. Preserve user configuration by default during Mod/loader removal.
6. Do not use `Assembly.Load` on untrusted Mod assemblies for package inspection; use static metadata/PE inspection.
7. Do not force-translate unknown third-party configuration keys/descriptions.
8. Developer Mode exposes diagnostics; it never disables install/configuration safety rules.
9. Profiles alter desired state; profile membership changes are not Mod install/uninstall operations.
10. Portable profiles do not redistribute third-party binaries or game files.

## Known UI implementation concern

Dynamic Avalonia pages that depend on application resources must render after attachment to the visual tree. Previous regressions were caused by rendering too early, where simple text appeared but resource-driven icons/badges did not. Keep the `AttachedToVisualTree` lifecycle guard when changing dynamic Mods/Profiles/Discover-style pages.

Other historical Avalonia/C# regressions to avoid:

- use `this.FindResource(...)`, not naked `FindResource(...)`;
- Avalonia 12 uses `ZIndex="..."`, not `Panel.ZIndex="..."`;
- use `PlaceholderText`, not obsolete `Watermark`;
- namespace `HTFManager.Infrastructure.System` can shadow `System`; use `global::System...` when needed.

## Next planned version

### v0.3.6 — Profile Restore Assistant

Goal: turn an imported `.htfprofile` with missing requirements into a guided restoration plan.

Expected behavior:

```text
Portable profile
→ match installed Mods
→ classify missing requirements
→ resolve supported Thunderstore PackageKeys
→ show restoration/install plan
→ install available requirements through existing Package Inspector pipeline
→ re-match profile
→ apply only when complete
```

Do not create a second Mod installer for this feature. Reuse the existing Thunderstore, Package Inspector, dependency/conflict, staging, rollback and ownership services.

Nexus integration should remain a later provider/integration step rather than being embedded directly into profile logic.

## Cross-session workflow

For a new ChatGPT conversation, provide the public GitHub repository URL and ask the assistant to read this file, `PROJECT_STATE.json`, the current patch notes, and the architecture document before proposing code changes.

If repository access is not practical, generate a source-only handoff archive with:

```powershell
.\build\export-handoff.ps1
```

The handoff archive should contain tracked source/docs only, not `bin/`, `obj/`, local runtime data, game files or downloaded Mod/loader binaries.
