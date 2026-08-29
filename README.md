# HTF Manager v0.3.8

A lightweight, game-specific Mod Manager and launcher for **How to Fish (渔力全开)**.

HTF Manager is built with **.NET 10**, **C#**, and **Avalonia 12**. Its safety model emphasizes explicit ownership, package inspection, reversible writes, deterministic profile identity, and user-controlled reconciliation/update actions.

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
- Maintain profiles containing Mod enabled/disabled state and optional Mod configuration snapshots.
- Export/import lightweight `.htfprofile` metadata-only packages.
- Export/import full `.htfbundle` profile bundles containing only eligible verified HTF-managed source artifacts.
- Preserve profile expected Mod identity/version metadata and report `Healthy`, `Missing`, `VersionMismatch`, and `IdentityUncertain` states.
- Restore missing bundled Mods through Package Inspector without reinstalling Mods that already satisfy the profile.
- Recognize deterministic local BepInEx intrinsic identities such as a `BepInPlugin` GUID without fabricating Thunderstore identity.
- Reconcile `VersionMismatch` explicitly by restoring the exact profile-expected version or accepting the deterministic installed version as the new profile baseline.
- Retain verified package-version history for exact offline restore/bundle sharing when source artifacts remain available.
- Publish a Windows x64 self-contained single-file `HTFManager.exe`.
- Check stable GitHub Releases, verify update size/SHA-256, stage the executable, and perform an explicit restart-and-update flow for supported published builds.

## Safety model

HTF Manager intentionally separates **managed** content from **external/manual** content. Unknown files are not silently taken over or deleted.

Normal Mod installation must not overwrite the game executable, `UnityPlayer.dll`, managed game assemblies, BepInEx core files, or unknown bootstrap DLLs. Loader installation uses a separate validated transaction path. Configuration/profile operations create recovery data before overwriting tracked configuration files.

Profile reconciliation is exact and explicit. A profile expecting `1.2.0` does not silently accept or install `1.3.0`. Provider `PackageKey` remains the strongest identity; local `IntrinsicId` is used only when no provider identity exists. Package Inspector remains the confirmation boundary before exact-version replacement.

Lightweight `.htfprofile` files contain references and optional configuration snapshots only. Full `.htfbundle` files may additionally contain verified HTF-managed source artifacts. External/unmanaged Mods, loader binaries, game files, and ambiguous local packages are never bundled automatically. Users remain responsible for third-party redistribution permissions.

Application updates are also explicit. HTF Manager validates a stable release manifest and downloaded executable SHA-256 before staging. v0.3.8 does not provide forced updates, UAC elevation, code-signing verification, or silent replacement.

## Requirements

### Running the published application

- Windows 10/11 x64
- How to Fish through Steam
- BepInEx 5 and/or MelonLoader depending on the Mods being used

The `win-x64` release is self-contained, so end users do **not** need to install a separate .NET runtime.

### Development

- Windows 10/11
- .NET 10 SDK
- Git

Avalonia is currently pinned to **12.1.1**.

## Build and test

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet test --project .\tests\HTFManager.Tests\HTFManager.Tests.csproj --configuration Release --no-build --no-restore
```

Run from source:

```powershell
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

## Build the Windows release executable

After restore/build/test succeeds:

```powershell
.\build\publish-win-x64.ps1
```

Release assets are written under:

```text
artifacts/release/v0.3.8/
├─ HTFManager.exe
├─ update-manifest.json
└─ SHA256SUMS.txt
```

The executable is a .NET 10 `win-x64` self-contained single-file publish with trimming disabled for conservative Avalonia compatibility.

## Project structure

```text
src/
├─ HTFManager.App/             Avalonia UI, localization, composition, update-host mode
├─ HTFManager.Core/            Models and interfaces
└─ HTFManager.Infrastructure/  Game, loader, Mod, profile, artifact, update and storage services

tests/                         Deterministic profile/bundle/reconciliation/update/security tests
docs/                          Architecture and feature/schema notes
build/                         Local development/release helper scripts
.github/workflows/             Build/test and tag-driven release automation
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for subsystem boundaries and safety invariants.

## Local application data

HTF Manager stores its own state outside the game directory:

```text
%LOCALAPPDATA%\HTFManager\
```

This includes settings, profiles, ownership records, package caches/history, configuration backups, profile snapshots, recovery data, and application-update staging. Local runtime data is not intended to be committed to this repository.

## Current development baseline

**v0.3.8 — Version Reconciliation & Application Delivery** is the current implementation candidate.

The core v0.3.8 changes are:

```text
VersionMismatch
→ exact source planning
→ Bundle / retained exact artifact / exact Thunderstore
→ Package Inspector
→ existing transactional replacement
→ health refresh
```

and:

```text
GitHub stable Release
→ update-manifest.json
→ size + SHA-256 verification
→ staged HTFManager.exe
→ explicit Restart and update
→ temporary same-EXE update host
→ backup / replace / relaunch / rollback-on-apply-failure
```

The implementation candidate contains **71 automated tests** and must still pass the normal Windows/.NET 10 release gate plus real EXE/reconciliation smoke testing before `buildVerified` is promoted to true and the version is tagged/released.

See [`PATCH_NOTES_v0.3.8.md`](PATCH_NOTES_v0.3.8.md), [`docs/V0.3.8_SCOPE_LOCK.md`](docs/V0.3.8_SCOPE_LOCK.md), [`docs/UPDATE_MANIFEST_V1.md`](docs/UPDATE_MANIFEST_V1.md), and [`docs/HTFBUNDLE_SCHEMA_V1.md`](docs/HTFBUNDLE_SCHEMA_V1.md).

## License

MIT. See [`LICENSE`](LICENSE).
