# Changelog

## Unreleased
- Review panel now has one button, Clash Viewer. The ribbon Next / Previous buttons were removed (owner decision 2026-11-24); Next / Previous stay in the window.

## Unreleased (branch `enhancements`, PR #75, owner-tested in Revit 2022 on 2026-11-24)
- Tool renamed **Clash Viewer** (user-facing); full-colour BIM icon family; one ribbon button (Next/Previous live in the window)
- Revizto: shaded/Fine views, annotations off except section box (#68, #77); no Isolate (#69, #93); why-listed line (#70); Not anchored (#71); empty-view check, Check locations, Hide empty (#72); location hints + Go to element (#73); other-model warning (#79)
- Clash: section box around the clash elements (#74); WIP-Clash Viewer- duplicates (#78); Navisworks colours item 1 red / item 2 green incl. linked (#80, #83); hit diagnostics (#81); Isolate/Unisolate (#82); close perspective camera fitted to all clash elements with the real field of view, clear of the window (#84–#87); innermost model + category safety check (#90); optional exact camera from a viewpoints XML (#23)
- Clash report reader accepts any Navisworks report contents: Layer column (#89), no Summary (#91), clear message for missing essentials (#93); pure parser with regression tests (#94)
- Reopen last source per project + Change source (#92)
- pyRevit loader loads from bytes: no blocked-DLL error, no file lock (#88); standalone install unblocks files (#95)

## v0.1.0 (unreleased, waiting for owner test #67)
- #55: Three builds (net48, net8.0-windows, net10.0-windows) with CI, unit tests, zips and an unzipped `test-build` branch.
- #60: Review panel icons (Clash Navigator, Next, Previous), light and dark, 32 px and 16 px.
- #61: The AICon routine `viewpoint-navigator-window` is ported into the add-in, with a diff verification.
- #62: Settings folder `%AppData%\BIMRadar\ClashNavigator`, with a one-time copy of old AICon `vpnav` files.
- #63: Next and Previous commands that step the open navigator.
- #64: pyRevit extension that adds the Review panel (Clash Navigator, Next, Previous) to the BIMRadar tab.
- #65: Standalone add-in with `install.bat` / `uninstall.bat`; it hides itself when the pyRevit extension is registered.
- #66: Release packaging: draft GitHub Release from a `v*` tag with both zips, `docs/INSTALL.md`, README install section.
