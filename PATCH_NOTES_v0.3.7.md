# HTF Manager v0.3.7 — Portable Profile Bundle & Health

v0.3.7 turns portable profiles into a profile-first environment-sharing workflow while preserving the existing Package Inspector, ownership and transactional-install safety boundaries.

## Highlights

### Durable profile expected state

Profiles now preserve expected Mod metadata independently from the machine's current installed state. This allows HTF Manager to report:

- `Healthy`
- `Missing`
- `VersionMismatch`
- `IdentityUncertain`

Expected metadata is preserved through capture/import/export rather than silently drifting when the local installed version changes.

### Lightweight and Full sharing

Profile sharing now supports:

- **Lightweight** — `.htfprofile`, metadata/configuration only;
- **Full** — `.htfbundle`, embedded `.htfprofile` plus eligible verified HTF-managed source artifacts.

Opening a `.htfbundle` never installs a Mod automatically. HTF Manager validates the container, reads/verifies the embedded profile first, computes environment health, and only then exposes exact bundled candidates for requirements that are actually missing.

### Verified artifact bundling

Full share only uses retained HTF-managed source artifacts whose SHA-256 still matches the current installation record. HTF Manager does not reconstruct a package by scraping live game directories.

External/unmanaged Mods, development Mods, loader binaries, ambiguous local packages, game files and dependency closure are not automatically bundled.

### Deterministic local Mod identity

v0.3.7 adds a distinct local `IntrinsicId` identity path. Provider identity and local identity remain separate:

```text
Thunderstore/provider Mod → PackageKey
local deterministic Mod   → IntrinsicId
```

For BepInEx assemblies, HTF Manager statically reads the `BepInPlugin` GUID/name/version from PE metadata without loading the untrusted assembly. A managed local package such as:

```text
IntrinsicId = com.moddle.howtofish.truedot
Version     = 1.0.0
PackageKey  = null
```

can therefore participate in Full share when its retained artifact is verified. HTF Manager never fabricates a Thunderstore-style `PackageKey` from this local ID.

Duplicate intrinsic IDs are treated as ambiguous rather than selecting the first candidate.

### Safe bundled restore

A missing exact bundled payload is lazily extracted to temporary staging, checked against declared size/SHA-256 and the embedded profile identity, then sent through the existing Package Inspector and transactional installer.

Already healthy Mods suppress duplicate installation. `VersionMismatch` remains detection-only in v0.3.7; the presence of the expected payload does not authorize a downgrade or replacement.

### Security

`.htfbundle` validation rejects unsafe/ambiguous structures including path traversal/rooted paths, symlink entries, duplicate critical entries, unsupported schema, unreferenced files, hash mismatches, identity mismatches and declared-size/entry-count limits.

## Compatibility

- Existing `.htfprofile` files continue to load.
- Legacy v0.3.6 profile bindings are migrated conservatively without inventing lost historical versions.
- Existing v0.3.6 Restore Assistant remote behavior remains available for missing Thunderstore requirements.
- A previously installed local package whose old profile expectation lacks exact version/intrinsic metadata is not silently redefined; re-capture/update the profile expectation before expecting Full-share eligibility.

## Explicitly not included

v0.3.7 does **not** add:

- automatic version downgrade/upgrade reconciliation;
- adopting the installed version as a new profile baseline;
- silent/batch Install All;
- Nexus integration;
- dependency-closure bundle export;
- loader bundling;
- external/unmanaged Mod takeover;
- automatic profile Apply.

Those version-reconciliation actions are planned for v0.3.8.

## Release validation

Before merge/tag, run on Windows with .NET 10:

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet test --project .\tests\HTFManager.Tests\HTFManager.Tests.csproj --configuration Release --no-build --no-restore
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

Expected suite after the final local-identity enhancement: **54 tests**.
