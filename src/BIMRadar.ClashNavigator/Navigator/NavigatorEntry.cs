using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace BIMRadar.ClashNavigator.Navigator
{
public static class NavigatorEntry {
  // #92: last source per document (key = doc.PathName, or doc.Title when unsaved). In memory for the session + last_by_doc.txt (key|kind|path, newest last, max 50).
  public class LastSource { public string Kind; public string Path; }
  static readonly Dictionary<string, LastSource> LastByDoc = new Dictionary<string, LastSource>(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> LastOrder = new List<string>();
  static bool lastLoaded;
  const string LastFile = "last_by_doc.txt";

  static string DocKey(Document doc) { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }

  static void LoadLast() {
    if (lastLoaded) return;
    lastLoaded = true;
    try {
      var p = System.IO.Path.Combine(NavData.AppDir, LastFile);
      if (!System.IO.File.Exists(p)) return;
      foreach (var line in System.IO.File.ReadAllLines(p)) {
        var parts = line.Split('|');
        if (parts.Length != 3 || parts[0] == "" || parts[2] == "") continue;
        if (!LastByDoc.ContainsKey(parts[0])) LastOrder.Add(parts[0]);
        LastByDoc[parts[0]] = new LastSource { Kind = parts[1], Path = parts[2] };
      }
    } catch (Exception) { }
  }

  static void Remember(string key, string kind, string path) {
    LoadLast();
    LastOrder.Remove(key);
    LastOrder.Add(key);
    LastByDoc[key] = new LastSource { Kind = kind, Path = path };
    while (LastOrder.Count > 50) { LastByDoc.Remove(LastOrder[0]); LastOrder.RemoveAt(0); }
    try {
      System.IO.Directory.CreateDirectory(NavData.AppDir);
      System.IO.File.WriteAllLines(System.IO.Path.Combine(NavData.AppDir, LastFile), LastOrder.Select(k => k + "|" + LastByDoc[k].Kind + "|" + LastByDoc[k].Path).ToArray());
    } catch (Exception) { }
  }

  static bool SourceExists(LastSource ls) {
    if (ls == null || string.IsNullOrEmpty(ls.Path)) return false;
    if (ls.Kind == "viewpoints") return System.IO.Directory.Exists(ls.Path) || System.IO.File.Exists(ls.Path);
    if (ls.Kind == "clash" || ls.Kind == "revizto") return System.IO.File.Exists(ls.Path);
    return false;
  }

  public static string Open(UIApplication uiapp) { return Open(uiapp, false); }

  // forceDialog (#92): always ask for the source (Change source button); otherwise reopen this document's remembered source when it still exists.
  public static string Open(UIApplication uiapp, bool forceDialog) {
    var uidoc = uiapp.ActiveUIDocument;
    if (uidoc == null) return "Open a Revit model first.";
    var doc = uidoc.Document;
    // Viewpoint Navigator: Navisworks viewpoints (XML + report folder) or Revizto issues (Export to Excel).
    // Writes Viewpoint_Navigator.html next to the data and opens the Previous / Next window.
    string pathIn = "";
    System.IO.Directory.CreateDirectory(NavData.AppDir);
    Func<string, string> readPref = n => { try { var p = System.IO.Path.Combine(NavData.AppDir, n); return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p).Trim() : ""; } catch (Exception) { return ""; } };
    Action<string, string> writePref = (n, v) => { try { System.IO.File.WriteAllText(System.IO.Path.Combine(NavData.AppDir, n), v ?? ""); } catch (Exception) { } };
    var owner = new Win32Owner(uiapp.MainWindowHandle);

    string docKey = DocKey(doc);
    LoadLast();
    string kind = null;
    LastSource remembered;
    if (!forceDialog && LastByDoc.TryGetValue(docKey, out remembered) && SourceExists(remembered)) { kind = remembered.Kind; pathIn = remembered.Path; }   // #92: skip the dialog
    else {
      using (var sd = new SourceDialog(readPref("last_source.txt"))) {
        if (sd.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK || sd.Choice == null) return "Cancelled.";
        kind = sd.Choice;
      }
      writePref("last_source.txt", kind);
      if (kind == "revizto") {
        using (var od = new System.Windows.Forms.OpenFileDialog()) {
          od.Title = "Choose the Revizto issues Excel (Export to Excel)";
          od.Filter = "Revizto Excel export (*.xlsx)|*.xlsx";
          string lastX = readPref("last_revizto.txt");
          if (lastX != "" && System.IO.File.Exists(lastX)) { od.InitialDirectory = System.IO.Path.GetDirectoryName(lastX); od.FileName = System.IO.Path.GetFileName(lastX); }
          if (od.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return "Cancelled: no Excel chosen.";
          pathIn = od.FileName;
        }
      } else if (kind == "clash") {
        using (var od = new System.Windows.Forms.OpenFileDialog()) {
          od.Title = "Choose the Navisworks clash report (Clash Detective > Report > HTML (Tabular))";
          od.Filter = "Clash report (*.html;*.htm)|*.html;*.htm";
          string lastC = readPref("last_clash.txt");
          if (lastC != "" && System.IO.File.Exists(lastC)) { od.InitialDirectory = System.IO.Path.GetDirectoryName(lastC); od.FileName = System.IO.Path.GetFileName(lastC); }
          if (od.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return "Cancelled: no clash report chosen.";
          pathIn = od.FileName;
        }
      } else {
        using (var dlg = new System.Windows.Forms.FolderBrowserDialog()) {
          dlg.Description = "Choose the project folder with the Navisworks viewpoints XML and the viewpoints report (HTML + pictures)";
          dlg.ShowNewFolderButton = false;
          string last = readPref("last_folder.txt");
          if (System.IO.Directory.Exists(last)) dlg.SelectedPath = last;
          if (dlg.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return "Cancelled: no project folder chosen.";
          pathIn = dlg.SelectedPath;
        }
      }
    }

    // the old window closes only now, so a cancelled dialog (Change source) keeps it open
    if (NavForm.Instance != null && !NavForm.Instance.IsDisposed) { try { NavForm.Instance.Close(); } catch (Exception) { } }
    return LoadSource(uiapp, doc, owner, kind, pathIn, docKey);
  }

  // #92: loads a chosen / remembered source (kind = clash | viewpoints | revizto) and opens the window
  static string LoadSource(UIApplication uiapp, Document doc, Win32Owner owner, string kind, string pathIn, string docKey) {
    Action<string, string> writePref = (n, v) => { try { System.IO.File.WriteAllText(System.IO.Path.Combine(NavData.AppDir, n), v ?? ""); } catch (Exception) { } };
    List<VpCam> cams;
    string projectDir, picDir, htmlTitle, htmlFolder, reportName = null, sourceName, defaultModel = "", fromFile, htmlName = "Viewpoint_Navigator.html";

    if (kind == "revizto") {
      if (!System.IO.File.Exists(pathIn)) return "Excel not found: " + pathIn;
      writePref("last_revizto.txt", pathIn);
      cams = ReviztoData.Load(pathIn, doc, out htmlTitle);
      if (cams.Count == 0) return "No Revizto issues with a 3D camera found in: " + pathIn;
      // navigator next to the Excel, snapshots in a sub-folder named after it
      projectDir = System.IO.Path.GetDirectoryName(pathIn);
      htmlName = System.IO.Path.GetFileNameWithoutExtension(pathIn) + "_Navigator.html";
      picDir = System.IO.Path.Combine(projectDir, System.IO.Path.GetFileNameWithoutExtension(pathIn));
      System.IO.Directory.CreateDirectory(picDir);
      htmlFolder = "Revizto issues"; sourceName = "Revizto issues"; fromFile = System.IO.Path.GetFileName(pathIn);
      var match = cams.Select(c => c.Model).FirstOrDefault(m => ReviztoData.ModelMatches(m, doc.Title));
      if (match != null) defaultModel = match;
      // first picture of the default list now, so the first step already has the right frame
      var first = cams.FirstOrDefault(c => defaultModel == "" || string.Equals(c.Model, defaultModel, StringComparison.OrdinalIgnoreCase));
      if (first != null) ReviztoData.Download(first, picDir);
    } else if (kind == "clash") {
      if (!System.IO.File.Exists(pathIn)) return "Clash report not found: " + pathIn;
      writePref("last_clash.txt", pathIn);
      bool hasStatus;
      cams = ClashData.Load(pathIn, doc, out htmlTitle, out picDir, out hasStatus);
      if (cams.Count == 0) {
        if (ClashData.Missing.Count > 0)   // #94
          return "This clash report cannot be read: it was written without " + string.Join(" and ", ClashData.Missing) +
                 ".\n\nIn Navisworks: Clash Detective > Report > Contents, tick " + string.Join(", ", ClashData.Missing) +
                 " (and Item Path, Image), keep Report Format = HTML (Tabular), then Write Report again.";
        return "No clashes found in: " + pathIn + ". Export the report from Clash Detective as HTML (Tabular) with Clash Point and Item ID.";
      }
      if (ClashData.Warnings.Count > 0) {   // #94: still usable, tell once what is reduced
        string w = "This clash report was written without " + string.Join(" and ", ClashData.Warnings) + ".\n\n" +
                   (ClashData.Warnings.Contains("Item Path") ? "- Without Item Path the model names are unknown: elements are searched by ID in the open model only (links cannot be told apart).\n" : "") +
                   (ClashData.Warnings.Contains("Image") ? "- Without Image there are no clash pictures.\n" : "") +
                   "\nTo fix: Clash Detective > Report > Contents, tick them, Write Report again. Continuing with what is available.";
        try { Autodesk.Revit.UI.TaskDialog.Show("BIMRadar \u00b7 Clash Viewer", w); } catch (Exception) { }
      }
      ClashData.ApplyNavCams(cams, pathIn, doc);   // #23
      projectDir = System.IO.Path.GetDirectoryName(pathIn);
      htmlName = System.IO.Path.GetFileNameWithoutExtension(pathIn) + "_Navigator.html";
      htmlFolder = "Navisworks clash test"; sourceName = "Navisworks clash test"; fromFile = System.IO.Path.GetFileName(pathIn);
      var cm = cams.SelectMany(c => c.Models).FirstOrDefault(m => ReviztoData.ModelMatches(m, doc.Title));
      if (cm != null) defaultModel = cm;
      if (!hasStatus) reportName = null;
    } else {
      string xmlPath;
      if (System.IO.Directory.Exists(pathIn)) {
        projectDir = pathIn;
        xmlPath = NavData.FindXml(pathIn);
        if (xmlPath == null) return "No Navisworks viewpoints XML found in: " + pathIn + ". Export it from Navisworks (Output > Export > Viewpoints) into this folder.";
      } else if (System.IO.File.Exists(pathIn)) {
        xmlPath = pathIn;
        projectDir = System.IO.Path.GetDirectoryName(pathIn);
      } else return "Folder or XML not found: " + pathIn;
      writePref("last_folder.txt", projectDir);
      cams = NavData.Load(xmlPath, doc, out htmlTitle, out htmlFolder);
      if (cams.Count == 0) return "No viewpoints found in: " + xmlPath;
      reportName = NavData.MapReport(projectDir, cams);
      picDir = NavData.FindPictureDir(projectDir, "", cams);
      sourceName = "Navisworks viewpoints"; fromFile = System.IO.Path.GetFileName(xmlPath);
    }

    Remember(docKey, kind, kind == "viewpoints" ? projectDir : pathIn);   // #92: loaded fine, remember it for this document

    string htmlPath = "", htmlMsg, htmlErr = "";
    try { htmlPath = NavData.WriteNavigator(projectDir, htmlName, picDir, htmlTitle, htmlFolder, reportName, cams); htmlMsg = "Created " + htmlPath + "."; }
    catch (Exception ex) { htmlErr = ex.Message; htmlMsg = "Could not create " + htmlName + " in " + projectDir + ": " + ex.Message; }

    var h = new NavHandler();
    h.Cams = cams;
    h.PicDir = picDir;
    h.HtmlPath = htmlPath;
    h.HtmlError = htmlErr;
    h.Source = sourceName;
    h.Isolated = sourceName.StartsWith("Navisworks clash");   // #82: clash opens isolated; the Isolate / Unisolate button toggles it
    h.BoxM = 3.0;
    var ev = Autodesk.Revit.UI.ExternalEvent.Create(h);
    var f2 = new NavForm(h, ev, defaultModel);
    h.Form = f2;
    NavForm.Instance = f2;
    f2.Show(owner);
    h.Action = "Refresh";
    ev.Raise();

    if (kind == "revizto") {
      // download the remaining Revizto snapshots in the background (default model first)
      var todo = cams.OrderBy(c => (defaultModel != "" && string.Equals(c.Model, defaultModel, StringComparison.OrdinalIgnoreCase)) ? 0 : 1).ToList();
      string dir = picDir;
      var th = new System.Threading.Thread(() => {
        int done = 0;
        foreach (var c in todo) {
          ReviztoData.Download(c, dir);
          done++;
          int idx = c.Index, d = done, tot = todo.Count;
          try { if (!f2.IsDisposed && f2.IsHandleCreated) f2.BeginInvoke(new Action(() => f2.PictureArrived(idx, d, tot))); } catch (Exception) { }
        }
      });
      th.IsBackground = true;
      th.Start();
    }

    int shown = defaultModel == "" ? cams.Count : cams.Count(c => string.Equals(c.Model, defaultModel, StringComparison.OrdinalIgnoreCase) || c.Models.Any(m => string.Equals(m, defaultModel, StringComparison.OrdinalIgnoreCase)));
    return "Clash Viewer (" + sourceName + ") opened with " + cams.Count + " items from " + fromFile + (reportName != null ? " (pictures from " + reportName + ")" : "")
      + (kind != "viewpoints" ? (defaultModel != "" ? ". Showing " + shown + " items of " + defaultModel : ". No model in the list matches " + doc.Title + ", showing all models") : "") + ". " + htmlMsg;
  }
}
}
