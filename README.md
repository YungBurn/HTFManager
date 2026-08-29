# HTF Manager v0.3.7

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
- Export/import lightweight `.htfprofile` packages for metadata-only profile sharing.
- Export/import full `.htfbundle` portable profile bundles containing only eligible, verified HTF-managed Mod source artifacts.
- Preserve profile expected Mod identity/version metadata and report `Healthy`, `Missing`, `VersionMismatch`, and `IdentityUncertain` health states.
- Restore missing bundled Mods without reinstalling Mods that already satisfy the profile.
- Guide restoration through the existing Package Inspector and transactional install pipeline; no bundle payload is installed silently.
- Recognize deterministic local BepInEx intrinsic identities (for example a `BepInPlugin` GUID) so eligible local Mods can participate in full sharing without pretending to be Thunderstore packages.

## Safety model

HTF Manager intentionally separates **managed** content from **external/manual** content. Unknown files are not silently taken over or deleted.

Normal Mod installation must not overwrite the game executable, `UnityPlayer.dll`, managed game assemblies, BepInEx core files, or unknown bootstrap DLLs. Loader installation uses a separate validated transaction path. Configuration and profile operations create recovery data before overwriting tracked configuration files.

Lightweight `.htfprofile` files contain references and optional configuration snapshots only. Full `.htfbundle` files may additionally contain verified HTF-managed Mod source artifacts when the profile has an exact expected version and a deterministic identity. External/unmanaged Mods, loader binaries, game files, and ambiguous local packages are never bundled automatically. Users remain responsible for third-party redistribution permissions.

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

tests/                         Deterministic profile/bundle/security tests
docs/                          Architecture and feature design notes
build/                         Local development/release helper scripts
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for current subsystem boundaries and safety invariants.

For continuing development in another ChatGPT session, start with [`SESSION_HANDOFF.md`](SESSION_HANDOFF.md) and [`PROJECT_STATE.json`](PROJECT_STATE.json).

## Local application data

HTF Manager stores its own state outside the game directory:

```text
%LOCALAPPDATA%\HTFManager\
```

This includes settings, profiles, Mod/loader ownership records, caches, configuration backups, profile snapshots, and recovery data. Local runtime data is not intended to be committed to this repository.

## Current baseline

**v0.3.7 — Portable Profile Bundle & Health** is the current release candidate. The final intrinsic-identity overlay must pass the normal Windows/.NET 10 release gate before merge/tag. It builds on the v0.3.6 Restore Assistant with durable expected-state metadata, read-only profile health, lightweight/full sharing, `.htfbundle` schema v1, verified artifact retention, profile-first bundle import, lazy payload extraction, and bundled missing-Mod restoration through the existing Package Inspector/install pipeline.

Full sharing is deliberately conservative. A Mod is automatically bundled only when HTF Manager can prove a managed source artifact belongs to the exact profile expectation. Thunderstore `PackageKey` remains the strongest provider identity; local BepInEx packages can use a deterministic intrinsic plugin identity such as `com.moddle.howtofish.truedot`. Package identity is never fabricated from a filename.

Version mismatch detection is included, but **v0.3.7 does not automatically replace, downgrade, or upgrade a mismatched installed Mod**. That explicit reconciliation workflow is the next planned milestone: **v0.3.8 — Profile Version Reconciliation**.

See [`PATCH_NOTES_v0.3.7.md`](PATCH_NOTES_v0.3.7.md), [`docs/V0.3.7_PORTABLE_PROFILE_BUNDLE_AND_HEALTH.md`](docs/V0.3.7_PORTABLE_PROFILE_BUNDLE_AND_HEALTH.md), and [`docs/HTFBUNDLE_SCHEMA_V1.md`](docs/HTFBUNDLE_SCHEMA_V1.md).

## License

MIT. See [`LICENSE`](LICENSE).
