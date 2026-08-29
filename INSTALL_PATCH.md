# Applying HTF Manager incremental development file packages

HTF Manager local development packages are designed as baseline-specific overlays containing only files that are new or changed for that step. Binary `.patch` files are not required for the current local-first workflow.

Before applying a future incremental file package:

1. Check the current version in `VERSION` / `PROJECT_STATE.json`.
2. Make sure `git status` is clean or commit/stash local work.
3. Extract the ZIP into the repository root and replace only the files included in the package.
4. Run:

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx
```

5. Run the application and test the workflow changed by the patch.
6. Review `git diff` before committing.

Do not apply a package intended for a newer baseline onto an older tree unless the package explicitly documents that compatibility. Because the package contains only changed/new files, it assumes all files from earlier validated steps are already present.
