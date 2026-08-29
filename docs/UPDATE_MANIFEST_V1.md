# HTF Manager Update Manifest v1

`update-manifest.json` is the machine-readable contract between a stable GitHub Release and HTF Manager's application updater.

Example:

```json
{
  "schemaVersion": 1,
  "channel": "stable",
  "version": "0.3.9",
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
- `asset`: v1 requires exactly `HTFManager.exe`.
- `size`: expected executable length in bytes, greater than zero.
- `sha256`: 64 hexadecimal characters for the executable contents.
- `publishedAt`: UTC timestamp generated with the release assets.

## Release discovery validation

Before exposing an update as installable, HTF Manager verifies that:

1. GitHub's latest tag parses as a supported application version and is strictly newer than the running version;
2. the release contains `update-manifest.json` and the expected executable asset;
3. manifest and executable download URLs are HTTPS;
4. manifest schema/channel/RID are supported;
5. manifest version matches the GitHub release tag;
6. manifest asset is exactly `HTFManager.exe`;
7. manifest SHA-256 is structurally valid;
8. manifest size is greater than zero;
9. manifest asset size matches GitHub release metadata when GitHub reports a size.

A same-version or older latest release is considered `UpToDate`, not an installable update.

## Download validation

The executable is downloaded into a unique `.download-*` temporary file.

During download HTF Manager:

1. rejects a reported `Content-Length` that differs from the manifest size;
2. enforces the manifest size as a streaming upper bound even when the server does not provide `Content-Length`;
3. requires the final file length to equal the manifest size exactly;
4. computes SHA-256 and requires an exact match;
5. moves the file into the stable staging path only after validation succeeds.

Partial `.download-*` files are removed after failure or cancellation. A previously staged executable can be reused only after its length and SHA-256 are recomputed and still match the current manifest.

## Apply-time validation

`Restart and Update` revalidates the staged executable immediately before creating the temporary Update Host.

The Update Host independently verifies:

- expected executable size;
- expected SHA-256;
- replacement target availability.

After replacement, the new executable must complete application initialization and write a private startup acknowledgement. The old `.old` backup is removed only after that acknowledgement is observed.

If the new executable exits before acknowledgement or the acknowledgement times out, the Update Host attempts to stop the new process, restore the old executable, and relaunch the previous version.

## Trust boundary

The manifest and executable are both retrieved over HTTPS from GitHub Releases and linked by size plus SHA-256. This is an integrity mechanism and guards against accidental/mismatched release assets and corrupted downloads.

Manifest v1 does **not** claim publisher authenticity equivalent to Windows Authenticode. Authenticode signing and expected-publisher enforcement are separate hardening layers and are not required by the v1 schema.
