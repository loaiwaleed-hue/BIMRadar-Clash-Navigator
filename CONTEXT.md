# BIMRadar Clash Navigator: project context

These are the standing rules for every ticket. Read them before implementing anything.

## Product family
- **BIMRadar** is ONE Revit ribbon tab. Every tool has its own repo, and each repo adds only its own panel or buttons to that tab.
- Planned panels:
  - **Health**: model health, warnings
  - **Check**: rule-based QA/QC, e.g. LOI
  - **Fix**
  - **Review**: Clash Navigator, warnings navigator
  - **Data**: parameters, COBie, asset register
  - **Audit**: ISO 19650 / BEP / client standards
- Final panel names and icons are decided in the wayfinder Grill tickets. Until then, use these as working names.
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

## Runtime (to be confirmed in wayfinder)
- C#, Revit API. AICon tests so far: Revit 2022 + AICon 3.2.3. The owner also uses Revit 2024 and 2025.
- Revit 2025+ runs .NET 8 and Revit ≤ 2024 runs .NET Framework 4.8. Version differences go through ONE compat place.
- The navigator window is modeless (WinForms in AICon). Every model change goes through `ExternalEvent`.
- Units: Revit internal feet (mm / 304.8).

## Process
- Tracker: GitHub Issues on this repo. Labels: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix, wayfinder:map, history.
- Code that cannot run here (anything that needs Revit) needs a mock or reflection-based verification write-up before its ticket closes. The write-up must say whether it verified reads or writes. Writes are proven only by the owner's Revit test on a model copy.
- Releases: tag + GitHub Release only after the owner verifies in Revit. Deploy from tags, never from main.
