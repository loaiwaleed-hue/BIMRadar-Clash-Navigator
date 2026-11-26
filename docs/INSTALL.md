# Install BIMRadar Clash Viewer

**BIMRadar** is one Revit ribbon tab. Each tool adds its own panel. This repo adds the **Review** panel: one large **Clash Viewer** button. Next / Previous are in the Clash Viewer window.

## Revit versions

| Revit | Build used |
|---|---|
| 2022, 2023, 2024 | net48 |
| 2025, 2026 | net8.0-windows |
| 2027 | net10.0-windows |

The right build is picked for you by both install routes.

Pick **one** route below. Download the zips from the [latest Release](../../../releases/latest). Do **not** use GitHub's "Code > Download ZIP": that is source only and has no DLL.

## Route A: pyRevit extension

1. Download `BIMRadar-ClashNavigator-pyRevit.zip` from the latest Release.
2. Unzip it into a folder, for example a shared network folder such as `...\BIMRadar\`. After unzipping, that folder must contain `BIMRadar-ClashNavigator.extension`.
3. In Revit: pyRevit tab > Settings > Custom Extension Directories > add that folder (the one that contains the `.extension` folder, not the `.extension` folder itself). Save.
4. Click **Reload** (or restart Revit). The **BIMRadar** tab shows the **Review** panel.

## Route B: standalone add-in

1. Close Revit.
2. Download `BIMRadar-ClashNavigator-Standalone.zip` from the latest Release and unzip it anywhere.
3. Double-click `install.bat`. It installs per user (no admin needed) for each Revit year 2022 to 2027 that you have.
4. Start Revit. The **BIMRadar** tab shows the **Review** panel.

To remove it: close Revit and double-click `uninstall.bat`.

## Using both routes

If both are present, the add-in hides itself when the pyRevit extension is registered, so you never get two panels. To use the standalone add-in instead, remove the extension folder from pyRevit's Custom Extension Directories.

## Settings and migration from AICon

Settings and data are stored in `%AppData%\BIMRadar\ClashNavigator`.
On first run, old files from `%AppData%\AICon\vpnav` are copied there once. The old folder is left untouched.

## Updating

Close Revit first: the DLL is locked while Revit runs.
- pyRevit: replace the extension folder with the new one from the latest Release, then Reload.
- Standalone: unzip the new zip and run `install.bat` again.

## Reporting issues

Use [GitHub Issues](../../../issues). Say your Revit year and what you did.
This repo is public: **do not attach client or project data** (exports, screenshots of real models, project names, file paths). Describe the problem generically or use made-up sample data.

## Navisworks clash report settings (HTML)

Clash Detective → Report → **Report Format: HTML (Tabular)**. The contents the owner uses (tested 2026-11-24):

| Tick | Leave off (optional) |
|---|---|
| Summary, Clash Point, Date Found, Item Path, Item ID, Status, Description, Image, Clash Group, Grid Location | Assigned To, Date Approved, Approved By, Layer Name, Distance, Comments, Quick Properties, Simulation Dates/Event |

Required for Clash Viewer: **Clash Point, Item ID, Item Path** (and **Image** for the pictures). Since #89, extra columns such as *Layer Name* no longer break the model names, but the set above is the tested one.
