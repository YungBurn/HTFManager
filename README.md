# HTF Manager v0.3.9

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
- hardened application-update validation;
- explicit, user-controlled reconciliation and update actions.

> **Current source baseline:** v0.3.9 — Update Hardening & Reconciliation UX  
> **Status:** validated implementation on the v0.3.9 feature branch; PR/merge and formal release are pending.

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
  - accepting a deterministic, source-compatible installed version as the new profile baseline.
- Retaining verified package-version artifact history for:
  - exact-version restore;
  - offline reconciliation;
  - historical Full Share bundle creation.
- Showing the profile-expected and currently installed versions separately when version drift exists.
- Showing the provenance of exact-version restore packages, including:
  - active portable bundle;
  - retained package history;
  - Thunderstore.
- Distinguishing missing bundled packages from VersionMismatch entries that already have an exact payload available inside a bundle.
- Publishing a Windows x64 self-contained single-file `HTFManager.exe`.
- Checking stable GitHub Releases for application updates.
- Rejecting same-version and downgrade application-update candidates.
- Strictly validating application-update manifest metadata.
- Validating update asset size and SHA-256.
- Cleaning failed or incomplete temporary update downloads.
- Revalidating staged executables before they are reused.
- Staging application updates.
- Performing an explicit **Restart and Update** replacement flow for supported published builds.
- Using an update-host acknowledgement mechanism for safer post-replacement startup handling in v0.3.9 and later update flows.

---

## Profile version reconciliation

HTF Manager explicitly handles profile version drift.

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

or, when identity and source constraints are satisfied:

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

### Restore source provenance

v0.3.9 exposes the selected exact-version source to the user.

Package Inspector can distinguish sources such as:

```text
Portable bundle
Retained package history
Thunderstore
```

This makes reconciliation source selection visible instead of requiring the user to infer where the artifact originated.

### Accept Installed Version

When the currently installed Mod has deterministic identity and a compatible source association, the user can explicitly promote that installed version to the new profile baseline.

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

The mutation is explicit and persisted to the profile.

HTF Manager does not allow this action merely because the Mod identity matches.

The installed source must also satisfy the profile's source expectations.

For example:

```text
Expected identity: Sopika-More_Colours
Installed identity: Sopika-More_Colours
Identity match: yes

Expected source: Thunderstore
Installed source: Local archive
Source match: no
```

In this case, **Accept Installed Version** is not offered.

This preserves the existing source-ownership safety boundary and prevents an otherwise matching local package from silently replacing a provider-backed profile expectation.

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

Retained historical artifacts can also support:

- offline reconciliation;
- exact-version restore;
- portable Full Share bundle creation.

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

A Full Share bundle prefers the artifact matching the **profile-expected version**, rather than blindly exporting the currently installed version.

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

### Bundle reconciliation UX

v0.3.9 makes bundle recoverability terminology more explicit.

The import UI distinguishes:

```text
Missing + exact payload available in bundle
```

from:

```text
VersionMismatch + exact expected version available in bundle
```

This avoids treating the generic phrase “recoverable from bundle” as if it represented every payload contained in the archive.

A VersionMismatch can therefore remain a VersionMismatch while also indicating that an exact expected version is available from the active bundle.

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

HTF Manager publishes Windows builds targeting:

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

The general update flow is:

```text
Check latest stable GitHub Release
        ↓
Read update-manifest.json
        ↓
Validate manifest metadata
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

A temporary update-host process is used to perform replacement after the main process exits.

---

## v0.3.9 updater hardening

v0.3.9 strengthens validation around GitHub Release updates.

### Version validation

The update service rejects:

```text
Latest == Current
Latest < Current
```

Only a newer compatible stable version can become an update candidate.

HTF Manager therefore does not treat an older GitHub Release as a downgrade opportunity.

### Manifest validation

Application update metadata is treated as untrusted remote input.

v0.3.9 validates relevant fields before accepting an update candidate, including:

- release version;
- release channel;
- target runtime identifier;
- expected executable asset;
- download URL;
- expected file size;
- SHA-256.

The updater does not silently substitute another asset when the expected `HTFManager.exe` entry is invalid.

### Transport restrictions

Expected application-update downloads use HTTPS.

Unexpected or invalid asset URLs are rejected instead of being staged.

### Download-size validation

When available, HTTP response length is checked against the expected manifest size.

The download path also tracks streamed bytes so unexpectedly oversized content cannot simply be accepted because the server omitted or misreported a header.

After the transfer completes, the final file length is validated again.

### SHA-256 validation

The final staged executable must match the SHA-256 specified by the update manifest.

A mismatched executable is rejected and is not offered for application replacement.

### Failed-download cleanup

Temporary downloads use isolated `.download-*` files.

If a download:

- fails;
- is cancelled;
- violates size constraints;
- fails SHA-256 validation;

the invalid temporary artifact is removed rather than being left behind as a reusable staged executable.

### Staged update revalidation

An existing staged executable is not trusted solely because it already exists on disk.

Before reuse, HTF Manager revalidates the staged artifact against the expected update metadata.

A stale, altered, truncated, or otherwise invalid staged file must be discarded instead of being reused for Restart and Update.

---

## Update Host

The running `HTFManager.exe` cannot replace itself directly.

HTF Manager therefore copies the update-host executable to a temporary location and uses that process to perform the replacement.

Conceptually:

```text
HTFManager.exe
        ↓
Stage validated new executable
        ↓
Copy temporary Update Host
        ↓
Exit current application
        ↓
Update Host waits for parent exit
        ↓
Preserve old executable as .old
        ↓
Move staged executable into place
        ↓
Launch new executable
```

### v0.3.9 startup acknowledgement

v0.3.9 adds a startup acknowledgement mechanism to improve post-replacement safety.

For update flows initiated by v0.3.9 or later, the new application can acknowledge successful initialization after the application startup path has progressed far enough to initialize Avalonia.

The intended flow is:

```text
Replace executable
        ↓
Launch new version
        ↓
Wait for startup acknowledgement
        ├─ acknowledged
        │      ↓
        │   replacement accepted
        │
        └─ failed / exited / timed out
               ↓
            attempt rollback
```

The previous executable is preserved while the acknowledgement decision is pending.

### Important validation boundary

A real:

```text
v0.3.8 → v0.3.9
```

self-update is still useful for validating the existing production update path.

However, the Update Host performing that replacement originates from the currently running `v0.3.8` executable.

Therefore, `v0.3.8 → v0.3.9` does **not** fully validate the new v0.3.9 startup-acknowledgement logic.

The new acknowledgement flow requires:

```text
v0.3.9 → a newer published version
```

for complete end-to-end validation.

---

## Update settings UI

The application update section exposes relevant update state to the user.

Depending on the current state, the UI can display information such as:

- current application version;
- update channel;
- latest release information;
- release publication time;
- expected download size;
- download/verification status;
- whether an update is currently available.

Automatic update checking remains optional and can be disabled in Settings.

Network failures are treated as update-check failures rather than application-fatal errors.

---

## Application update boundaries

HTF Manager does **not** currently implement:

- forced application updates;
- silent background executable replacement;
- automatic downgrade;
- automatic UAC elevation;
- delta/binary-diff updates;
- Beta/pre-release channel switching;
- automatic acceptance of malformed release metadata.

Application updates remain explicit and user-controlled.

---

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

HTF Manager is preparing SignPath Foundation integration for official Windows release signing.

The code-signing integration is not considered complete until the external SignPath project configuration and release workflow have been approved and validated.

Older releases and local development/RC builds may therefore be unsigned.

### Team roles

Project roles are currently:

- **Authors / Committers:** YungBurn
- **Reviewers:** YungBurn
- **Approvers:** YungBurn

Changes intended for a release are reviewed through the public Git repository and pull-request history.

Every release signing request using the SignPath Foundation certificate requires explicit manual approval before signing.

### Build integrity

Official release binaries are intended to originate from the public GitHub repository and its release workflow.

The target release pipeline is:

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

HTF Manager will not transfer information to other networked systems unless specifically requested by the user or required by a user-enabled application feature.

Network access is used for application functionality that requires an online service, including:

- accessing supported Mod package providers such as Thunderstore when package information or downloads are requested;
- checking GitHub Releases for HTF Manager application updates;
- downloading an HTF Manager application update after the relevant user action.

Automatic update checks can be disabled in HTF Manager settings.

Profile data, configuration snapshots, managed-package history, ownership records, recovery data, and game files are not intended to be uploaded as part of ordinary package browsing or application-update operations.

Local application state remains stored on the user's machine unless the user explicitly performs an export/share operation.

---

## Requirements

### Running the published application

- Windows 10/11 x64
- How to Fish through Steam
- BepInEx 5 and/or MelonLoader depending on the Mods being used

The `win-x64` release is published as self-contained.

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

The current v0.3.9 implementation contains:

```text
81 automated tests
81 passed
0 failed
0 skipped
```

The validated test suite covers areas including:

- profile identity;
- profile health;
- bundle serialization/import;
- exact-version reconciliation;
- package artifact history;
- reconciliation source constraints;
- application update manifest validation;
- version comparison;
- update download size validation;
- SHA-256 validation;
- failed-download cleanup;
- staged artifact validation;
- application update models/services;
- security and safety invariants.

---

## Manual validation

v0.3.9 has undergone application-level validation using the published Windows executable.

Completed checks include:

- [x] Release build succeeds
- [x] Automated tests: **81 / 81 PASS**
- [x] `win-x64` publish succeeds
- [x] Published `HTFManager.exe` starts correctly
- [x] Single-file relocation smoke test
- [x] `VersionMismatch` detection using a real managed Mod
- [x] Profile UI separately shows Expected / Installed versions during drift
- [x] Exact-version restore
- [x] Historical exact-artifact discovery
- [x] Restore-source provenance
- [x] `Accept Installed Version` with matching source
- [x] Source-mismatched installed version is not offered as an acceptable baseline
- [x] Accepted baseline persists after application restart
- [x] Offline historical-artifact restore regression
- [x] Full `.htfbundle` uses the profile-expected historical artifact
- [x] `.htfbundle` carries the exact expected package version
- [x] `.htfbundle` Version Reconciliation round trip
- [x] Bundle recoverability wording distinguishes Missing from VersionMismatch
- [x] Bundle exact-version provenance
- [x] Restore Expected returns Profile Health to `Healthy`
- [x] Application update settings UI
- [x] Same-version release suppression
- [x] Older Stable Releases are not treated as updates
- [x] Offline update-check failure handling
- [x] Failed-download temporary-file cleanup
- [x] Automatic update preference persistence regression

Deferred:

- [ ] Real `v0.3.8 → v0.3.9` production self-update
- [ ] v0.3.9 startup-ack Update Host end-to-end validation
- [ ] SignPath Authenticode release signing

These deferred items require either:

- a real published cross-version environment;
- a version newer than v0.3.9;
- or completion of the external SignPath signing integration.

They are not considered failed validation.

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
   └─ v0.3.9/
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

The intended official release ordering is:

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

The executable must be signed before its final release hash is generated.

This is required because adding an Authenticode signature changes the executable and therefore changes its SHA-256.

Until SignPath integration is complete, locally generated and validation RC executables should be treated as unsigned development/release-candidate artifacts rather than final signed release binaries.

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
└─ Architecture, feature-scope, validation and schema documentation

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

**v0.3.9 — Update Hardening & Reconciliation UX**

v0.3.9 builds on the v0.3.8 Version Reconciliation & Application Delivery foundation.

The reconciliation path is:

```text
VersionMismatch
        ↓
Show Expected / Installed clearly
        ↓
Exact source planning
        ↓
Bundle / retained artifact / exact Thunderstore
        ↓
Show restore provenance
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
Strict manifest validation
        ↓
Reject same/older versions
        ↓
Validate HTTPS / RID / channel / asset
        ↓
Download with size limits
        ↓
Final size + SHA-256 validation
        ↓
Clean failed temporary downloads
        ↓
Revalidate staged executable
        ↓
Explicit Restart and Update
        ↓
Temporary Update Host
        ↓
Backup / replace / relaunch
        ↓
Startup acknowledgement
        ↓
Accept replacement or attempt rollback
```

The v0.3.9 implementation has completed:

```text
81 / 81 automated tests
Release build validation
win-x64 publish validation
Published EXE startup validation
Single-file relocation smoke testing
Version Reconciliation UX validation
Restore provenance validation
Accept Installed source-guard validation
Portable Bundle reconciliation validation
Updater UI/hardening validation
Network-failure validation
Failed-download cleanup validation
```

The implementation is currently being prepared for commit, pull-request review, merge, and later formal release.

---

## Version history

### v0.3.9 — Update Hardening & Reconciliation UX

Primary focus:

- hardened application-update validation;
- safer staged update handling;
- failed-download cleanup;
- Update Host startup acknowledgement;
- clearer Expected / Installed Profile UX;
- restore-source provenance;
- source-aware Accept Installed gating;
- clearer `.htfbundle` reconciliation terminology.

See:

- [`PATCH_NOTES_v0.3.9.md`](PATCH_NOTES_v0.3.9.md)
- [`docs/V0.3.9_SCOPE_LOCK.md`](docs/V0.3.9_SCOPE_LOCK.md)
- [`docs/V0.3.9_VALIDATION.md`](docs/V0.3.9_VALIDATION.md)

### v0.3.8 — Version Reconciliation & Application Delivery

Introduced:

- explicit Profile Version Reconciliation;
- versioned package artifact history;
- exact historical restore;
- expected-version Full Share bundles;
- Windows single-file/self-contained publishing;
- GitHub Releases application-update infrastructure.

See:

- [`PATCH_NOTES_v0.3.8.md`](PATCH_NOTES_v0.3.8.md)
- [`docs/V0.3.8_SCOPE_LOCK.md`](docs/V0.3.8_SCOPE_LOCK.md)
- [`docs/UPDATE_MANIFEST_V1.md`](docs/UPDATE_MANIFEST_V1.md)
- [`docs/HTFBUNDLE_SCHEMA_V1.md`](docs/HTFBUNDLE_SCHEMA_V1.md)

---

## Documentation

Additional architecture, schema, validation, and release documentation includes:

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/HTFBUNDLE_SCHEMA_V1.md`](docs/HTFBUNDLE_SCHEMA_V1.md)
- [`docs/UPDATE_MANIFEST_V1.md`](docs/UPDATE_MANIFEST_V1.md)
- [`docs/DOWNLOADS.md`](docs/DOWNLOADS.md)
- [`docs/V0.3.9_SCOPE_LOCK.md`](docs/V0.3.9_SCOPE_LOCK.md)
- [`docs/V0.3.9_VALIDATION.md`](docs/V0.3.9_VALIDATION.md)
- [`PATCH_NOTES_v0.3.9.md`](PATCH_NOTES_v0.3.9.md)

---

## Contributing

HTF Manager is currently a focused, game-specific project.

When contributing:

- preserve explicit ownership boundaries;
- do not silently take ownership of external files;
- keep Package Inspector as the confirmation boundary;
- preserve rollback/recovery behavior;
- use deterministic identity wherever version reconciliation depends on identity;
- preserve source identity constraints;
- do not introduce version fallback into exact reconciliation;
- treat remote update metadata as untrusted input;
- do not bypass application-update size/hash validation;
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