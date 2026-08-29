# HTF Manager v0.3.6

A lightweight, game-specific Mod Manager and launcher for **How to Fish (渔力全开)**.

HTF Manager is built with **.NET 10**, **C#**, and **Avalonia 12**. It is designed around safe, reversible Mod management instead of directly modifying game assemblies.

## Current capabilities

- Detect the Steam installation of How to Fish.
- Detect and manage BepInEx 5 and MelonLoader environments.
- Install local DLL/ZIP Mods through Package Inspector and staging.
- Browse and install supported Thunderstore packages.
- Track managed Mod ownership separately from external/manual Mods.
- Enable, disable, update, and uninstall managed Mods safely.
- Inspect dependencies, destination paths, conflicts, and package risk before installation.
- Automatically set up supported Mod loaders with validation, backup, rollback, and ownership tracking.
- Edit BepInEx and MelonLoader configuration through the Configuration Center.
- Provide reviewed Chinese mappings for known loader configuration without AI/forced translation of unknown third-party settings.
- Maintain profiles containing Mod enabled/disabled state.
- Save optional per-profile Mod configuration snapshots with recovery and rollback.
- Export and import portable `.htfprofile` packages without redistributing Mod binaries or game files.
- Detect missing or version-mismatched Mods when importing a portable profile.
- Guide restoration of missing Thunderstore requirements through Profile Restore Assistant and the existing Package Inspector/install pipeline.

## Safety model

HTF Manager intentionally separates **managed** content from **external/manual** content. Unknown files are not silently taken over or deleted.

Normal Mod installation must not overwrite the game executable, `UnityPlayer.dll`, managed game assemblies, BepInEx core files, or unknown bootstrap DLLs. Loader installation uses a separate validated transaction path. Configuration and profile operations create recovery data before overwriting tracked configuration files.

Portable profiles contain references and optional configuration snapshots only. They do **not** bundle third-party Mod DLLs, Mod archives, BepInEx/MelonLoader binaries, or game files.

## Requirements

- Windows 10/11
- .NET 10 SDK for development/building
- How to Fish through Steam
- BepInEx 5 and/or MelonLoader depending on the Mods being used

Avalonia is currently pinned to **12.1.1**.

## Build

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx
```

Run the desktop application:

```powershell
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

The `main` branch is also checked by GitHub Actions using .NET 10 on Windows.

## Project structure

```text
src/
├─ HTFManager.App/             Avalonia UI, localization and composition
├─ HTFManager.Core/            Models and interfaces
└─ HTFManager.Infrastructure/  Game, loader, Mod, profile and storage services

docs/                          Architecture and feature design notes
build/                         Local development/release helper scripts
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for current subsystem boundaries and safety invariants.

## Local application data

HTF Manager stores its own state outside the game directory:

```text
%LOCALAPPDATA%\HTFManager\
```

This includes settings, profiles, Mod/loader ownership records, caches, configuration backups, profile snapshots, and recovery data. Local runtime data is not intended to be committed to this repository.

## Current baseline

**v0.3.6** is the current verified development baseline. It adds the Profile Restore Assistant on top of portable profiles: unresolved Thunderstore requirements are planned by exact `PackageKey`, the requested version is preferred, fallbacks require explicit acknowledgement, and every install still passes through the existing Package Inspector and transactional install pipeline. See [`PATCH_NOTES_v0.3.6.md`](PATCH_NOTES_v0.3.6.md) and [`docs/V0.3.6_PROFILE_RESTORE_ASSISTANT.md`](docs/V0.3.6_PROFILE_RESTORE_ASSISTANT.md).

The next planned development milestone is **v0.3.7 — Profile Health & Version Reconciliation**. It will preserve expected Mod metadata for resolved profile members, detect version drift that v0.3.6 intentionally treats as a match, and provide a safe path to reconcile supported Thunderstore versions without weakening Package Inspector or ownership rules.

## License

MIT. See [`LICENSE`](LICENSE).
