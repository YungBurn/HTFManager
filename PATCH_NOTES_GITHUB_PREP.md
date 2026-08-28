# v0.3.5.1 GitHub Preparation Patch

This repository-only patch does not change HTF Manager runtime behavior.

It prepares the verified v0.3.5.1 source baseline for public GitHub hosting and future cross-session development by adding/updating:

- repository-safe `.gitignore`;
- `.gitattributes`;
- public README updated to v0.3.5.1;
- `VERSION` and machine-readable `PROJECT_STATE.json`;
- `SESSION_HANDOFF.md` for new development conversations;
- current `docs/ARCHITECTURE.md`;
- `docs/DEVELOPMENT.md`;
- first-upload instructions in `GITHUB_UPLOAD.md`;
- Windows/.NET 10 GitHub Actions build validation;
- `build/export-handoff.ps1` for creating small source-only cross-session archives;
- generalized `INSTALL_PATCH.md` guidance.

No C# or AXAML runtime files are modified by this patch.
