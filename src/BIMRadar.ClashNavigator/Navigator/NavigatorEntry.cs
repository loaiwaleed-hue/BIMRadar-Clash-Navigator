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
  public static string Open(UIApplication uiapp) {
    var uidoc = uiapp.ActiveUIDocument;
    if (uidoc == null) return "Open a Revit model first.";
    var doc = uidoc.Document;
    // Viewpoint Navigator: Navisworks viewpoints (XML + report folder) or Revizto issues (Export to Excel).
    // Writes Viewpoint_Navigator.html next to the data and opens the Previous / Next window.
    string pathIn = ("" ?? "").Trim().Trim('"');
    System.IO.Directory.CreateDirectory(NavData.AppDir);
    Func<string, string> readPref = n => { try { var p = System.IO.Path.Combine(NavData.AppDir, n); return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p).Trim() : ""; } catch (Exception) { return ""; } };
    Action<string, string> writePref = (n, v) => { try { System.IO.File.WriteAllText(System.IO.Path.Combine(NavData.AppDir, n), v ?? ""); } catch (Exception) { } };
    var owner = new Win32Owner(uiapp.MainWindowHandle);

    if (NavForm.Instance != null && !NavForm.Instance.IsDisposed) { try { NavForm.Instance.Close(); } catch (Exception) { } }

    string kind;
    if (pathIn != "") kind = pathIn.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? "revizto" : pathIn.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || pathIn.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ? "clash" : "viewpoints";
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
      if (cams.Count == 0) return "No clashes found in: " + pathIn + ". Export the report from Clash Detective as HTML (Tabular) with Clash Point and Item ID.";
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

    string htmlPath = "", htmlMsg, htmlErr = "";
    try { htmlPath = NavData.WriteNavigator(projectDir, htmlName, picDir, htmlTitle, htmlFolder, reportName, cams); htmlMsg = "Created " + htmlPath + "."; }
    catch (Exception ex) { htmlErr = ex.Message; htmlMsg = "Could not create " + htmlName + " in " + projectDir + ": " + ex.Message; }

    var h = new NavHandler();
    h.Cams = cams;
    h.PicDir = picDir;
    h.HtmlPath = htmlPath;
    h.HtmlError = htmlErr;
    h.Source = sourceName;
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
    return "Viewpoint Navigator (" + sourceName + ") opened with " + cams.Count + " items from " + fromFile + (reportName != null ? " (pictures from " + reportName + ")" : "")
      + (kind != "viewpoints" ? (defaultModel != "" ? ". Showing " + shown + " items of " + defaultModel : ". No model in the list matches " + doc.Title + ", showing all models") : "") + ". " + htmlMsg;
  }
}
}
