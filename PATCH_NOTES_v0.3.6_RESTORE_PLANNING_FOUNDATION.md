# v0.3.6 Profile Restore Planning Foundation Patch

## Historical status

This was the first implementation stage of v0.3.6. Its promotion condition has now been satisfied: the Restore Assistant UI/end-to-end flow was completed and locally verified. The final release summary is `PATCH_NOTES_v0.3.6.md`.

## Scope

This patch implements the first three v0.3.6 foundation items without adding the Restore Assistant UI:

1. Profile Restore Assistant behavior specification.
2. `ProfileRestorePlan` / `ProfileRestoreItem` / `ProfileRestoreDisposition` model.
3. Exact `PackageKey + requested version` Thunderstore restoration planning.

It also adds the minimum integration seam required by the future UI: `PrepareRemotePackageAsync(package, version)` reuses the existing Package Inspector preparation path with the version selected by the plan.

## Added

- `docs/V0.3.6_PROFILE_RESTORE_ASSISTANT.md`
- `src/HTFManager.Core/Interfaces/IProfileRestoreService.cs`
- `src/HTFManager.Core/Models/ProfileRestoreDisposition.cs`
- `src/HTFManager.Core/Models/ProfileRestoreItem.cs`
- `src/HTFManager.Core/Models/ProfileRestorePlan.cs`
- `src/HTFManager.Infrastructure/Profiles/ProfileRestoreService.cs`

## Changed

- `src/HTFManager.App/App.axaml.cs`
  - registers `ProfileRestoreService`.
- `src/HTFManager.App/Services/AppServices.cs`
  - exposes `IProfileRestoreService`;
  - keeps the existing latest-version prepare method;
  - adds explicit-version remote package preparation for future Restore Assistant use.

## Deliberately unchanged

- profile schema;
- `ProfileService` matching behavior;
- Profiles UI;
- Package Inspector UI;
- installer/dependency/loader logic;
- `VERSION`, `PROJECT_STATE.json`, README stable-baseline marker.

The stable baseline should not be promoted from v0.3.5.1 to v0.3.6 until the Restore Assistant UI and end-to-end restoration flow are completed and build-verified.
