# HTF Manager v0.3.6 — Profile Restore Assistant

## Release status

**Verified development baseline.**

Local Windows validation completed with .NET 10:

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

Restore and Release build succeeded and the application entered the UI. Three existing Avalonia `AVLN3001` warnings remain for `LoaderSetupDialog`, `PackageInspectorDialog`, and `ProfileImportDialog`; they are non-blocking in the verified workflow and were not introduced by Profile Restore Assistant.

## Added

### Profile Restore planning

- `ProfileRestorePlan`, `ProfileRestoreItem`, and `ProfileRestoreDisposition`.
- `IProfileRestoreService` and `ProfileRestoreService`.
- Deterministic Thunderstore resolution using exact `PackageKey`.
- Requested-version preference with explicit `VersionFallback` classification.
- No fuzzy package-name substitution.

### Profile Restore Assistant UI

- `Restore missing mods` entry point on Profiles.
- Native Avalonia `ProfileRestoreDialog`.
- `Ready`, `VersionFallback`, `PackageUnavailable`, and `ManualRequired` presentation.
- Explicit acknowledgement before a fallback version can enter Package Inspector.
- Catalog-load failure is treated separately from package unavailability.
- Installation is blocked while the game is running.

### Existing installer integration

- Remote preparation can target an explicit `RemoteModVersion`.
- Every restoration still enters the existing Package Inspector before writes.
- Successful installs refresh local Mods, re-run `ResolveMissingMods`, and rebuild the restore plan.
- Completing restoration does not automatically apply the profile.

### Application identity

- Added the HTF Manager application logo as Avalonia/Windows assets.
- Configured the executable/window/taskbar icon.
- Added the logo to the application sidebar header.

## Preserved safety boundaries

- No second Mod installer.
- No silent fallback installation.
- No automatic profile apply.
- No fuzzy Thunderstore matching.
- No automatic handling for local/external requirements.
- No Nexus integration.
- Existing package inspection, dependency/conflict checks, staging, rollback, loader handling, and ownership rules remain authoritative.

## Known limitation carried into v0.3.7

Profile matching still treats a trusted identity match as resolved even when the installed version differs from the version recorded in the imported portable profile. v0.3.6 intentionally does not change that matching contract. The next milestone will preserve expected metadata for resolved members and report/reconcile version drift explicitly.
