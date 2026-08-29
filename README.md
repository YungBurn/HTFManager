# HTF Manager v0.3.8

A lightweight, game-specific Mod Manager and launcher for **How to Fish (渔力全开)**.

HTF Manager is built with **.NET 10**, **C#**, and **Avalonia 12**.

The project emphasizes:

- explicit ownership;
- package inspection before installation;
- reversible and transactional writes;
- deterministic Mod/profile identity;
- exact-version reconciliation;
- portable profile sharing;
- conservative handling of unknown files;
- explicit, user-controlled application updates.

> **Current source baseline:** v0.3.8 — Version Reconciliation & Application Delivery  
> **Status:** validated and merged into `main`; formal v0.3.8 release preparation is in progress.

---

## Current capabilities

HTF Manager currently supports:

- Detecting the Steam installation of **How to Fish**.
- Detecting and managing **BepInEx 5** and **MelonLoader** environments.
- Installing local DLL/ZIP Mods through **Package Inspector** and staging.
- Browsing and installing supported **Thunderstore** packages.
- Tracking HTF-managed Mod ownership separately from external/manual Mods.
- Enabling, disabling, updating, and uninstalling managed Mods safely.
- Inspecting dependencies, destination paths, conflicts, identity, and package risk before installation.
- Automatically setting up supported Mod loaders with validation, backup, rollback, and ownership tracking.
- Editing supported BepInEx and MelonLoader configuration through the **Configuration Center**.
- Maintaining profiles containing Mod enabled/disabled state and optional Mod configuration snapshots.
- Exporting/importing lightweight `.htfprofile` metadata-only profile packages.
- Exporting/importing full `.htfbundle` portable profile bundles containing eligible verified HTF-managed source artifacts.
- Preserving expected Mod identity/version metadata inside profiles.
- Reporting profile health as:
  - `Healthy`
  - `Missing`
  - `VersionMismatch`
  - `IdentityUncertain`
- Restoring missing bundled Mods through Package Inspector without reinstalling Mods that already satisfy the profile.
- Recognizing deterministic local BepInEx intrinsic identities such as a `BepInPlugin` GUID without fabricating Thunderstore identity.
- Reconciling `VersionMismatch` explicitly by:
  - restoring the exact profile-expected version; or
  - accepting the deterministic installed version as the new profile baseline.
- Retaining verified package-version artifact history for:
  - exact-version restore;
  - offline reconciliation;
  - historical Full Share bundle creation.
- Publishing a Windows x64 self-contained single-file `HTFManager.exe`.
- Checking stable GitHub Releases for application updates.
- Validating downloaded application-update size and SHA-256.
- Staging application updates.
- Performing an explicit **Restart and Update** replacement flow for supported published builds.

---

## Profile version reconciliation

v0.3.8 introduces explicit handling of profile version drift.

For example:

```text
Profile Expected: 1.0.2
Installed:        1.0.3
Status:           VersionMismatch
```

HTF Manager does not silently change either side.

The user can explicitly choose to:

```text
Restore Expected Version
```

or:

```text
Accept Installed Version
```

### Restore Expected Version

The resolver searches for the exact requested version using the following source priority:

```text
Active .htfbundle exact payload
        ↓
Retained exact local artifact
        ↓
Exact Thunderstore version
        ↓
Manual resolution
```

Version fallback is intentionally disabled.

If a profile expects:

```text
1.0.2
```

HTF Manager will not silently install:

```text
1.0.3
```

simply because the expected version cannot be found.

### Accept Installed Version

When the currently installed Mod has deterministic identity and source association, the user can explicitly promote that installed version to the new profile baseline.

For example:

```text
Before:

Expected:  1.0.2
Installed: 1.0.3
Status:    VersionMismatch

After "Accept Installed Version":

Expected:  1.0.3
Installed: 1.0.3
Status:    Healthy
```

This mutation is explicit and persisted to the profile.

---

## Versioned package artifact history

HTF Manager retains verified source artifacts for managed package versions when possible.

Artifact metadata can include:

- `PackageKey`
- `IntrinsicId`
- `Version`
- `Source`
- SHA-256
- File length
- Stored path
- Capture timestamp

This allows a previously installed exact version to remain usable even after a newer version is installed.

Example:

```text
Previously installed:
More_Colours 1.0.2

Currently installed:
More_Colours 1.0.3

Profile expected:
More_Colours 1.0.2
```

If the verified `1.0.2` artifact is still retained, HTF Manager can restore it without requiring version fallback.

Retained historical artifacts can also support offline reconciliation and portable bundle creation.

---

## Profile formats

HTF Manager uses two separate portable-profile formats.

### `.htfprofile`

A lightweight profile package.

It contains profile metadata such as:

- expected Mod identities;
- expected versions;
- enabled/disabled state;
- optional supported configuration snapshots.

It does **not** embed Mod binaries.

This makes `.htfprofile` suitable when package sources can be resolved independently on the destination machine.

### `.htfbundle`

A Full Share portable bundle.

It contains:

```text
bundle.json
profile.htfprofile
payload/
```

Eligible payloads can contain verified HTF-managed source artifacts.

A Full Share bundle is designed to prefer the artifact matching the **profile-expected version**, rather than blindly exporting the currently installed version.

For example:

```text
Profile Expected:
More_Colours 1.0.2

Currently Installed:
More_Colours 1.0.3

Retained Artifact:
More_Colours 1.0.2
```

The Full Share bundle can carry:

```text
More_Colours 1.0.2
```

rather than `1.0.3`.

Bundle import remains profile-first.

Importing a `.htfbundle` does not automatically:

- install embedded Mods;
- downgrade installed Mods;
- apply a profile;
- take ownership of external files.

Each bundled package that requires installation still enters **Package Inspector** and requires explicit confirmation.

---

## Identity model

HTF Manager distinguishes provider-backed identity from local intrinsic identity.

The effective identity priority is:

```text
PackageKey > IntrinsicId
```

### Provider identity

A provider-backed package can use a deterministic key such as:

```text
Sopika-More_Colours
```

This remains the strongest form of managed identity.

### Local intrinsic identity

HTF-managed local Mods may use deterministic intrinsic identity when no provider identity exists.

For example, a BepInEx plugin may expose:

```text
com.moddle.howtofish.truedot
```

through its `BepInPlugin` GUID.

This allows supported local Mods to participate in:

- profile health;
- exact-version reconciliation;
- artifact history;
- portable bundles.

A local intrinsic identity is never allowed to silently replace or take ownership of an existing provider-backed identity.

---

## Safety model

HTF Manager intentionally separates **managed** content from **external/manual** content.

Unknown files are not silently taken over or deleted.

### Mod installation safety

Normal Mod installation must not overwrite protected game/runtime components such as:

- the game executable;
- `UnityPlayer.dll`;
- managed game assemblies;
- BepInEx core files;
- unknown bootstrap DLLs.

Loader installation uses a separate validated transaction path.

Package Inspector acts as the confirmation boundary before Mod installation or exact-version replacement.

### Transactional replacement

Version replacement uses the existing managed installation pipeline.

Conceptually:

```text
Resolve exact artifact
        ↓
Package Inspector
        ↓
Explicit user confirmation
        ↓
Preserve current managed artifact
        ↓
Transactional replacement
        ↓
Update ownership/install state
        ↓
Rescan
        ↓
Refresh Profile Health
```

Replacement failures follow the existing rollback model.

Normal reconciliation does not intentionally remove user configuration.

### Configuration safety

Supported configuration/profile operations create recovery data before overwriting tracked configuration files.

HTF Manager data and recovery material are stored outside the game directory where appropriate.

---

## Portable bundle safety

Lightweight `.htfprofile` files contain references and optional configuration snapshots only.

Full `.htfbundle` files may additionally contain verified HTF-managed source artifacts.

HTF Manager does not automatically bundle:

- external/manual Mods;
- ambiguous unmanaged packages;
- loader binaries;
- game files;
- unknown bootstrap components.

Users remain responsible for complying with third-party Mod redistribution licenses and permissions when sharing Full Share bundles.

---

## Application delivery

v0.3.8 introduces the Windows application-delivery baseline.

The release target is:

```text
Windows x64
.NET 10
Self-contained
Single-file
```

The published executable is designed so end users do not need to separately install the .NET runtime.

The publish configuration intentionally keeps trimming disabled for conservative Avalonia compatibility.

Expected release assets are:

```text
HTFManager.exe
update-manifest.json
SHA256SUMS.txt
```

Generated release artifacts are not committed to normal Git source control.

---

## Application updates

HTF Manager includes a Stable Channel application-update mechanism based on GitHub Releases.

The update flow is:

```text
Check latest stable GitHub Release
        ↓
Read update-manifest.json
        ↓
Compare versions
        ↓
Download HTFManager.exe
        ↓
Validate expected file length
        ↓
Validate SHA-256
        ↓
Stage update
        ↓
Explicit "Restart and Update"
```

The running executable is not overwritten directly.

A temporary update-host process is used to:

```text
Wait for the current HTFManager process
        ↓
Preserve the previous executable
        ↓
Replace the target executable
        ↓
Launch the new version
        ↓
Attempt rollback if replacement fails
```

v0.3.8 does **not** implement:

- forced application updates;
- silent background replacement;
- automatic UAC elevation;
- delta updates;
- Beta/pre-release channel switching.

Application-update checks can be disabled in Settings.

### Current updater validation status

The following have been validated:

- stable GitHub Release checking;
- older releases are not interpreted as updates;
- update-check network failures fail safely;
- automatic update-check preference persists across restart.

A real cross-version executable replacement requires a published version newer than v0.3.8.

Therefore:

```text
v0.3.8 → newer stable release
```

self-update validation remains intentionally deferred until such a release exists.

---

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

HTF Manager release binaries are intended to be built from the public source repository through the project's GitHub Actions release pipeline.

### Team roles

- **Authors / Committers:** YungBurn
- **Reviewers:** YungBurn
- **Approvers:** YungBurn

Changes intended for a release are reviewed through the public Git repository
and pull-request history.

Every release signing request using the SignPath Foundation certificate
requires explicit manual approval before signing.

### Build integrity

Official release binaries are intended to originate from the public GitHub repository and its release workflow.

The release pipeline is designed around:

```text
Source / tag
        ↓
Restore
        ↓
Build
        ↓
Automated tests
        ↓
Windows publish
        ↓
Code signing
        ↓
Signature verification
        ↓
SHA-256 generation
        ↓
update-manifest.json
        ↓
SHA256SUMS.txt
        ↓
GitHub Release
```

SHA-256 values and update manifests must be generated **after** the final executable has been signed because Authenticode signing changes the executable bytes.

### Privacy

HTF Manager does not intentionally collect telemetry or analytics.

Network access is used only for application functionality that requires an online service, including:

- accessing supported Mod package providers such as Thunderstore when package information or downloads are requested;
- checking GitHub Releases for HTF Manager application updates;
- downloading an HTF Manager application update after the relevant user action.

Automatic update checks can be disabled in HTF Manager settings.

Profile data, configuration snapshots, managed-package history, ownership records, recovery data, and game files are not intended to be uploaded as part of ordinary package browsing or application-update operations.

Local application state remains stored on the user's machine unless the user explicitly performs an export/share operation.

HTF Manager will not transfer information to other networked systems unless
specifically requested by the user or required by a user-enabled feature.

---

## Requirements

### Running the published application

- Windows 10/11 x64
- How to Fish through Steam
- BepInEx 5 and/or MelonLoader depending on the Mods being used

The `win-x64` release is self-contained.

End users do **not** need to install a separate .NET runtime for the official self-contained executable.

### Development

- Windows 10/11
- .NET 10 SDK
- Git

Avalonia is currently pinned to:

```text
12.1.1
```

---

## Build and test

Restore dependencies:

```powershell
dotnet restore HTFManager.slnx
```

Build Release:

```powershell
dotnet build HTFManager.slnx `
  --configuration Release `
  --no-restore
```

Run automated tests:

```powershell
dotnet test `
  --project .\tests\HTFManager.Tests\HTFManager.Tests.csproj `
  --configuration Release `
  --no-build `
  --no-restore
```

Run from source:

```powershell
dotnet run `
  --project .\src\HTFManager.App\HTFManager.App.csproj
```

---

## Automated validation

The current v0.3.8 baseline contains:

```text
71 automated tests
71 passed
0 failed
0 skipped
```

The validated test suite covers areas including:

- profile identity;
- profile health;
- bundle serialization/import;
- exact-version reconciliation;
- package artifact history;
- source resolution;
- application update models/services;
- security and safety invariants.

---

## Manual validation

v0.3.8 has also undergone real application-level validation.

Completed checks include:

- [x] Published `HTFManager.exe` starts correctly
- [x] Self-contained execution
- [x] Single-file execution
- [x] `VersionMismatch` detection using a real managed Mod
- [x] Exact-version restore
- [x] Historical exact-artifact discovery
- [x] `Accept Installed Version`
- [x] Accepted baseline persistence after application restart
- [x] Offline historical-artifact restore
- [x] Full `.htfbundle` uses the profile-expected historical artifact
- [x] `.htfbundle` carries the exact expected package version
- [x] `.htfbundle` Version Reconciliation round trip
- [x] Restore Expected returns Profile Health to `Healthy`
- [x] GitHub Releases update UI
- [x] Older Stable Releases are not incorrectly treated as updates
- [x] Offline update-check failure handling
- [x] Automatic update preference persistence

Deferred:

- [ ] Real cross-version Self Update

The deferred item requires:

```text
v0.3.8
→ newer stable release
→ download
→ SHA-256 verification
→ staging
→ Update Host replacement
→ relaunch
→ user-data preservation
→ rollback validation
```

This item is deferred because a newer stable release does not yet exist; it is not considered a failed validation.

---

## Build the Windows release executable

After restore/build/test succeeds:

```powershell
.\build\publish-win-x64.ps1
```

Release assets are written under:

```text
artifacts/
└─ release/
   └─ v0.3.8/
      ├─ HTFManager.exe
      ├─ update-manifest.json
      └─ SHA256SUMS.txt
```

The executable is published as:

```text
Target:                  win-x64
Framework:               .NET 10
Self-contained:          true
Single-file:             true
Native self-extraction:  enabled
Trimming:                disabled
ReadyToRun:              disabled
```

Release-generated files under `artifacts/` are excluded from normal Git source control.

---

## Release integrity

Official release generation is expected to maintain the following ordering:

```text
Build
↓
Test
↓
Publish
↓
Authenticode signing
↓
Verify signature
↓
Calculate final SHA-256
↓
Generate update-manifest.json
↓
Generate SHA256SUMS.txt
↓
Publish GitHub Release
```

The executable must be signed before its release hash is generated.

This is required because adding an Authenticode signature changes the executable and therefore changes its SHA-256.

---

## Project structure

```text
src/
├─ HTFManager.App/
│  └─ Avalonia UI, localization, composition and update-host mode
│
├─ HTFManager.Core/
│  └─ Core models, contracts and interfaces
│
└─ HTFManager.Infrastructure/
   └─ Game, loader, Mod, package, profile, artifact,
      application-update and persistence services

tests/
└─ Deterministic profile, bundle, reconciliation,
   application-update and security tests

docs/
└─ Architecture, feature-scope and schema documentation

build/
└─ Local development and release helper scripts

.github/
└─ workflows/
   └─ Build/test and tag-driven release automation
```

See:

[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)

for subsystem boundaries and safety invariants.

---

## Local application data

HTF Manager stores its own state outside the game directory:

```text
%LOCALAPPDATA%\HTFManager\
```

Depending on enabled features and application history, this can include:

- application settings;
- profiles;
- ownership records;
- package caches;
- versioned package artifact history;
- configuration backups;
- profile snapshots;
- recovery data;
- application-update staging.

This runtime data is not intended to be committed to the repository.

---

## Current development baseline

The current source baseline is:

**v0.3.8 — Version Reconciliation & Application Delivery**

The version-reconciliation path is:

```text
VersionMismatch
        ↓
Exact source planning
        ↓
Bundle / retained artifact / exact Thunderstore
        ↓
Package Inspector
        ↓
Existing transactional replacement
        ↓
Health refresh
```

The application-update path is:

```text
GitHub Stable Release
        ↓
update-manifest.json
        ↓
Version comparison
        ↓
Size + SHA-256 verification
        ↓
Staged HTFManager.exe
        ↓
Explicit Restart and Update
        ↓
Temporary same-EXE Update Host
        ↓
Backup / replace / relaunch
        ↓
Rollback on apply failure
```

v0.3.8 has completed its automated and primary manual validation and has been merged into `main`.

Formal release preparation is currently focused on Windows release trust and Authenticode signing.

No `v0.3.8` release should be considered final until the release signing and release-asset pipeline is complete.

---

## Documentation

Additional design and release documentation includes:

- [`PATCH_NOTES_v0.3.8.md`](PATCH_NOTES_v0.3.8.md)
- [`docs/V0.3.8_SCOPE_LOCK.md`](docs/V0.3.8_SCOPE_LOCK.md)
- [`docs/UPDATE_MANIFEST_V1.md`](docs/UPDATE_MANIFEST_V1.md)
- [`docs/HTFBUNDLE_SCHEMA_V1.md`](docs/HTFBUNDLE_SCHEMA_V1.md)
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)

---

## Contributing

HTF Manager is currently a focused, game-specific project.

When contributing:

- preserve explicit ownership boundaries;
- do not silently take ownership of external files;
- keep package inspection as the confirmation boundary;
- preserve rollback/recovery behavior;
- use deterministic identity wherever version reconciliation depends on identity;
- do not introduce version fallback into exact reconciliation;
- do not commit generated release artifacts;
- add or update automated tests when modifying safety-sensitive behavior.

Before submitting changes:

```powershell
dotnet restore HTFManager.slnx

dotnet build HTFManager.slnx `
  --configuration Release `
  --no-restore

dotnet test `
  --project .\tests\HTFManager.Tests\HTFManager.Tests.csproj `
  --configuration Release `
  --no-build `
  --no-restore
```

---

## License

HTF Manager is licensed under the **MIT License**.

See [`LICENSE`](LICENSE).

```text
Copyright (c) 2026 YungBurn
```