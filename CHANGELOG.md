# Changelog

## v1.0.0 (2026-11-26)
First public release of **BIMRadar Clash Viewer**: BIMRadar tab › Review panel (Clash Viewer, Clear Overrides). pyRevit extension and standalone add-in for Revit 2022–2027. Owner-tested in Revit 2022 (pyRevit) and Revit 2024 (standalone).

### Clash Viewer
- Navisworks HTML clash reports, Navisworks viewpoints and Revizto issues in one window. Next and Previous are in the window.
- Clash: section box around the clash elements (#74), Navisworks colours (item 1 red, item 2 green, linked elements included) (#80, #83), Isolate/Unisolate (#82), and a close perspective camera fitted to all clash elements, clear of the window (#84–#87).
- Clash report reader accepts any Navisworks report contents: Layer column (#89), no Summary (#91), and a clear message when essential columns are missing (#93, #94).
- Save snapshot for clashes: click once, set the Revit view as you want the solved picture, then click again to capture it exactly as on screen (#97, #99, #100, #102). Navisworks viewpoints and Revizto keep the automatic export.
- Revizto: shaded Fine views, why-listed line, Not anchored, empty-view check, Check locations, Hide empty, Go to element, and an other-model warning (#68–#73, #77, #79).
- Reopens the last source per project, with a ◂ Back button to change it (#92). WIP-Clash Viewer- duplicates (#78).
- Clear Overrides ribbon button (#96).

### Install
- pyRevit loader reads the DLL from bytes: no blocked-DLL error, no file lock (#88).
- Standalone `install.bat` unblocks its files (#95). It now hides only when pyRevit really loads the extension (#98).

### Foundation (from the v0.1.0 plan)
- #55: Three builds (net48, net8.0-windows, net10.0-windows) with CI, unit tests, zips and an unzipped `test-build` branch.
- #60: Review panel icons (Clash Navigator, Next, Previous), light and dark, 32 px and 16 px.
- #61: The AICon routine `viewpoint-navigator-window` is ported into the add-in, with a diff verification.
- #62: Settings folder `%AppData%\BIMRadar\ClashNavigator`, with a one-time copy of old AICon `vpnav` files.
- #63: Next and Previous commands that step the open navigator.
- #64: pyRevit extension that adds the Review panel (Clash Navigator, Next, Previous) to the BIMRadar tab.
- #65: Standalone add-in with `install.bat` / `uninstall.bat`; it hides itself when the pyRevit extension is registered.
- #66: Release packaging: draft GitHub Release from a `v*` tag with both zips, `docs/INSTALL.md`, README install section.
