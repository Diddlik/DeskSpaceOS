# Open Implementation Points

Status is updated as each item is implemented and verified.

## Checklist

- [x] Default appearance setting is applied to newly created spaces.
  - The Appearance page saved default color and alpha, but new spaces used hardcoded defaults.
  - Implemented in service-created spaces and Settings app-created spaces.
- [x] New Space hotkey is registered and creates a space.
  - The Settings app saved `NewSpaceHotkey`, but the service only registered Peek and QuickHide.
  - Implemented as a default-size space at the current cursor position.
- [x] Tabs settings page is implemented.
  - `TabsPage` is currently only a title stub.
  - `TabStyle` exists in settings storage, but the Settings app has no controls for it and the runtime does not apply it.
  - Implemented with a Settings app selector and runtime refresh for space and portal tab strips.
- [x] Roll-Up settings page is implemented.
  - `RollUpPage` is currently only a title stub.
  - `EnableRollUp` exists in settings storage, but runtime usage still needs review and wiring.
  - Implemented with a Settings app toggle and a space/portal context-menu command gated by that setting.
- [x] Peek settings page is implemented.
  - Peek runtime behavior exists, but `PeekPage` is currently only a title stub.
  - Implemented with a Settings app enable toggle, hotkey field, and service hotkey re-registration on settings reload.
- [x] Global mouse hook is guarded for non-desktop/game windows.
  - Fullscreen or borderless apps on another display could receive laggy mouse input because the low-level hook dispatched desktop overlay work before cheaply filtering the target window.
  - Implemented cached desktop-layer handle checks, early non-desktop exits for click/wheel/up events, early-throttled ambient mouse-move processing for ZenMode/QuickHide auto state, and a foreground fullscreen/borderless monitor guard that suppresses passive DeskSpace mouse reactions while a game covers the pointer's display.

- [ ] Space icons no longer get lost or overlap (implemented 2026-09-29, needs live verification).
  - Causes fixed: stale ListView indices after desktop add/delete, new spaces created without a tab (icons not restored after restart), hot-reload wiping inactive tabs' icons, closing/deleting tabs and spaces leaving icons parked off-screen, same-space drops not re-snapped, duplicate display names, sorting rules giving one icon two owners, partial desktop at login dropping names on save, failed `spaces.json` read treated as "all spaces deleted".
  - Verify: delete/add desktop files next to spaces, rename a file inside a space, close a tab, delete a space with several tabs, drag within a space, restart the service.
  - Not addressed: display scaling other than 100% (grid uses DIPs as ListView pixels), fixed 80x100 icon cell vs. large desktop icons, icon hit-test when a monitor sits left of/above the primary, SettingsApp saving a stale `spaces.json` copy.

## Notes

- `PlaceholderPage` still exists, but current navigation no longer routes to it.
- `git status` is blocked by Git's dubious ownership guard for `F:/Coding/DescSpaceOS`; track progress from this file and build output until `safe.directory` is configured.
