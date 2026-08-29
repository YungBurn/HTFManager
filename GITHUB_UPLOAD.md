# Publishing HTF Manager v0.3.7 to GitHub

Repository:

```text
https://github.com/YungBurn/HTFManager
```

Development branch:

```text
feature/v0.3.7-profile-health-version-reconciliation
```

Release name:

```text
HTF Manager v0.3.7 — Portable Profile Bundle & Health
```

The release workflow is **local validation → feature commits → push → Pull Request → green Actions → main → annotated tag/release**. Do not force-push `main`.

## 1. Final local release gate

After applying the final v0.3.7 release overlay, run:

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet test --project .\tests\HTFManager.Tests\HTFManager.Tests.csproj --configuration Release --no-build --no-restore
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

Expected automated suite after the local-identity enhancement: **54 passing tests**.

Manual release checks should include:

- lightweight `.htfprofile` export/import still works;
- Full share produces `.htfbundle`;
- importing `.htfbundle` reads the embedded profile first and installs nothing automatically;
- an already healthy Mod is not offered for duplicate installation;
- a missing exact bundled Mod enters the existing Package Inspector only after explicit action;
- `VersionMismatch` is detected but not automatically replaced;
- a manifest-less managed BepInEx local package with a deterministic `BepInPlugin` GUID/version can be included in Full share;
- an ambiguous/duplicate intrinsic identity is not automatically matched or bundled;
- no loader/game/external unmanaged files are silently bundled.

## 2. Review repository changes

```powershell
git status --short
git diff --check
git diff --stat
```

Do not commit:

- `bin/`, `obj/` or publish output;
- local `.htfbundle`/`.htfprofile` test files;
- downloaded third-party Mod archives/DLLs used for smoke testing;
- game files;
- `%LOCALAPPDATA%\HTFManager` runtime/cache data;
- temporary patch/overlay ZIP files.

## 3. Commit the local v0.3.7 work

If Phase A/full implementation changes are still uncommitted, prefer a few meaningful commits rather than one giant implementation-history dump. For example:

```powershell
git add .
git diff --cached --check
git commit -m "Add v0.3.7 portable profile bundle and health"
```

If the earlier foundation is already committed separately, the final identity/release closure can be:

```powershell
git add .
git diff --cached --check
git commit -m "Finalize v0.3.7 local Mod identity and release metadata"
```

The working tree should be clean before push.

## 4. Push the feature branch

```powershell
git push -u origin feature/v0.3.7-profile-health-version-reconciliation
```

No force push is required for normal local development unless the feature branch history was intentionally rebased. Never use a blind `--force`; use `--force-with-lease` only when a rebase actually requires it.

## 5. Create the Pull Request

```text
base:    main
compare: feature/v0.3.7-profile-health-version-reconciliation
```

Suggested title:

```text
HTF Manager v0.3.7 — Portable Profile Bundle & Health
```

Suggested summary:

```text
- preserves durable expected Mod identity/version state and adds read-only profile health
- adds Lightweight (.htfprofile) and Full (.htfbundle) sharing
- adds verified package-artifact bundling with profile-first import and lazy extraction
- restores only genuinely missing exact bundled Mods through the existing Package Inspector/transactional installer
- suppresses duplicate installs for already healthy requirements
- detects VersionMismatch without automatic downgrade/upgrade
- adds deterministic local BepInEx intrinsic identity so eligible managed local Mods can be bundled without fabricating a Thunderstore PackageKey
- expands automated tests and promotes application/profile/bundle metadata to v0.3.7
```

Wait for GitHub Actions to pass before merging.

## 6. Merge and tag

After the PR is green and merged:

```powershell
git switch main
git pull --ff-only origin main
git tag -a v0.3.7 -m "HTF Manager v0.3.7 - Portable Profile Bundle & Health"
git push origin v0.3.7
```

Then create a GitHub Release using tag `v0.3.7` and `PATCH_NOTES_v0.3.7.md` as the release-note source.

## 7. Start v0.3.8 only after v0.3.7 is released

```powershell
git switch main
git pull --ff-only origin main
git switch -c feature/v0.3.8-profile-version-reconciliation
git push -u origin feature/v0.3.8-profile-version-reconciliation
```

v0.3.8 owns explicit version reconciliation. Do not retroactively add downgrade/upgrade behavior to the v0.3.7 release branch.
