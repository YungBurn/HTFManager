# HTFManager v0.3.6 Logo Integration Patch

This patch is intended to be applied **after** the `HTFManager-v0.3.6-restore-planning-foundation` patch has already been applied successfully.

## What this patch does

- Adds `src/HTFManager.App/Assets/AppIcon.png`
- Adds `src/HTFManager.App/Assets/AppIcon.ico`
- Sets the Windows executable icon via `ApplicationIcon`
- Registers app assets as Avalonia resources
- Sets the main window icon so it appears in the title bar and Windows taskbar
- Displays the same logo in the app sidebar header

## Apply as a Git patch

From the repository root:

```powershell
git apply .\HTFManager-v0.3.6-logo-integration.patch
```

Or, if you placed the patch elsewhere:

```powershell
git apply C:\path\to\HTFManager-v0.3.6-logo-integration.patch
```

## Manual file-copy fallback

If patching fails, copy the files under `files/` into the matching repository paths.

## Build & run

```powershell
dotnet restore HTFManager.slnx
dotnet build HTFManager.slnx --configuration Release --no-restore
dotnet run --project .\src\HTFManager.App\HTFManager.App.csproj
```

## Verification checklist

- The app opens normally
- The main window shows the logo in the title bar
- The Windows taskbar shows the custom app icon
- The left sidebar header shows the logo image
- `HTFManager.exe` shows the custom icon in Explorer (Release build)

## Notes

Windows sometimes caches icons aggressively. If the taskbar or Explorer still shows the old icon:

- close the app fully
- rebuild the app
- pin/unpin the taskbar shortcut again
- or clear the Windows icon cache / restart Explorer
