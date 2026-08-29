# HTF Manager Downloads

Official HTF Manager Windows releases are distributed through GitHub Releases:

[Download HTF Manager releases](https://github.com/YungBurn/HTFManager/releases)

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/),
certificate by [SignPath Foundation](https://signpath.org/).

See the full project
[Code signing policy](../README.md#code-signing-policy)
for build integrity, signing roles, and privacy information.

HTF Manager release binaries are built from the project's public source
repository through GitHub Actions.

Release signing is being integrated for the v0.3.8 release pipeline.
Older releases may be unsigned.

For signed releases, release hashes and `update-manifest.json` are generated
after Authenticode signing so that published SHA-256 values correspond to the
final distributed executable.

## Release integrity

Official Windows release assets may include:

- `HTFManager.exe`
- `update-manifest.json`
- `SHA256SUMS.txt`

Users should download HTF Manager only from the official GitHub repository
and its GitHub Releases page.