# HTF Manager v0.3.9 — Update Hardening & Reconciliation UX

v0.3.9 focuses on hardening the v0.3.8 application-delivery path and clarifying version-reconciliation UI. It is intentionally not a large feature expansion.

## Application update hardening

- Rejects same-version and downgrade candidates.
- Requires the stable v1 update manifest to match the GitHub release tag.
- Requires the release executable asset to be exactly `HTFManager.exe`.
- Requires HTTPS manifest and executable asset URLs.
- Validates release asset metadata before download.
- Enforces the declared executable size while streaming the download, preventing an oversized response from being accepted.
- Verifies final executable size and SHA-256 before staging.
- Cleans partial `.download-*` files after failures and cancellation.
- Reuses an existing staged executable only after revalidating its size and SHA-256.
- Revalidates staged size and SHA-256 immediately before launching the Update Host.

## Safer restart-and-update transaction

The temporary Update Host now requires a startup acknowledgement from the replacement executable.

```text
old HTFManager.exe exits
→ Update Host verifies staged size + SHA-256
→ old EXE becomes HTFManager.exe.old
→ staged EXE replaces target
→ new EXE launches with a private update acknowledgement path
→ Avalonia application initialization completes
→ new EXE writes acknowledgement
→ Update Host removes .old and staged files
```

If the new executable exits early or does not acknowledge startup within the timeout, the host stops the attempted new process, restores `HTFManager.exe.old`, and relaunches the previous executable.

This converts rollback from a synchronous `Process.Start` check into a real startup-confirmation boundary.

## Version reconciliation UX

Profile rows with real version drift now show both values explicitly, for example:

```text
Expected 1.0.2 · Installed 1.0.3
```

This avoids making a profile baseline look as if it changed merely because the installed Mod changed.

Package Inspector also identifies the resolver provenance used for exact-version reconciliation, such as:

- portable bundle;
- retained package history;
- Thunderstore.

## Portable bundle UX

The bundle-import counter previously labelled generically as bundled/recoverable is clarified to mean **missing + exact bundled payload**.

A `VersionMismatch` item whose exact expected version is present in the bundle now receives a dedicated badge indicating that an exact bundle payload is available.

## Update Settings UI

The Application Updates section now exposes:

- current version;
- latest version;
- update channel;
- release publish time;
- last checked time;
- expected download size;
- payload verification state.

## Automated validation target

v0.3.9 adds nine updater hardening tests on top of the v0.3.8 suite, bringing the expected suite to:

```text
80 tests
```

New coverage includes:

- downgrade suppression;
- release/manifest version mismatch;
- malformed SHA-256 metadata;
- unexpected executable asset names;
- non-HTTPS executable URLs;
- Content-Length mismatch;
- oversized streaming response rejection;
- partial download cleanup;
- verified staged-file reuse.

## Deferred validation

The real self-update test remains a release-stage validation until both published executables exist:

```text
v0.3.8 → v0.3.9
```

The release gate must verify successful replacement, startup acknowledgement, user-data preservation, and forced rollback behavior.

Authenticode enforcement is not enabled by this patch. Code signing can be layered onto the release pipeline separately once an official signing identity is available.
