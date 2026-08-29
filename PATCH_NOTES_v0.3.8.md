# HTF Manager v0.3.8 — Version Reconciliation & Application Delivery

v0.3.8 completes the version-drift workflow introduced by Profile Health and adds the first supported self-contained Windows executable/update pipeline.

## Profile version reconciliation

`VersionMismatch` is no longer only informational. A deterministic mismatch can now expose explicit actions:

- **Restore expected version** — locate the exact version from the active bundle, retained artifact history, or Thunderstore; open Package Inspector; then reuse the normal transactional installer.
- **Accept installed version** — explicitly rewrite that profile expectation to the currently installed deterministic version.
- **Leave unchanged** — keep the mismatch visible.

Reconciliation never silently picks a newer/older fallback. The expected version string remains authoritative.

## Versioned package artifact history

Managed package retention now preserves verified historical versions when available. Records include deterministic identity, source, exact version, file size, SHA-256, and the content-addressed stored file.

This enables offline reconciliation for an older local or Thunderstore package when HTF Manager still has the exact verified artifact. Full `.htfbundle` export can also carry a retained exact expected version even if the sender's current installation has drifted.

Provider `PackageKey` remains stronger than local `IntrinsicId`; identity is never guessed from display text or filenames.

## Safe replacement

Exact-version restoration still crosses Package Inspector before installation. The existing transactional installer handles replacement, obsolete owned-file cleanup, rollback, ownership records, and preservation of user configuration according to existing policy.

## Windows executable distribution

v0.3.8 adds a `win-x64` .NET 10 self-contained single-file publish profile. The release helper generates:

```text
HTFManager.exe
update-manifest.json
SHA256SUMS.txt
```

Users of the published EXE do not need to install a separate .NET runtime. Development still requires the .NET 10 SDK.

## Application updates

Settings now includes application update controls. Stable checks use the public GitHub Releases latest endpoint and are throttled to once per 24 hours unless **Check now** is selected.

A downloaded update is streamed to local staging, size/SHA-256 verified, and only then becomes eligible for **Restart and update**. The running single-file EXE copies itself to a temporary update-host path, exits, and lets that host back up/replace/relaunch the application. Failed replacement attempts restore the previous executable when possible.

Automatic replacement is intentionally unavailable when the app is not running as the supported published single-file EXE or its directory is not writable.

## Release automation

A tag-driven GitHub Actions release workflow now:

1. restores dependencies;
2. builds Release;
3. runs the automated suite;
4. publishes the win-x64 single-file executable;
5. generates update manifest/SHA-256 files;
6. attaches those artifacts to the GitHub Release.

## Validation target

The v0.3.8 implementation candidate contains **71 deterministic tests** before local Windows release validation.

Release validation must still include:

- clean restore/build/test on Windows with .NET 10;
- a real version-replacement round trip;
- `Accept installed version` profile mutation check;
- local `IntrinsicId` reconciliation check;
- execution of the produced `HTFManager.exe` on a machine without relying on `dotnet run`;
- local inspection of generated `update-manifest.json` and SHA-256;
- Settings update-check behavior against the published stable GitHub Release.

## Deliberate exclusions

v0.3.8 does not add forced/silent Mod reconciliation, fuzzy identity, Nexus integration, automatic profile Apply, UAC elevation, installers, delta application updates, prerelease channels, or Authenticode signing.
