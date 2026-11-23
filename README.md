# BIMRadar · Clash Navigator

Part of **BIMRadar**, one Revit ribbon tab for model QA/QC, review and audit tools.
This repo holds the **Clash Navigator**, which lives in the tab's **Review** panel.

Clash Navigator takes three kinds of coordination data into Revit and steps through them one item at a time:

| Source | What you export | What you get in Revit |
|---|---|---|
| Navisworks clash test | Clash Detective report, HTML (Tabular) | Each clash isolated to its two elements (host or linked), with a section box and the clash picture |
| Navisworks saved viewpoints | Viewpoints report (.html + pictures) **and** Viewpoints (.xml) | One perspective 3D view moved to each saved camera |
| Revizto issues | Issue Tracker > Export to Excel | Issue camera, zone / room / stamp filters, building-code search |

It also has:
- **Action status** for every item: Open, Closed, Need clarification or Action by others. It records who set it and when, and lets you add comments.
- A **web navigator / report** saved next to the data, with Export report and Export Excel.
- Status shared between Revit and the web page through `navigator_status.json`.

## Try the Hello build

The first build only proves loading: it adds **BIMRadar > Review > Hello**, which shows the Revit year and .NET runtime.

1. Open the latest run on the [Actions](../../actions) tab and download the two artifacts, `BIMRadar-ClashNavigator-pyRevit` and `BIMRadar-ClashNavigator-Standalone`.
2. Pick one route (never both):
   - **pyRevit:** unzip, add the unzipped folder in pyRevit Settings > Custom Extension Directories, then reload pyRevit.
   - **Standalone:** unzip, run `install.bat`, then restart Revit.

## Status

Being ported from the AICon routine `viewpoint-navigator-window` (kept in [`reference-source/`](reference-source/)).
Work is tracked in [Issues](../../issues). The design map is the `wayfinder:map` issue.
History issues (labelled `history`) record the 41 items found and solved during the AICon phase (19–23 Nov 2026).

Planned delivery (see the wayfinder decisions):
- a **pyRevit extension** that adds the Review panel to the **BIMRadar** tab, and
- a **standalone Revit add-in** (`.addin` + DLL) for people without pyRevit,
both built from the same C# code.

- Project rules: [CONTEXT.md](CONTEXT.md)
- Changes: [CHANGELOG.md](CHANGELOG.md)

License: [MIT](LICENSE)
