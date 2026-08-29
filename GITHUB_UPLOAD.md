# Publishing HTF Manager v0.3.6 to GitHub

Repository:

```text
https://github.com/YungBurn/HTFManager
```

Current development branch:

```text
feature/v0.3.6-profile-restore-assistant
```

The recommended release workflow is **feature branch → GitHub Pull Request → main → annotated tag**. Do not force-push `main`.

## 1. Apply the v0.3.6 release-state files

Copy the GitHub release/finalization file package into the repository root so `VERSION`, release notes, handoff documents, architecture metadata, visible app version, and export metadata all report v0.3.6.

## 2. Re-run the verified build

From the repository root:

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

The currently known baseline emits three non-blocking Avalonia `AVLN3001` warnings for `LoaderSetupDialog`, `PackageInspectorDialog`, and `ProfileImportDialog`. A successful build and application startup remain required.

## 3. Review exactly what will be committed

```powershell
git status --short
git diff --check
git diff --stat
```

Confirm that the changes do not include:

- `bin/` or `obj/`;
- publish output;
- game files;
- downloaded Mod or loader binaries;
- `%LOCALAPPDATA%\HTFManager` data;
- personal test profiles/configuration;
- cache/download directories.

## 4. Stage and commit v0.3.6

```powershell
git add .
git diff --cached --check
git status --short
git commit -m "Add v0.3.6 Profile Restore Assistant"
```

Review `git status` after the commit; the working tree should be clean.

## 5. Push the feature branch

```powershell
git push -u origin feature/v0.3.6-profile-restore-assistant
```

Do not push directly over `main` and do not use `--force`.

## 6. Open the Pull Request

On GitHub create a Pull Request:

```text
base:    main
compare: feature/v0.3.6-profile-restore-assistant
```

Suggested title:

```text
HTF Manager v0.3.6 — Profile Restore Assistant
```

Suggested summary:

```text
- adds deterministic restore planning for missing portable-profile requirements
- restores supported Thunderstore requirements through existing Package Inspector/install pipeline
- requires explicit acknowledgement for version fallback
- re-matches profiles after successful restoration
- adds HTF Manager application/taskbar logo
- promotes the verified development baseline to v0.3.6
```

Wait for the GitHub Actions build to pass before merging.

## 7. Merge to main

Use the normal GitHub merge flow. After the PR is merged, synchronize local `main`:

```powershell
git checkout main
git pull --ff-only origin main
```

Confirm the release commit is present:

```powershell
git log --oneline --decorate -n 10
```

## 8. Tag v0.3.6

Only tag after `main` contains the merged, green v0.3.6 build:

```powershell
git tag -a v0.3.6 -m "HTF Manager v0.3.6 - Profile Restore Assistant"
git push origin v0.3.6
```

A GitHub Release can then be created from tag `v0.3.6`, using `PATCH_NOTES_v0.3.6.md` as the release-note source.

## 9. Start v0.3.7 development

After `main` and tag `v0.3.6` are confirmed:

```powershell
git checkout main
git pull --ff-only origin main
git checkout -b feature/v0.3.7-profile-health-version-reconciliation
git push -u origin feature/v0.3.7-profile-health-version-reconciliation
```

The planned scope is documented in:

```text
docs/V0.3.7_PROFILE_HEALTH_AND_VERSION_RECONCILIATION.md
```

Keep the first v0.3.7 implementation patch focused on the expectation/health data model and read-only health calculation before adding repair UI or provider work.
