# HTF Manager Update Manifest v1

`update-manifest.json` is the machine-readable contract between a stable GitHub Release and HTF Manager's application updater.

Example:

```json
{
  "schemaVersion": 1,
  "channel": "stable",
  "version": "0.3.8",
  "rid": "win-x64",
  "asset": "HTFManager.exe",
  "size": 123456789,
  "sha256": "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
  "publishedAt": "2026-08-29T08:00:00.0000000+00:00"
}
```

## Required fields

- `schemaVersion`: currently exactly `1`.
- `channel`: currently exactly `stable`.
- `version`: normalized application version, without a leading `v`.
- `rid`: currently `win-x64`.
- `asset`: a filename only; v1 uses `HTFManager.exe`.
- `size`: expected executable length in bytes, greater than zero.
- `sha256`: 64 hexadecimal characters for the executable contents.
- `publishedAt`: UTC timestamp generated with the release assets.

## Validation

Before exposing an update as installable, HTF Manager verifies that:

1. GitHub's latest tag parses as a supported application version and is newer than the running version.
2. the release contains exactly-addressable `update-manifest.json` and executable assets;
3. manifest schema/channel/RID are supported;
4. manifest version matches the GitHub release tag;
5. the asset name is a simple filename and exists on the release;
6. manifest asset size matches GitHub metadata when available;
7. the downloaded executable length and SHA-256 match the manifest.

A failed validation produces an update error and never starts replacement.

## Trust boundary

The manifest and executable are both retrieved over HTTPS from GitHub Releases, then linked by SHA-256. This is an integrity mechanism. v1 does not claim publisher authenticity equivalent to Windows Authenticode signing; signed release verification is a later hardening target.
