# HTF Manager Development Handoff

This document is the preferred entry point when development continues in a new ChatGPT conversation or on another workstation.

## Current baseline

- Project: **HTF Manager**
- Current version: **v0.3.7 release candidate**
- Last verified state: the v0.3.7 full bundle/health implementation passed local Windows/.NET 10 build/tests/runtime before the final intrinsic-local-identity release overlay. Re-run the release gate after applying the final overlay before merge/tag.
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

### Portable profiles and bundles

v0.3.5 introduced lightweight `.htfprofile` export/import. v0.3.7 keeps that metadata-only format and adds `.htfbundle` for explicit full portable sharing.

Profiles now preserve durable expected Mod identity/version metadata and can report `Healthy`, `Missing`, `VersionMismatch`, or `IdentityUncertain`. Full bundles contain the embedded `.htfprofile` plus only eligible verified HTF-managed source artifacts; opening/importing a bundle never installs payloads automatically.

Identity precedence is provider `PackageKey` first, then deterministic local `IntrinsicId` where available, then conservative local bindings/name/file matching. BepInEx local DLL/ZIP packages can expose their `BepInPlugin` GUID/name/version through static PE metadata inspection without loading the assembly. This allows a managed package such as `TrueDotCrosshair` to remain a local package (`PackageKey = null`) while still being safely shareable when its exact version and retained source artifact are verified.

Bundle restore is profile-first and only offers a bundled payload when the requirement is actually `Missing`. Healthy requirements suppress duplicate installation and `VersionMismatch` never triggers automatic replacement in v0.3.7. Payloads are lazily extracted, size/hash/identity checked, and then passed to the existing Package Inspector/transactional installer.

### Profile Restore Assistant

v0.3.6 adds guided restoration for unresolved portable-profile requirements. Automatic planning is limited to exact Thunderstore `PackageKey` matches. Requested versions are preferred; unavailable requested versions become explicit `VersionFallback` items and require user acknowledgement before Package Inspector can be opened.

Restore Assistant never installs silently. It reuses the existing Package Inspector/install pipeline, refreshes Mods after a successful install, re-runs `ResolveMissingMods`, and never automatically applies the profile. Local/external requirements remain manual.

The application now also includes its custom HTF Manager logo for the executable, main window/taskbar, and sidebar header.

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
10. Lightweight `.htfprofile` files never redistribute binaries. `.htfbundle` may carry only explicitly eligible verified HTF-managed Mod source artifacts; never include game files, loaders, ambiguous local packages, or external/unmanaged Mods automatically.

## Known UI implementation concern

Dynamic Avalonia pages that depend on application resources must render after attachment to the visual tree. Previous regressions were caused by rendering too early, where simple text appeared but resource-driven icons/badges did not. Keep the `AttachedToVisualTree` lifecycle guard when changing dynamic Mods/Profiles/Discover-style pages.

Other historical Avalonia/C# regressions to avoid:

- use `this.FindResource(...)`, not naked `FindResource(...)`;
- Avalonia 12 uses `ZIndex="..."`, not `Panel.ZIndex="..."`;
- use `PlaceholderText`, not obsolete `Watermark`;
- namespace `HTFManager.Infrastructure.System` can shadow `System`; use `global::System...` when needed.

## Release validation status

The v0.3.7 feature implementation previously passed local restore/build/test/runtime validation. The final intrinsic-identity/release overlay changes package metadata parsing and version metadata, so the release gate must be re-run before merge/tag:

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet test --project .\tests\HTFManager.Tests\HTFManager.Tests.csproj --configuration Release --no-build --no-restore
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

Expected automated suite after the intrinsic-identity enhancement: **54 tests**. Also verify one manifest-less BepInEx local ZIP/DLL with a deterministic `BepInPlugin` GUID can be captured and included in Full Share, while a duplicate/ambiguous intrinsic identity remains non-automatic.

## Next planned version

### v0.3.8 — Profile Version Reconciliation

Goal: act on the `VersionMismatch` health state that v0.3.7 deliberately detects but does not repair.

Expected direction:

```text
VersionMismatch
→ exact expected-version availability
→ explicit user choice
→ Package Inspector / ownership-safe replacement
→ refresh and re-check health
```

No silent downgrade/upgrade. External/manual Mods must not be taken over just to satisfy a profile. A second explicit action may allow the user to adopt the currently installed deterministic version as the profile baseline, but that is a v0.3.8 concern. Nexus remains a later provider integration.

## Cross-session workflow

For a new ChatGPT conversation, provide the public GitHub repository URL and ask the assistant to read this file, `PROJECT_STATE.json`, the current patch notes, and the architecture document before proposing code changes.

If repository access is not practical, generate a source-only handoff archive with:

```powershell
.\build\export-handoff.ps1
```

The handoff archive should contain tracked source/docs only, not `bin/`, `obj/`, local runtime data, game files or downloaded Mod/loader binaries.
