# HTF Bundle Schema v1 (`.htfbundle`)

## Purpose

`.htfbundle` is the full portable-profile container introduced in v0.3.7. It is ZIP-compatible on disk but uses a dedicated extension so HTF Manager can distinguish it from an ordinary Mod `.zip` package.

The archive is a **profile container with optional package payloads**, not an installer.

## Required archive layout

```text
<name>.htfbundle
├─ bundle.json
├─ profile.htfprofile
└─ payload/
   └─ <portable-id>/
      └─ <artifact>
```

`bundle.json` is authoritative for payload mapping. Folder names alone must never be used to infer package identity.

## Root rules

Schema v1 permits exactly:

- one root `bundle.json`;
- exactly one root `profile.htfprofile` entry referenced by `bundle.json`;
- zero or more payload entries referenced by the manifest;
- no executable behavior from arbitrary unreferenced archive entries.

Readers should reject ambiguous duplicate critical entries.

## Manifest shape

Illustrative contract:

```json
{
  "schemaVersion": 1,
  "generatedWithVersion": "0.3.8",
  "profileEntry": "profile.htfprofile",
  "profileSha256": "...",
  "payloads": [
    {
      "portableId": "...",
      "packageKey": "Author-ModName",
      "intrinsicId": null,
      "version": "1.2.0",
      "source": "Thunderstore",
      "artifactKind": "Archive",
      "entry": "payload/<portable-id>/package.zip",
      "sha256": "...",
      "uncompressedSize": 123456
    }
  ]
}
```

Field naming may be adjusted during implementation, but the following semantics are required.

### `schemaVersion`

Integer bundle-container schema version. Initial value: `1`.

This is independent of the embedded `.htfprofile` schema version.

### `generatedWithVersion`

HTF Manager application version that created the bundle.

### `profileEntry`

Schema v1 requires this to be the root path `profile.htfprofile`. Keeping the location fixed avoids ambiguous critical-entry layouts while the manifest still records the path explicitly for forward compatibility.

### `profileSha256`

SHA-256 of the exact embedded profile bytes.

### `payloads`

Optional payload descriptors. Every descriptor must map back to one expected requirement from the embedded profile.

Required payload identity data:

- `portableId`;
- `packageKey` when the profile has a trusted provider identity;
- `intrinsicId` when a local Mod has a deterministic intrinsic identity and no provider key;
- expected `version` when known;
- `source`;
- artifact kind;
- archive entry path;
- SHA-256;
- uncompressed byte size.

## Artifact kinds

Schema v1 planning supports only artifacts already compatible with existing Package Inspector flows, for example:

- `Archive` — a Mod ZIP/package archive;
- `Assembly` — a managed local DLL artifact when the local inspector supports it safely.

Do not invent a generic raw-directory artifact in v1.

## Identity validation

Before a payload can be offered for restore, its manifest identity must agree with the embedded profile requirement.

At minimum validate, when present:

```text
portableId
PackageKey
IntrinsicId
expected version
source
```

A matching hash does not override an identity mismatch.


### Local intrinsic identity

Schema v1 keeps provider and local identity separate. A local managed package may have `packageKey = null` and an `intrinsicId` obtained from static Mod metadata. For BepInEx this is the `BepInPlugin` GUID (for example `com.moddle.howtofish.truedot`). HTF Manager must not fabricate a Thunderstore-style `PackageKey` from this value.

A local payload is eligible only when the corresponding profile expectation has the same deterministic intrinsic identity, an exact expected version, a matching logical source, and a verified retained HTF-managed source artifact. Ambiguous or missing intrinsic identity remains manual.

## Read order

Readers must follow this order:

```text
open ZIP read-only
→ validate critical entry paths/counts
→ read bundle.json
→ read/verify embedded .htfprofile
→ inspect profile
→ compute environment state
→ inspect payload descriptors
```

Payload bytes are not extracted during initial inspection. A compatible exact payload may be associated with either a `Missing` item or, beginning with v0.3.8, a `VersionMismatch` item for explicit version reconciliation. Merely exposing that descriptor does not authorize installation.

## Lazy extraction

When a user explicitly selects a bundled package for inspection:

```text
manifest entry
→ safe temporary extraction
→ SHA-256 verification
→ identity verification
→ Package Inspector
```

Only the selected payload should be materialized.

## Security requirements

Reject/guard against:

- absolute paths;
- Windows drive/UNC paths;
- `..` traversal;
- extraction outside canonical staging directory;
- duplicate `bundle.json` or ambiguous profile entries;
- duplicate manifest payload entry paths;
- duplicate portable IDs unless schema explicitly allows it;
- symlink/reparse-like entries when detectable;
- manifest/profile files above defined safe limits;
- excessive entry count;
- excessive per-entry uncompressed size;
- excessive aggregate uncompressed size;
- declared size differing materially from actual extracted size;
- payload SHA-256 mismatch;
- payload descriptor identity inconsistent with embedded profile.

Implementation should use streaming reads/writes and checked size accumulation to reduce ZIP-bomb/memory-exhaustion risk.

## Configuration snapshots

Profile configuration snapshots remain inside the embedded `.htfprofile`.

Payload packages must not be treated as a second authoritative configuration snapshot.

## Loader handling

Bundle schema v1 does not automatically include game loaders. Loader requirements continue through the existing Package Inspector/Loader Setup boundary.

## Dependencies

Bundle schema v1 does not require dependency-closure packaging. Payload entries primarily correspond to explicit profile members. Existing dependency inspection/resolution remains authoritative during installation.

## External/unmanaged Mods

Schema v1 can describe that a profile expects such a Mod, but the Full Share exporter must not automatically package arbitrary external/unmanaged files.

## Forward compatibility

Unknown optional fields may be ignored only when they do not change security or identity semantics.

An unknown higher `schemaVersion` must not be treated as schema v1.
