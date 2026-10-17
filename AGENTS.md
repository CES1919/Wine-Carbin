# AGENTS.md

## Project overview

Wine Cabinet is a lightweight Windows desktop organizer built with Windows Forms on `net10.0-windows`. Keep the application small and avoid third-party packages unless a task explicitly requires one.

## Repository layout

- `src/WineCabinet/Core`: persistence and application rules; this layer never constructs windows.
- `src/WineCabinet/UI`: launcher window, flyout panel, animation, menus, and input handling.
- `src/WineCabinet/Platform`: Windows Shell, registry, startup, and other native integration.
- `assets/source`: artwork embedded directly into the application assembly.
- `tests/WineCabinet.Tests`: executable regression tests without external test packages.
- `docs`: architecture documentation.
- `scripts` and `src/WineCabinet/Properties/PublishProfiles`: reproducible self-contained publishing.
- `Start Wine Cabinet.bat`: SDK bootstrap, architecture selection, publish, and launch entry point.

## Build and test

Run commands from the repository root:

```powershell
dotnet restore WineCabinet.sln
dotnet build WineCabinet.sln -c Release --no-restore
dotnet run --project tests/WineCabinet.Tests/WineCabinet.Tests.csproj -c Release --no-build
.\scripts\publish.ps1 -RuntimeIdentifier win-x64
```

The project builds only on Windows because it uses WinForms and Windows Shell APIs.

## Conventions

- User data lives in `%LOCALAPPDATA%\WineCabinet`, resolved by `Core/AppStorage.cs`. Keep that location stable so stored items keep resolving.
- The product name and executable identity are `Wine Cabinet`. Keep user-facing text in its current language unless a task asks for localization.
- Keep platform-specific operations in `Platform` and reusable behavior in `Core`.
- Behavior the application provides: single instance, current-user startup registration, desktop drag-and-drop, 3x3 and 4x4 paging, slot swapping, restoring items to the desktop, context menus, a system tray icon (left click opens the panel, right click opens the application menu), remembered launcher position with off-screen fallback, and startup failure reporting with optional visual styles.
- Stored-item menus use the actual Windows Shell IContextMenu handler, including namespace/system items; retain IContextMenu2/3 message forwarding and isolate organizer actions in the Wine Cabinet submenu. Synchronize native rename/delete/move results with library persistence. Shell tests may query system menus but must never empty the user's Recycle Bin or delete system items.
- Layout mapping: the 3x3 grid exposes 8 item slots with the application tile at cell 4; the 4x4 grid exposes 15 item slots with the application tile at cell 5, which is row 1, column 1. The 4x4 item slots are `0, 1, 2, 4, 6, 8, 9, 10` followed by `3, 7, 11, 12, 13, 14, 15`. Every 4x4 cell is rendered, including unused cells, which use the bordered empty-tile appearance.
- Add or update regression coverage when changing ordering, drag navigation, launch counting, persistence, or the embedded artwork pipeline.
- Use nullable reference types and follow the root `.editorconfig`.
- Startup registration uses the current-user `Run` key with `--startup`. Logon startup must not access this key; manual launch reads first and only requests write access if the command differs. Prefer the installed executable when present, otherwise the current executable. Do not introduce a service or an administrator requirement.
- Install source builds to a stable per-user directory before launch and verify the startup command points to the installed executable. Cross-check it through the Windows registry provider; fail on a mismatch with the process-local view and report when independent verification is unavailable. Do not reset user-disabled startup state.

## Repository hygiene

- Never commit `bin`, `obj`, `artifacts`, `TestResults`, `.dotnet-home`, `.nuget`, IDE state, user data, logs, or locally generated executables.
- Keep releases self-contained and single-file for both `win-x64` and `win-arm64`; application-owned runtime assets are embedded.
- Keep launcher artwork reproducible: embed the repository source file directly, preserve animated GIF frames, and keep the application resource name `WineCabinet.Assets.launcher`.
- Both launcher drawing paths use `ArtworkRenderer` contain scaling so the full image stays visible without cropping or aspect-ratio distortion.
- Do not add third-party image packages merely to resize launcher artwork; supported images are decoded at runtime with Windows/.NET `System.Drawing`.
- Keep documentation machine-independent: no local file system paths, local folder names, build output, test logs, screenshots, or claims about a particular local build.

## Documentation

- `README.md` covers what the application does, how to build and run it, user-visible behavior, data storage, and contribution basics.
- `docs/architecture.md` covers components, layout rules, persistence, drag-and-drop, the artwork pipeline, build and release, and failure handling.
- Describe the project as it is. Do not record development-stage notes, change history, or pending decisions, and keep every path either repository-relative or environment-variable based.
