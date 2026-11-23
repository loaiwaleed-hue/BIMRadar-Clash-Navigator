# BIMRadar · Clash Viewer

Part of **BIMRadar**, one Revit ribbon tab for model QA/QC, review and audit tools.
This repo holds the **Clash Viewer**, which lives in the tab's **Review** panel.

Clash Viewer takes three kinds of coordination data into Revit and steps through them one item at a time:

| Source | What you export | What you get in Revit |
|---|---|---|
| Navisworks clash test | Clash Detective report, HTML (Tabular) | Each clash isolated to its two elements (host or linked), with a section box and the clash picture |
| Navisworks saved viewpoints | Viewpoints report (.html + pictures) **and** Viewpoints (.xml) | One perspective 3D view moved to each saved camera |
| Revizto issues | Issue Tracker > Export to Excel | Issue camera, zone / room / stamp filters, building-code search |

It also has:
- **Action status** for every item: Open, Closed, Need clarification or Action by others. It records who set it and when, and lets you add comments.
- A **web navigator / report** saved next to the data, with Export report and Export Excel.
- Status shared between Revit and the web page through `navigator_status.json`.

## Install

Download the zip from the latest [Release](../../releases) and follow [docs/INSTALL.md](docs/INSTALL.md).
There are two routes (pick one, never both): a **pyRevit extension** or a **standalone add-in** (`install.bat`).
Supports Revit 2022 to 2027.
Do not use "Code > Download ZIP": that is source only and has no DLL.

## Test builds

The `test-build` branch holds the latest `main` build, unzipped, for testers (`pyRevit/` and `Standalone/` folders, plus `BUILD.txt` with the commit).
These are not releases and may be unfinished. Each CI run also keeps both builds as artifacts on the [Actions](../../actions) tab.

## Status

The port from the AICon routine `viewpoint-navigator-window` (kept in [`reference-source/`](reference-source/)) is done and packaged.
Version 0.1.0 is waiting for the owner's test in Revit before it is released.
Work is tracked in [Issues](../../issues). The design map is the `wayfinder:map` issue.
History issues (labelled `history`) record the 41 items found and solved during the AICon phase (19–23 Nov 2026).

Delivery (see the wayfinder decisions):
- a **pyRevit extension** that adds the Review panel to the **BIMRadar** tab, and
- a **standalone Revit add-in** (`.addin` + DLL) for people without pyRevit,
both built from the same C# code.

- Project rules: [CONTEXT.md](CONTEXT.md)
- Install guide: [docs/INSTALL.md](docs/INSTALL.md)
- Changes: [CHANGELOG.md](CHANGELOG.md)

License: [MIT](LICENSE)
