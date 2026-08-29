# HTF Manager Downloads

Official HTF Manager Windows releases are distributed through GitHub Releases:

[Download HTF Manager releases](https://github.com/YungBurn/HTFManager/releases)

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/),
certificate by [SignPath Foundation](https://signpath.org/).

See the full project
[Code signing policy](../README.md#code-signing-policy)
for build integrity, signing roles, approval requirements, and privacy
information.

HTF Manager release binaries are intended to be built from the project's
public source repository through GitHub Actions.

Release signing is being integrated into the official HTF Manager release
pipeline. Older releases and local development/release-candidate builds may
be unsigned.

For signed releases, release hashes and `update-manifest.json` are generated
after Authenticode signing so that published SHA-256 values correspond to the
final distributed executable.

## Official release assets

Official Windows releases may include:

- `HTFManager.exe`
- `update-manifest.json`
- `SHA256SUMS.txt`

`HTFManager.exe` is published for Windows x64 as a .NET 10 self-contained
single-file application.

End users do not need to install a separate .NET runtime for the official
self-contained Windows executable.

## Release integrity

The intended official release pipeline is:

```text
Public source / release tag
        ↓
Restore dependencies
        ↓
Release build
        ↓
Automated tests
        ↓
Publish win-x64
        ↓
Authenticode signing
        ↓
Verify signature
        ↓
Generate final SHA-256
        ↓
Generate update-manifest.json
        ↓
Generate SHA256SUMS.txt
        ↓
Publish GitHub Release
```

Authenticode signing changes the executable bytes.

Therefore, the final SHA-256 checksum and update manifest must be generated
after signing rather than before signing.

Until the external code-signing integration has been completed and validated,
locally generated development and release-candidate executables should be
treated as unsigned builds.

## Application updates

HTF Manager can check the project's stable GitHub Releases for application
updates.

The application-update path validates:

- release version;
- release channel;
- runtime identifier;
- expected executable asset;
- download URL;
- expected file size;
- SHA-256.

Same-version and older-version releases are not treated as available updates.

Downloaded executables must pass the expected size and SHA-256 validation
before they can be staged for replacement.

Application updates remain explicit and user-controlled.

HTF Manager does not intentionally perform:

- forced application updates;
- silent background executable replacement;
- automatic downgrade;
- automatic UAC elevation.

Automatic application update checks can be disabled in HTF Manager settings.

## Privacy

HTF Manager does not intentionally collect telemetry or analytics.

HTF Manager will not transfer information to other networked systems unless
specifically requested by the user or required by a user-enabled application
feature.

Network access can occur for functionality including:

- accessing supported Mod package providers such as Thunderstore;
- checking GitHub Releases for HTF Manager application updates;
- downloading an application update after the relevant user action.

Profile data, configuration snapshots, managed-package history, ownership
records, recovery data, and game files are not intended to be uploaded as
part of ordinary package browsing or application-update operations.

Local application state remains on the user's machine unless the user
explicitly performs an export/share operation.

## Download safety

Users should download HTF Manager only from the official project repository
and its GitHub Releases page:

[HTF Manager GitHub Releases](https://github.com/YungBurn/HTFManager/releases)

For releases that provide `SHA256SUMS.txt`, users can independently verify the
downloaded executable hash before running it.

On Windows PowerShell:

```powershell
Get-FileHash .\HTFManager.exe -Algorithm SHA256
```

Compare the resulting SHA-256 value with the value published in
`SHA256SUMS.txt` for the same release.

Unsigned older releases or development builds may trigger Microsoft Defender
SmartScreen warnings because Windows cannot identify a trusted publisher.

Official Authenticode signing is being integrated to improve release identity
and Windows trust for future signed builds.