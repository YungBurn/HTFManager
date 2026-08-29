# HTF Manager Architecture — v0.3.8

## 1. Design goals

HTF Manager is a game-specific desktop Mod manager/launcher for How to Fish. The architecture prioritizes:

- reversible operations;
- explicit ownership;
- package inspection before writes;
- separation between Mod-loader management and normal Mod management;
- preservation of user configuration;
- support for BepInEx and MelonLoader without assuming mixed-loader compatibility;
- a small native desktop UI rather than an embedded web application.

## 2. Project boundaries

### `HTFManager.App`

Avalonia UI and application composition:

- MainWindow and three-column shell;
- Home / Mods / Discover / Profiles / Configuration / Tools / Settings pages;
- localization;
- configuration localization schemas;
- dialogs/overlays and page-level workflow presentation.

App code may depend on Core and Infrastructure. Core must not depend on Avalonia.

### `HTFManager.Core`

Pure models and interfaces for:

- game environment;
- loader state;
- installed/remote Mod metadata;
- package inspection;
- configuration documents;
- profiles and profile snapshots;
- portable-profile inspection;
- operation results.

Core contains no UI and should avoid direct OS/file-system implementation details.

### `HTFManager.Infrastructure`

Concrete services for:

- Steam game discovery and launch;
- BepInEx/MelonLoader environment inspection;
- loader setup/maintenance;
- local Mod scanning and package installation;
- Thunderstore catalog access;
- configuration parsing/writing/backups;
- profiles, snapshots, portable-profile import/export;
- local JSON storage;
- Windows shell/process integration.

## 3. Persistent UI shell

The application keeps a stable three-column layout:

```text
Navigation | Active workspace | Game controls/status
```

The right panel remains visible across pages so launching the game and checking environment health never requires leaving the current workflow.

Dynamic views that construct controls from runtime resources must wait until they are attached to Avalonia's visual tree before their first resource-dependent render.

## 4. Game and loader environment

Game discovery resolves the actual directory containing `How to Fish.exe`.

The environment service separately reports BepInEx and MelonLoader state. Loader detection must not imply that two installed loaders are safe to run together.

Loader setup is a separate subsystem from normal Mod installation because it may write bootstrap DLLs and loader runtime files near the game executable.

The loader setup transaction is conceptually:

```text
Trusted source metadata
→ download/cache
→ staging extraction
→ archive/path validation
→ expected-layout validation
→ target conflict inspection
→ backup
→ commit
→ loader ownership record
→ environment re-scan
→ rollback on failure
```

Unknown existing bootstrap files are a conflict, not an invitation to overwrite.

## 5. Normal Mod install pipeline

All supported install entry points should converge on the same safety flow:

```text
Local DLL / Local ZIP / Thunderstore
→ Package Inspector
→ install plan
→ dependencies/conflicts
→ staging
→ transactional writes
→ ownership registry
```

Static metadata inspection is used to distinguish BepInEx `BaseUnityPlugin`, MelonLoader `MelonMod` and `MelonPlugin` without loading untrusted assemblies into the manager process.

Typical destinations:

- BepInEx plugin → `BepInEx/plugins/...`
- MelonMod → `Mods/...`
- MelonPlugin → `Plugins/...`

Mixed/ambiguous packages are rejected instead of guessed.

## 6. Ownership model

Managed Mods have installation records describing the files HTF Manager owns. This enables deterministic update/uninstall operations.

External/manual Mods are scanned and displayed, but HTF Manager does not claim ownership simply because it recognizes them.

Loader ownership is tracked separately from Mod ownership.

## 7. Package Inspector

Package Inspector is read-only until the user confirms an installation. It reports:

- package identity/source;
- loader/component type;
- version;
- install destinations;
- file count;
- dependencies;
- conflicts;
- risk state.

Unsafe package paths and protected game targets must be blocked before staging is committed.

## 8. Configuration Center

Configuration Center scans supported BepInEx and MelonLoader configuration sources and maps parsed entries into native Avalonia controls.

Saving edits modifies the relevant values while preserving unknown configuration content as much as possible. Saves are blocked while the game is running and backups are created before writes.

Known loader configuration may have reviewed local Chinese presentation mappings. Unknown third-party Mod configuration is shown using the author's source text; no AI/automatic guessing is used.

Developer Mode controls the amount of metadata shown in the UI. It is not a safety bypass.

## 9. Profiles, expected state and configuration snapshots

Profiles contain desired Mod enabled/disabled state and optional unresolved Mod requirements. v0.3.7 adds a durable `ExpectedMods` inventory so a profile can preserve the identity and expected version of resolved members instead of deriving those values from the machine's current installation later.

The intended relationship is:

```text
ExpectedMods = canonical desired inventory
ModStates = currently resolved local apply bindings
UnresolvedMods = compatibility projection for requirements with no local binding
```

Legacy v0.3.6 profiles are migrated conservatively in memory. Existing unresolved requirements retain complete metadata; resolved legacy bindings are marked `LegacyBindingOnly` rather than inventing a historical version.

`ProfileHealthService` is read-only and compares expected state with installed Mods. It reports `Healthy`, `Missing`, `VersionMismatch`, or `IdentityUncertain`. Trusted provider `PackageKey` identity takes precedence over local identity; when no provider key exists, v0.3.7 can use a deterministic intrinsic Mod identity extracted from static assembly metadata (for BepInEx, the `BepInPlugin` GUID) before falling back to saved local bindings/name/file matching. Duplicate intrinsic identities are ambiguous rather than guessed. Health calculation does not install packages, change expected versions, or mutate the game directory.

v0.3.4 adds optional per-profile configuration snapshots. Snapshots use paths relative to the game root and store SHA-256 hashes. Applying a profile:

```text
validate game not running
→ validate profile/snapshots
→ create recovery copy of current affected configs
→ apply Mod enabled states
→ restore eligible profile config snapshots
→ rollback changed states/configs on failure
```

Loader-global settings are excluded from ordinary Mod profile snapshots.

## 10. Portable profiles

v0.3.5 exports/imports `.htfprofile` ZIP containers containing a manifest and optional configuration snapshots.

Portable identity uses source/package metadata rather than local ownership IDs so the same Mod can be matched on another machine.

Import validates:

- schema/version;
- archive paths;
- entry/count/size limits;
- duplicate portable identities;
- configuration target restrictions;
- configuration hashes.

Missing Mods become unresolved profile requirements. Profiles with unresolved requirements cannot be applied unless the requirements are installed/re-matched or explicitly removed.

Lightweight `.htfprofile` files never bundle third-party Mod binaries, loader runtimes or game files. v0.3.7 separately introduces the `.htfbundle` container for explicitly eligible package artifacts; that format remains profile-first and must not turn opening a bundle into automatic installation.

## 11. Profile Restore Assistant

v0.3.6 adds a restoration orchestration layer for unresolved portable-profile requirements. The planner is read-only and classifies each unresolved requirement as `Ready`, `VersionFallback`, `PackageUnavailable`, or `ManualRequired`.

Remote automatic resolution remains intentionally limited to exact Thunderstore `PackageKey` matches. v0.3.7 additionally allows an exact `.htfbundle` payload for a requirement that is currently `Missing`; this can include an eligible local managed Mod identified by a deterministic intrinsic identity. When a Thunderstore requested version is available it is selected directly; when only another downloadable version is available the item is marked as `VersionFallback` and requires explicit acknowledgement. The planner never guesses a package from display text or filename alone.

Installable candidates reuse the existing remote preparation and Package Inspector path:

```text
Profile unresolved requirement
→ ProfileRestoreService plan
→ exact Thunderstore package/version candidate
→ Package Inspector
→ existing dependency/conflict checks
→ existing transactional install
→ refresh local Mods
→ ResolveMissingMods
```

Restore Assistant does not automatically apply the profile after restoration completes.

## 12. Local manager data

HTF Manager stores its own state under:

```text
%LOCALAPPDATA%\HTFManager\
```

The game directory is not used as the manager's database.

Data categories include settings, profiles, Mod/loader ownership records, caches, backups, profile snapshots and recovery transactions.

## 13. Repository and incremental-package discipline

`main` is the stable development baseline. Before starting a feature step, record the required baseline version. Local development packages should be ZIP overlays containing only changed/new repository files plus the relevant notes; unchanged source and generated output should not be duplicated.

`VERSION`, `PROJECT_STATE.json` and `SESSION_HANDOFF.md` should be updated whenever the stable baseline changes.

Generated build output, game files, downloaded loader/Mod binaries and local application data do not belong in Git.

## 14. v0.3.7 portable-bundle baseline

v0.3.7 is **Portable Profile Bundle & Health**. `ExpectedMods` is the canonical desired inventory and `ProfileHealthService` compares that desired state with the current machine as `Healthy`, `Missing`, `VersionMismatch` or `IdentityUncertain`. The calculation is read-only; only `Missing` blocks profile application.

Full sharing uses `.htfbundle`, a ZIP-compatible profile-first container with exactly one root `bundle.json`, one root `profile.htfprofile` and optional verified package payloads. `PackageArtifactStore` resolves only HTF-managed retained source artifacts whose SHA-256 still matches the current installation record. Thunderstore packages use provider `PackageKey`; eligible local BepInEx DLL/ZIP installs may use their intrinsic `BepInPlugin` GUID plus exact version without fabricating a provider key. External/development Mods, loaders, ambiguous local packages, reconstructed live-game directories and dependency closure are not automatically bundled.

`ProfileBundleService` owns bundle export, structural/security validation, embedded-profile inspection and lazy payload materialization. Opening a bundle never installs a Mod: the embedded profile is validated first, environment health is computed, and only a requirement that is actually `Missing` may expose an exact bundled payload. Healthy requirements suppress duplicate installation, while `VersionMismatch` remains warning-only in v0.3.7 even when the expected payload is present.

Bundled payloads are transport artifacts, not a second installation path. A selected payload is lazily extracted to staging, checked against declared size/SHA-256 and profile identity, then passed to the existing Package Inspector and transactional installer with the original logical source/PackageKey/version metadata preserved. The received bundle path is session-only and is never persisted into `ModProfile`; successfully installed payloads enter the normal managed package cache through the existing installer.

The restore planner gives an exact bundle payload priority over Thunderstore for `Missing` requirements and can operate offline when every unresolved requirement is already classifiable from the bundle/manual state. Remote fallback behavior remains the existing v0.3.6 behavior for genuinely missing Thunderstore requirements. Automatic version replacement/downgrade reconciliation is explicitly outside v0.3.7 and remains a later release. Nexus support also remains a later provider integration.

The canonical v0.3.7 scope and exclusions are defined in `docs/V0.3.7_SCOPE_LOCK.md`.

## 15. Active v0.3.8 subsystem — version reconciliation

v0.3.8 is **Version Reconciliation & Application Delivery**. It keeps `ProfileHealthService` read-only, but adds `ProfileVersionReconciliationService` to plan explicit actions for `VersionMismatch` items. Reconciliation never treats a different available version as good enough. Exact source priority is active imported bundle payload, retained exact artifact history, exact Thunderstore version, then manual resolution.

`PackageArtifactStore` now maintains content-addressed verified history under the manager data directory. Before a managed replacement the current verified source artifact is preserved when available; successful new installs capture their source artifact when package caching is enabled. Historical lookups require deterministic identity, the same logical source, exact version, file size and SHA-256. A provider-backed requirement cannot fall back to an intrinsic local identity.

The restore path remains:

```text
VersionMismatch
→ exact reconciliation plan
→ explicit Restore expected version
→ Package Inspector
→ existing transactional installer
→ refresh local Mods / health
```

Because the existing installer already backs up owned target files, removes obsolete owned files, commits new files and rolls back failed transactions, reconciliation reuses that path rather than creating a second version-replacement engine. User configuration remains preserved by the existing installer policy.

`AcceptInstalledVersion` is a separate explicit profile mutation. It is allowed only for a deterministic identity match with a known installed version. It updates the profile expectation/binding and never fabricates a provider identity or claims external/manual ownership.

A `.htfbundle` opened on a mismatched receiver may expose its exact expected payload to the reconciliation planner, but this does not authorize installation by itself. Full export may also use a verified historical artifact for the profile-expected version when the sender's currently installed version has drifted. The profile desired state remains authoritative.

## 16. Application delivery and updates

v0.3.8 introduces a supported Windows release artifact built as `.NET 10`, `win-x64`, self-contained, single-file `HTFManager.exe`, with trimming disabled. `build/publish-win-x64.ps1` creates the executable plus `update-manifest.json` and `SHA256SUMS.txt`; `.github/workflows/release.yml` validates a version tag, runs build/tests, publishes those assets and attaches them to the GitHub Release.

`GitHubReleaseUpdateService` treats the public stable GitHub Release as update metadata. It compares the running application version to the latest stable tag, validates update-manifest schema/channel/RID/version/asset metadata, streams the executable to `%LOCALAPPDATA%\HTFManager\updates`, then verifies size and SHA-256 before marking the update ready. Automatic startup checks are throttled by `LastUpdateCheckUtc`; manual **Check now** bypasses the throttle.

`WindowsApplicationUpdateApplier` is intentionally separate from network/download logic. It only enables self-replacement for a supported published single-file Windows executable in a writable directory. The running EXE is copied to a temporary update-host path, the main process exits, and that temporary host verifies the staged SHA-256 again, backs up the current target to `.old`, replaces it, starts the new executable, and restores the old target if replacement/startup application fails synchronously.

The update subsystem never modifies `%LOCALAPPDATA%\HTFManager` profiles/package history except its own `updates` staging directory. v0.3.8 does not include UAC elevation, forced updates, delta updates, prerelease channels, installers or Authenticode verification. Those are later distribution-hardening concerns.

The canonical v0.3.8 scope is `docs/V0.3.8_SCOPE_LOCK.md`; the updater file contract is `docs/UPDATE_MANIFEST_V1.md`.
