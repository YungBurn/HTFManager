# Applying HTF Manager development patches

HTF Manager patches are designed to be applied over a specific stable source baseline.

Before applying a future patch:

1. Check the current version in `VERSION` / `PROJECT_STATE.json`.
2. Make sure `git status` is clean or commit/stash local work.
3. Extract the patch into the repository root and replace the listed files.
4. Run:

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx
```

5. Run the application and test the workflow changed by the patch.
6. Review `git diff` before committing.

Do not apply a patch intended for a newer baseline onto an older tree unless the patch explicitly documents that compatibility.
