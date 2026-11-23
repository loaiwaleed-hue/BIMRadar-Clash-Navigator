# BIMRadar Clash Navigator: project context

These are the standing rules for every ticket. Read them before implementing anything.

## Product family
- **BIMRadar** is ONE Revit ribbon tab. Every tool has its own repo, and each repo adds only its own panel or buttons to that tab.
- Panels, in this order (decided #47):
  - **Health**: model health, warnings
  - **Check**: rule-based QA/QC + data / parameter / COBie validation
  - **Fix**: ID / type fixes, later family rename and more
  - **Review**: Clash Navigator, warnings navigator
  - **Audit**: ISO 19650 / BEP / client standards
- Icons: line style, 32 px large / 16 px small, one accent colour per panel: Health red, Check green, Fix orange, Review blue, Audit dark grey.
- This repo adds to **Review**: a large **Clash Navigator** button + small **Next ▸** / **◂ Previous** buttons (#48).
- Each tool is public or private by the owner's choice, decided per tool. **This repo is PUBLIC** (MIT).
- Sister repo: `loaiwaleed-hue/qaqc-checker` (private: LOI Compliance Report, Select IDs, Fix Element IDs, Fix Type Names). It moves under the BIMRadar tab in a later ticket in that repo.

## Source of truth
- The latest AICon routine is the source of truth: `reference-source/Routine.cs` + `routine.json` (`viewpoint-navigator-window`, modified 2026-11-23).
- When AICon changes, the owner gives the newest copy and it replaces `reference-source/`. **Always port from the newest AICon version.**
- The old split files (Helpers.cs, Revizto.cs, ClashData.cs, separate template) in the owner's AICon folder are obsolete and ignored.
- Behaviour, wording and colours must match the AICon routine unless a ticket says otherwise. The solved history issues record why each behaviour exists.

## Public repo: data rules
- Never commit client or project data: Revizto/Navisworks exports, screenshots of real models, project names, file paths from the owner's PC, people's names from issue data.
- Use synthetic samples only, in `samples/`. Real test data stays on the owner's PC (`samples-private/` is git-ignored).
- History issues describe the problem generically, e.g. "a resort project", not the real project name.

## Runtime (decided #49, #50, #51)
- C#, Revit API, Revit **2022–2027**, three builds:
  - **net48**: Revit 2022–2024
  - **net8.0-windows**: Revit 2025–2026
  - **net10.0-windows**: Revit 2027
- **API compatibility:** check every Revit API member against each version on https://www.revitapidocs.com/ or https://rvtdocs.com/ (e.g. `/2027/`) before using it. Version differences go through ONE compat class.
  - ElementId is 64-bit since 2024: never `IntegerValue`, build ids with the existing `Eid(long)`.
  - Revit 2027 has add-in isolation (see #53).
- Two install routes, one build:
  - pyRevit extension: `BIMRadar.tab/Review.panel`
  - standalone zip with `install.bat` / `uninstall.bat` (per-year Addins folder)
  - never both panels at once
- Settings: `%AppData%\BIMRadar\ClashNavigator`. Old `%AppData%\AICon\vpnav` files are copied over once on first run. `navigator_status.json` next to the data is unchanged.
- The navigator window is modeless (WinForms). Every model change goes through `ExternalEvent` + named transactions, as in the routine.
- Units: Revit internal feet (mm / 304.8).

## Process
- Tracker: GitHub Issues on this repo. Labels: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix, wayfinder:map, history.
- Code that cannot run here (anything that needs Revit) needs a mock or reflection-based verification write-up before its ticket closes. The write-up must say whether it verified reads or writes. Writes are proven only by the owner's Revit test on a model copy.
- Releases: tag + GitHub Release only after the owner verifies in Revit. Deploy from tags, never from main.
