# T7 install.bat dry run

Scope: **logic only** (year detection, tfm choice, pyRevit warning, summary). The batch file itself, the add-in loading, the ribbon creation, the pyRevit E3 check in `App.cs` and the icons are NOT verified here: they are proven only on Windows / in Revit (#67). Nothing here verifies reads or writes of Revit data.

Method: `install.bat` rules ported 1:1 to Python (`scripts/dryrun_t7.py` in the session, not committed): for each year 2022..2027 whose `Addins\<year>` folder exists, tfm = net48, net8 for >= 2025, net10 for >= 2027; copy `.addin` + DLL (+pdb) into `<year>\BIMRadar.ClashNavigator\`; warn if pyRevit config mentions the extension; print summary. Fake folder trees were created in a temp directory (paths shown as `<fake>`).

```
=== no years ===
---- Summary ----
Nothing installed.

=== 2022+2025 ===
copy BIMRadar.ClashNavigator.addin -> <fake>/Autodesk/Revit/Addins/2022/
copy net48/BIMRadar.ClashNavigator.dll (+pdb) -> <fake>/Autodesk/Revit/Addins/2022/BIMRadar.ClashNavigator/
Installed for Revit 2022 (net48)
copy BIMRadar.ClashNavigator.addin -> <fake>/Autodesk/Revit/Addins/2025/
copy net8/BIMRadar.ClashNavigator.dll (+pdb) -> <fake>/Autodesk/Revit/Addins/2025/BIMRadar.ClashNavigator/
Installed for Revit 2025 (net8)
---- Summary ----
Installed for 2 Revit version(s): 2022 2025

=== all years ===
copy BIMRadar.ClashNavigator.addin -> <fake>/Autodesk/Revit/Addins/2022/
copy net48/BIMRadar.ClashNavigator.dll (+pdb) -> <fake>/Autodesk/Revit/Addins/2022/BIMRadar.ClashNavigator/
Installed for Revit 2022 (net48)
copy BIMRadar.ClashNavigator.addin -> <fake>/Autodesk/Revit/Addins/2023/
copy net48/BIMRadar.ClashNavigator.dll (+pdb) -> <fake>/Autodesk/Revit/Addins/2023/BIMRadar.ClashNavigator/
Installed for Revit 2023 (net48)
copy BIMRadar.ClashNavigator.addin -> <fake>/Autodesk/Revit/Addins/2024/
copy net48/BIMRadar.ClashNavigator.dll (+pdb) -> <fake>/Autodesk/Revit/Addins/2024/BIMRadar.ClashNavigator/
Installed for Revit 2024 (net48)
copy BIMRadar.ClashNavigator.addin -> <fake>/Autodesk/Revit/Addins/2025/
copy net8/BIMRadar.ClashNavigator.dll (+pdb) -> <fake>/Autodesk/Revit/Addins/2025/BIMRadar.ClashNavigator/
Installed for Revit 2025 (net8)
copy BIMRadar.ClashNavigator.addin -> <fake>/Autodesk/Revit/Addins/2026/
copy net8/BIMRadar.ClashNavigator.dll (+pdb) -> <fake>/Autodesk/Revit/Addins/2026/BIMRadar.ClashNavigator/
Installed for Revit 2026 (net8)
copy BIMRadar.ClashNavigator.addin -> <fake>/Autodesk/Revit/Addins/2027/
copy net10/BIMRadar.ClashNavigator.dll (+pdb) -> <fake>/Autodesk/Revit/Addins/2027/BIMRadar.ClashNavigator/
Installed for Revit 2027 (net10)
---- Summary ----
Installed for 6 Revit version(s): 2022 2023 2024 2025 2026 2027

=== pyRevit registered (2024) ===
copy BIMRadar.ClashNavigator.addin -> <fake>/Autodesk/Revit/Addins/2024/
copy net48/BIMRadar.ClashNavigator.dll (+pdb) -> <fake>/Autodesk/Revit/Addins/2024/BIMRadar.ClashNavigator/
Installed for Revit 2024 (net48)
WARNING: pyRevit_config.ini mentions BIMRadar-ClashNavigator.extension.
The add-in will stay hidden while the pyRevit extension is registered.
---- Summary ----
Installed for 1 Revit version(s): 2024

```

Notes
- `uninstall.bat` removes exactly `<year>\BIMRadar.ClashNavigator.addin` and the `<year>\BIMRadar.ClashNavigator\` folder for 2022..2027; not dry-run here.
- `AddStackedItems(RibbonItemData, RibbonItemData)` exists in Revit 2022-2027.
