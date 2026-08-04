# Agent Instructions — DeskSpaceOS (Single Point of Truth)

This file is the **single source of truth** for every AI agent working in this
repository (Claude, Codex, Gemini, Copilot, etc.). `AGENTS.md`, `CLAUDE.md`, and
`GEMINI.md` are thin anchors that point here — read this file first.

**Live file rule:** this document must always describe the *current* state of the
checkout. Whenever a change alters project layout, target frameworks, platforms,
dependencies, build/run/install commands, architecture boundaries, storage
formats, or feature status, update this file in the same change.

## Source Order

1. The user's current request is authoritative.
2. This file (`agent_instruction.md`) is the current repo guidance.
3. `DeskSpaceOS.SettingsApp/AGENTS.md` + `.github/instructions/*.instructions.md`
   are authoritative for **SettingsApp (WinUI 3)** work — read them before
   editing that project.
4. `OPEN_IMPLEMENTATION_POINTS.md` tracks recent feature status and local blockers.
5. `README.md` is user-facing product documentation, not agent guidance.
6. `CLAUDE.md` is useful background; verify against the workspace before relying on it.

Always verify framework/package versions against the relevant `.csproj` — never
hard-code versions from memory.

## What DeskSpaceOS Is

A Windows desktop organizer (codename "OpenSpace"). It creates visual "spaces" on
the desktop to group and manage icons, mirrors folders as on-desktop portals, and
adds features like sorting rules, hotkeys, roll-up, zen mode, peek, and quick-hide.
It injects a rendering layer between the desktop wallpaper and icons using
undocumented Win32 WorkerW window manipulation.

## Solution Shape

- Solution file: `DeskSpaceOS.slnx` (VS `.slnx` format).
- Platform: **x64 only** (declared in `.slnx` and each `.csproj`).
- Build output is centralized under `artifacts/` via `Directory.Build.props`
  (`UseArtifactsOutput=true`).

| Project | TFM | Kind / notes |
|---|---|---|
| `DeskSpaceOS.Core` | `net10.0` | Class library. No UI deps. `Nullable` + `ImplicitUsings` enabled. |
| `DeskSpaceOS.Service` | `net10.0-windows` | `WinExe` worker service. `UseWPF` + `UseWindowsForms`. References Core. Packages: `Microsoft.Extensions.Hosting` 10.0.0, `Velopack` 0.0.1298. Icon `AppIcon.ico`. |
| `DeskSpaceOS.SettingsApp` | `net10.0-windows10.0.26100.0` (min `10.0.17763.0`) | WinUI 3 / Windows App SDK `WinExe`. `RootNamespace = DeskSpaceOS_SettingsApp` (underscore). RIDs `win-x86;win-x64;win-arm64`. MSIX tooling enabled but `WindowsPackageType=None` (unpackaged run). References Core. Packages: `Microsoft.WindowsAppSDK` `*`, `Microsoft.Windows.SDK.BuildTools` `*`. |

Read the relevant `.csproj` for exact framework/package/platform values — they are
the source of truth, not this table.

## Build & Run

Always pass `-p:Platform=x64`.

```powershell
# Whole solution
dotnet build DeskSpaceOS.slnx -c Debug -p:Platform=x64

# Individual projects
dotnet build DeskSpaceOS.Core/DeskSpaceOS.Core.csproj -c Debug -p:Platform=x64
dotnet build DeskSpaceOS.Service/DeskSpaceOS.Service.csproj -c Debug -p:Platform=x64
dotnet build DeskSpaceOS.SettingsApp/DeskSpaceOS.SettingsApp.csproj -c Debug -p:Platform=x64

# Run
dotnet run --project DeskSpaceOS.Service/DeskSpaceOS.Service.csproj -c Debug -p:Platform=x64
dotnet run --project DeskSpaceOS.SettingsApp/DeskSpaceOS.SettingsApp.csproj -c Debug -p:Platform=x64
```

Root helpers: `run-service.bat`, `run-settings.bat`.
Installer: `build-installer.ps1` / `build-installer.cmd` (Velopack; publishes **both**
Service and SettingsApp self-contained into the pack dir, outputs under `artifacts/dist`).

### Releases (CI)
Pushing a semver tag `v*` (e.g. `git tag v0.5.1 && git push origin v0.5.1`) triggers
`.github/workflows/release.yml` on a Windows runner: it derives the version from the tag,
runs `build-installer.ps1`, and publishes a GitHub Release with the Velopack assets to
`Diddlik/DeskSpaceOS` (channel `stable`, vpk pinned to `0.0.1298`). Manual/local release:
`build-installer.ps1 -Version X.Y.Z -GitHubToken <tok>` (GitHub) or `-LocalReleasesPath <dir>`.

### Auto-update
`UpdateService` (Velopack `GithubSource`) polls every 4h and self-applies. The release URL
defaults in code to `https://github.com/Diddlik/DeskSpaceOS`; override with `Updates:Url`
in `appsettings.json` (shipped next to the exe) — use a local folder / `file://` URI to test
the update flow. `VelopackApp.Build().Run()` must stay first in `Program.cs`.

SettingsApp requires Windows **Developer Mode** enabled. If a run fails because an
old instance is live, `taskkill /IM <name>.exe /F` before re-running.

## Architecture

### DeskSpaceOS.Core — shared, no UI
- `Models/` — `Space`, `DesktopIcon`, `SpaceTab`, `PortalTab`, `FolderPortal`,
  `SortingRule`, `SortingRuleEvaluator`, `SettingsEnums` (e.g. `TabStyle`,
  `HeaderVisibility`, `QuickHideScope`).
- `Storage/` — `AppSettingsStore` (+ `AppSettings`), `SpaceStore`,
  `FolderPortalStore`, `SortingRuleStore`, `SettingsWatcher` (live-reload of
  settings/data on file change).
- `Win32/` — P/Invoke interop:
  - `User32` — window finding, messages, `SetParent`.
  - `Kernel32` — cross-process memory for ListView manipulation.
  - `DesktopManager` — WorkerW injection (undocumented `0x052C` message to
    Progman; enumerates for the WorkerW behind `SHELLDLL_DefView`, falls back to
    Progman).
  - `ListViewManager` — read/write desktop icon positions via `SysListView32`
    (`LVM_GETITEMPOSITION` / `LVM_SETITEMPOSITION`) using `VirtualAllocEx` into
    explorer.exe.
  - `MouseHook` — global low-level mouse hook with double-click detection.

### DeskSpaceOS.Service — runtime desktop behavior
- Host: `Microsoft.Extensions.Hosting` app builder (`Program.cs`) with hosted
  services `Worker` and `UpdateService`.
- `Worker` initializes the desktop hook (WorkerW injection), locates
  `SysListView32`, and starts the WPF overlay on a dedicated **STA thread** via
  `OverlayManager`.
- Overlay: `OverlayWindow.xaml` (parented to WorkerW), `SelectionWindow.xaml`
  (drag-to-create), controls under `Controls/` (`SpaceControl`,
  `PortalSpaceControl`, `CreateSpacePopup`).
- Other runtime pieces: `FolderPortalWatcher`, `DesktopFileMonitor`,
  `ShellIconExtractor`, hotkey registration, live settings reload.
- Updates: `UpdateService` + Velopack. `Program.cs` intercepts Velopack CLI verbs
  first, and registers/unregisters autostart in the HKCU `...\Run` key under value
  `DeskSpaceOS` (also honors legacy `--install` / `--uninstall`).

### DeskSpaceOS.SettingsApp — WinUI 3 configuration UI
- `NavigationView`-based (`MainWindow`), pages: Spaces, Folder Portals, Appearance,
  Sorting Rules, Hotkeys, Tabs, Roll-Up, Peek, Zen Mode, Quick Hide, Layout,
  Settings, About. `PlaceholderPage` exists but is no longer routed to.
- Namespace is `DeskSpaceOS_SettingsApp` (underscore) — preserve unless the
  `.csproj` `RootNamespace` changes.

## Data & Settings Storage

- User settings persist to `%AppData%\DeskSpaceOS\settings.json` (indented JSON via
  `AppSettingsStore`). Spaces, folder portals, and sorting rules use sibling stores
  in the same folder.
- `SettingsWatcher` gives the Service live reload when the SettingsApp writes changes.
- Do **not** introduce a second settings format without an explicit migration;
  respect the existing patterns in `DeskSpaceOS.Core/Storage/`.

## Implementation Guidance

- Keep Core free of UI dependencies — models, stores, watchers, sorting logic, and
  Win32 wrappers only.
- Service owns runtime desktop behavior; SettingsApp owns user-facing configuration.
- Before running exported-symbol changes, find all callsites (references) — a
  missed callsite is a bug.
- For SettingsApp XAML, navigation, user-facing strings, data binding, permissions,
  or WinUI APIs, open the matching `.github/instructions/*.instructions.md` first.
- For unknown WinUI/WinAppSDK/Windows API types, use
  `.github/instructions/windows-apis.instructions.md` and official Microsoft docs
  before inspecting `.winmd` metadata.
- Check `OPEN_IMPLEMENTATION_POINTS.md` before changing feature areas listed there,
  and update it when status changes.

## SettingsApp Instruction Index

Detailed WinUI 3 agent rules live in `DeskSpaceOS.SettingsApp/AGENTS.md` and
`.github/instructions/`:

| File | Scope |
|---|---|
| `design-principles.instructions.md` | DRY, KISS, SOLID, YAGNI |
| `globalization.instructions.md` | Globalization & localization (`.resw`, `x:Uid`) |
| `accessibility.instructions.md` | AutomationProperties, keyboard nav, contrast |
| `security.instructions.md` | Secrets, input validation, least privilege |
| `performance.instructions.md` | `x:Bind`, `x:Load`, virtualization, async |
| `code-quality.instructions.md` | Static analysis, StyleCop, cleanup |
| `winui-best-practices.instructions.md` | MVVM, WinUI patterns, API verification |
| `windows-apis.instructions.md` | WinAppSDK / Platform SDK API catalog |
| `testing.instructions.md` | Unit testing, build & run |

## Verification

Run the narrowest useful build first, then the solution build for cross-project
changes:

- Core-only change → build `DeskSpaceOS.Core`, then any dependent project touched.
- Service change → build `DeskSpaceOS.Service`.
- SettingsApp change → build `DeskSpaceOS.SettingsApp`.
- Cross-layer change → build `DeskSpaceOS.slnx`.

There are **no test projects** in the current workspace. If tests are added, record
the test command here.

## Git Note

`git status` / other git commands may be blocked by Git's dubious-ownership guard
for `F:/Coding/DescSpaceOS`. The fix is:

```powershell
git config --global --add safe.directory F:/Coding/DescSpaceOS
```

Do **not** run that global configuration without the user's approval; until then,
track progress from `OPEN_IMPLEMENTATION_POINTS.md` and build output.

## Living File Checklist

Re-check this file at the end of any meaningful change. Update it when:

- project names, folders, target frameworks, platforms, or package/runtime
  assumptions change;
- build, run, test, install, or release commands change;
- architecture boundaries between Core, Service, and SettingsApp change;
- storage formats or settings keys change;
- new required agent workflow appears in `.github/instructions/` or SettingsApp
  guidance;
- known blockers or verification expectations change.

Keep this file short, factual, and tied to the current checkout. No speculative plans.
