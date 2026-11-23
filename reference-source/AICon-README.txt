Viewpoint Navigator Window (AICon routine)

INSTALL
1. Close Revit.
2. DELETE any old folder %APPDATA%\AICon\routines\viewpoint-navigator-window first.
3. Copy this folder into %APPDATA%\AICon\routines (keep the folder name).
4. %APPDATA%\AICon\routines.json must contain "allowCodeExecution": true
5. Start Revit. Built and tested with AICon 3.2.3 (Revit 2022). If it does not compile, update AICon.
The routine is ONE file (Routine.cs) and the web navigator page is built into it,
so it works from any routine folder (also a team folder).

USE
- Run "Viewpoint Window" (AICon first shows its own Run window), then choose:
  1 Navisworks clash test, 2 Navisworks viewpoints, 3 Revizto issues.
  Point at an option: it shows what to export and where to save it (a new empty folder per export,
  the web report and pictures are saved in that folder too).
- Search (Building / Volume code / Sequential number), e.g. D0, 33, 03306: matches a whole field of
  the ISO 19650 file name. Order: anchor model; anchored elsewhere but zone/room match ("Other anchor");
  no anchor -> Zone, then Room. "Not located" = no anchor, no zone code, no room (off until clicked).
  Results show as tags (click to hide/show) and in the Model list. * ? wildcards, ; = several codes.
- Filter: Name / Revizto status (or Clash status) / Stamp (several with ; = any of them).
- Action status (Open / Closed / Need clarification / Action by others) is your own status, with Action by (user, date).
  Revizto status / Clash status is the status from Revizto or Navisworks, shown separately.
  New Revizto issues start as Closed when Revizto says Solved / Closed, otherwise Open.
- Comment: Post (Enter), Shift+Enter = new line, Edit to change.
- Web report: the navigator page next to the data (Excel / clash report / viewpoints folder),
  with Export report and Export Excel.

HOW TO USE (also shown in the start window)
Navisworks clash test
 1. Create a new empty folder, e.g. ...\Clash 2026-11-23\
 2. Navisworks: Clash Detective > Report tab > Contents: tick Image, Clash Point, Status, Item ID
    (and Grid Location). Report Format: HTML (Tabular).
 3. Write Report > save it into the new folder. Only the HTML is needed (no XML).
 4. Run the tool, pick that .html.
Navisworks viewpoints
 1. Create a new empty folder, e.g. ...\VP 2026-11-23\
 2. Navisworks: Saved Viewpoints > select the viewpoints you need (delete the ones you don't need,
    then select all).
 3. Right-click the selection > Export Viewpoints Report... (.html + pictures) > save in the new folder.
 4. Right-click the selection AGAIN > Export Viewpoints... (.xml) > save in the same folder.
    Steps 3 and 4 are two separate menu entries, one below the other: run both.
 5. Run the tool, pick that folder.
Revizto issues
 1. Create a new empty folder, e.g. ...\Revizto 2026-11-23\
 2. Revizto: Issue Tracker > Export issues > Save as Excel (.xlsx) into the new folder. Excel only.
 3. Run the tool, pick that .xlsx.
The web report (navigator page) and pictures are saved in the same folder.
