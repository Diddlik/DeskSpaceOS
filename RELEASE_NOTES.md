## 0.6.3

Changing the language now takes effect right away.

### Fixed

**New language did not show up**
The language was saved, but it only applied after a full restart of the Settings
app. Clicking the tray icon while the window was still open just brought the old
window back, still in the old language. The Settings app now restarts itself when
you pick a new language.

## 0.6.2

The Settings app opens again from the tray icon.

### Fixed

**Settings app crashed on start**
Once a UI language was saved, the Settings app crashed right after launch, so
clicking the tray icon seemed to do nothing. It used a Windows language API that
only works in packaged apps. It now uses the Windows App SDK version, which also
works in the installed (unpackaged) app.

## 0.6.1

Space icons no longer get lost or pile up on top of each other.

### Fixed

**Icons lost from spaces or overlapping inside them**
Spaces tracked their icons by position in the desktop icon list, which Explorer
renumbers whenever a desktop item is added or removed. After that, spaces
pointed at the wrong icons and saved the wrong names. The service now keeps a
name snapshot and remaps every tracked icon whenever the desktop changes.

Also fixed in the same area:

* Spaces drawn on the desktop were created without a tab, so their icons were
  not restored after a restart.
* Editing a space in the Settings app dropped the icons of its inactive tabs.
* Closing a tab or deleting a space left icons hidden off-screen; they are now
  moved to the remaining tab or laid out where the space was.
* Icons moved within the same space now snap back into the grid.
* Icons with the same display name (e.g. `Report.docx` and `Report.pdf` with
  hidden extensions) no longer map to the same icon.
* A sorting rule applied to a renamed file no longer puts the icon into two spaces.
* Icons that are not on the desktop yet at login (Explorer still loading) are
  kept and claimed once they appear, instead of being dropped on the next save.
* A failed read of `spaces.json` during a write no longer removes all spaces.

Icons that already lost their space in an earlier version must be dragged back
in once.

## 0.6.0

Localization, correct version reporting, and update control.

### Added

**Multi-language Settings app (German, Russian, Ukrainian)**
All user-facing text in the Settings app is now localized. The app follows the
Windows display language by default and can be pinned to a specific language on
the Settings page (System default / English / Deutsch / Русский / Українська).

* String resources live in `DeskSpaceOS.SettingsApp/Strings/{en-US,de-DE,ru-RU,uk-UA}/Resources.resw`
  (253 keys per locale) and are resolved through `x:Uid` in XAML and the new
  `Loc` helper in code-behind — no user-facing literals remain in the app.
* `AppSettings.Language` stores the optional BCP-47 override; an empty value
  follows the OS. `ApplicationLanguages.PrimaryLanguageOverride` is applied
  before XAML initialization, so a language change takes effect on the next
  start (the Settings page says so via an info bar).
* Dynamically built UI — spaces cards, portal cards, sorting-rule editors, and
  all dialogs — is localized too, including automation names for screen readers.

**Manual update check in the navigation footer**
A localized **Check for updates** entry reports the current state: development
build, up to date, update available, or an error with its message. It is
read-only: downloading and applying stays with the background service.

**Update check on start, with an off switch**
The background service no longer polls every four hours. It performs exactly one
check per service start and applies a newer version automatically.

* New setting **Check for updates at startup** (`AppSettings.AutoUpdateCheck`,
  default on). When off, the service skips the check entirely; the manual check
  in the Settings app keeps working.
* New `Updates:StartupDelaySeconds` (default `120`) shortens the pre-check delay
  when testing the update flow against a local feed.

### Fixed

**About page showed 1.0.0.0 instead of the real version**
The page read the assembly version, which was never stamped. It now reports the
Velopack-installed version and falls back to the assembly informational version
(suffixed `-dev` in debug builds) for portable and development runs.
`build-installer.ps1` passes `-p:Version` into both published assemblies, so
release artifacts carry the tag version.

**Velopack locator was never initialized in the Settings app**
`UpdateManager` threw *"No VelopackLocator has been set"* during startup, which
crashed the Settings app with `0xC000027B` before its window appeared. Both the
version lookup and the update check now use an explicit
`VelopackLocator.CreateDefaultForPlatform`.

**Zen Mode preview images did not render**
Unpackaged WinUI resolves `ms-appx:///` through `StorageFile`, which requires
package identity the app does not have. The previews are loaded from the file
system next to the executable instead, and the asset items now carry
`CopyToPublishDirectory` so installed builds ship them.

**Deleting a folder portal never unwatched its directory**
`PortalSpaceControl.Deleted` was declared and subscribed but never raised
(warning `CS0067`). Removal is handled by the settings hot-reload path, which
already unwatches and detaches the control, so the dead event and its two dead
subscriptions were removed.

**Silent failures across the overlay**
Fifteen empty `catch` blocks swallowed exceptions without a trace. Failed shell
launches, clipboard operations, recycle-bin deletes, and space/portal load and
save operations now log a warning with the affected path; unreadable file
entries are listed with an unknown size and date instead of being dropped. The
update-URL and single-instance handlers catch only the exceptions they can
actually handle.

### Changed

* The update-available message no longer claims a background download is already
  running; it states that the update installs on the next start while the
  automatic check is enabled.
* `README.md` and `agent_instruction.md` document the language selection, the
  version reporting, and the startup-only update behavior.

### Notes for testers

Point `Updates:Url` in `appsettings.json` at a local folder or `file://` URI and
set `Updates:StartupDelaySeconds` to `0` to exercise the update path without
waiting or touching GitHub.
