# Spec: BIMRadar · Clash Navigator (port of the AICon routine)

Source: AICon routine `viewpoint-navigator-window` (`reference-source/Routine.cs`, 2026-11-23).
Decisions: wayfinder map #57 (#47–#54). Rules: [CONTEXT.md](../CONTEXT.md).

## Goal
Clash Navigator runs from the **BIMRadar** ribbon tab without AICon. It installs through pyRevit or as a standalone add-in, works in Revit 2022–2027, and behaves exactly as the AICon routine does today.

## User stories

### A. Ribbon
- **A1** As a BIM coordinator, I want a **BIMRadar** tab with a **Review** panel holding one large **Clash Viewer** button (the small Next / Previous ribbon buttons were dropped by the owner, 2026-11-24), so that I open and step through issues without AICon.
- **A2** As the owner, I want the Review panel to sit in the same BIMRadar tab as the panels from my other repos (Check, Fix…), so that all my tools live in one place.
- **A3** As a user, I want a blue line icon for each button (Review accent colour) and a short tooltip, so that the buttons are recognisable.
- **A4** ~~As a user, I want **Next / Previous** to move the open navigator. When no navigator is open, they say so in one line and do not error, so that I can map them to keyboard shortcuts safely.~~ **Dropped by the owner, 2026-11-24:** the window's own Next / Previous are enough.

### B. Behaviour kept from AICon
- **B1** As a user, I want clicking Clash Navigator to open the **source dialog first** (1 Navisworks clash test, 2 Navisworks viewpoints, 3 Revizto issues, with the "How to use" guide). AICon's "press Run" window is gone.
- **B2** As a user, I want the navigator window, search, filters, Action status, comments, snapshots, Duplicate 3D view, web report, Excel export and `navigator_status.json` sync to behave **exactly** as in the AICon routine, so that nothing I already solved comes back (history issues #1–#46).
- **B3** As a user, I want clicking Clash Navigator while the window is open to close the old window and open a fresh one, as the routine does now, so that I never get two windows.
- **B4** As a user, I want the clash isolation box size to stay at 3.0 m (the AICon default), so that views look the same.

### C. Settings
- **C1** As a user, I want my settings stored in `%AppData%\BIMRadar\ClashNavigator`, so that the tool no longer depends on AICon's folder.
- **C2** As an existing AICon user, I want my last source, folders and model searches copied once from `%AppData%\AICon\vpnav` on first run, without changing the AICon folder, so that I continue where I left off.
- **C3** As a user, I want web reports made by the AICon version to keep working with the new tool, because `navigator_status.json` and the HTML format are unchanged.

### D. Revit versions
- **D1** As a user on Revit **2022, 2023, 2024, 2025, 2026 or 2027**, I want the tool to load and work.
  - net48 build: 2022–2024
  - net8 build: 2025–2026
  - net10 build: 2027
- **D2** As the owner, I want every Revit API member checked per version (revitapidocs.com / rvtdocs.com) and version differences kept in one compat class, so that a new Revit year is a small change.

### E. Install
- **E1** As a pyRevit user, I want a `BIMRadar-ClashNavigator.extension` folder that I add once in pyRevit settings (or drop into the shared BIMRadar folder), so that the Review panel appears under BIMRadar.
- **E2** As a colleague without pyRevit, I want a zip with **install.bat / uninstall.bat** that installs the add-in for every Revit year found on my PC, so that I get the same BIMRadar tab and buttons.
- **E3** As a user who has both, I want the Review panel to appear only once.

### F. Build and release
- **F1** As the owner, I want GitHub to build the three DLLs and both zips on every push, so that I never need Visual Studio.
- **F2** As the owner, I want a GitHub Release with both zips **only when I ask for a tag**, after my test in Revit, so that colleagues only install verified versions.
- **F3** As the owner of a public repo, I want no client or project data in the repo: synthetic samples only.

### G. Verification
- **G1** As the owner, I want a "Hello BIMRadar" prototype first (one button, message box, both install routes, three builds), so that the tab merge and loading are proven on my Revit before the big port.
- **G2** As the owner, I want the ported tool tested by me in Revit 2022 and at least one of 2024/2025, on a model copy, before the first release. Each change that touches the model needs this, because writes (views, isolation, section box, snapshots) cannot be proven outside Revit.

## Out of scope (deferred)
- #23 Clash camera direction (needs a clash viewpoint source)
- #59 Shorter model tag names
- Rebranding `qaqc-checker` to the BIMRadar tab: a ticket in that repo
- Any new feature beyond the AICon routine
