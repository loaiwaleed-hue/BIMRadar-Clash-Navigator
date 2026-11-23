// Viewpoint Navigator Window - one file (works with every AICon version that runs script routines).
// Navisworks viewpoints (XML + report folder), Revizto issues (Export to Excel) or Navisworks clash test (HTML report).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AICon.Routines;
using WF = System.Windows.Forms;
using SD = System.Drawing;

public class ViewpointNavigatorRoutine : IAiconRoutine {
  public object Run(UIApplication uiapp, UIDocument uidoc, Document doc, RoutineInputs input) {
    // Viewpoint Navigator: Navisworks viewpoints (XML + report folder) or Revizto issues (Export to Excel).
    // Writes Viewpoint_Navigator.html next to the data and opens the Previous / Next window.
    string pathIn = (input.Text("xmlPath", "") ?? "").Trim().Trim('"');
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
      picDir = NavData.FindPictureDir(projectDir, input.Text("pictureFolder", ""), cams);
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
    h.BoxM = input.Number("boxSizeM", 3.0);
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

public class Win32Owner : WF.IWin32Window {
  private readonly IntPtr _h;
  public Win32Owner(IntPtr h) { _h = h; }
  public IntPtr Handle { get { return _h; } }
}

public class VpCam {
  public string Name, Guid, Img;          // Img = Navisworks picture file name (vp0001.jpg or from the report)
  public int Index;                        // position of the <view> in the XML (1-based)
  public XYZ Eye, Fwd, Up, Target;
  public double FocalM;
  public double FovH = 0, Aspect = 1;      // Navisworks vertical field of view (radians) and width/height
  public double[] RawPos, RawQ;            // as in the XML (shared metres / quaternion) for the web navigator
  public bool HasPlane;
  public XYZ PlanePt, PlaneN;
  // used by other sources (Revizto)
  public string Id, Model = "", Url, Note = "", St0;
  public string Zone = "", Room = "", Stamp = "";   // Revizto: zone / room (building code when there is no anchor model), stamp abbreviation
  public double[] RawDir;                  // look direction in shared coordinates (instead of RawQ)
  public bool HasPoint;                    // issue point -> section box around it
  public XYZ Point;
  public List<int> Select = new List<int>(); // Revit element ids to select
  // Navisworks clash test
  public bool IsClash, DefaultTest;
  public string Test = "", Status = "";
  public List<string> Models = new List<string>();                                 // models involved (Revizto: none, Model used)
  public List<KeyValuePair<string, int>> Items = new List<KeyValuePair<string, int>>(); // (model file, Element ID)
}

public static class NavData {
  const double FT = 0.3048;
  static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
  public static string AppDir { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AICon", "vpnav"); } }

  static XYZ Rot(double[] q, XYZ v) {
    double x = q[0], y = q[1], z = q[2], w = q[3];
    double tx = 2 * (y * v.Z - z * v.Y), ty = 2 * (z * v.X - x * v.Z), tz = 2 * (x * v.Y - y * v.X);
    return new XYZ(v.X + w * tx + (y * tz - z * ty), v.Y + w * ty + (z * tx - x * tz), v.Z + w * tz + (x * ty - y * tx));
  }
  static double D(XmlElement e, string a, double def) {
    double r; return (e != null && double.TryParse(e.GetAttribute(a), NumberStyles.Float, Inv, out r)) ? r : def;
  }

  public static Transform SharedToInternal(Document doc) {
    var pp = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
    var T = doc.ActiveProjectLocation.GetTotalTransform();
    var s0 = T.OfPoint(XYZ.Zero);
    bool intToShared = Math.Abs(s0.X - pp.EastWest) < 3.0 && Math.Abs(s0.Y - pp.NorthSouth) < 3.0;
    return intToShared ? T.Inverse : T;
  }

  public static List<VpCam> Load(string xmlPath, Document doc, out string nwdName, out string folderName) {
    Transform toInt = SharedToInternal(doc);

    var xml = new XmlDocument();
    xml.Load(xmlPath);
    nwdName = xml.DocumentElement.GetAttribute("filename");
    if (string.IsNullOrEmpty(nwdName)) nwdName = System.IO.Path.GetFileName(xmlPath);
    var vf = xml.GetElementsByTagName("viewfolder");
    folderName = vf.Count > 0 ? ((XmlElement)vf[0]).GetAttribute("name") : "";

    var list = new List<VpCam>();
    int idx = 0;
    foreach (XmlElement v in xml.GetElementsByTagName("view")) {
      idx++;
      var vp = (XmlElement)v.SelectSingleNode("viewpoint");
      if (vp == null) continue;
      var cam = (XmlElement)vp.SelectSingleNode("camera");
      var p = (XmlElement)vp.SelectSingleNode("camera/position/pos3f");
      var q = (XmlElement)vp.SelectSingleNode("camera/rotation/quaternion");
      if (p == null || q == null) continue;
      var c = new VpCam();
      c.RawPos = new double[] { D(p, "x", 0), D(p, "y", 0), D(p, "z", 0) };
      c.RawQ = new double[] { D(q, "a", 0), D(q, "b", 0), D(q, "c", 0), D(q, "d", 1) };
      var shared = new XYZ(c.RawPos[0] / FT, c.RawPos[1] / FT, c.RawPos[2] / FT);
      c.Name = v.GetAttribute("name");
      c.Guid = v.GetAttribute("guid");
      c.Index = idx;
      c.Img = "vp" + idx.ToString("0000") + ".jpg";
      c.Eye = toInt.OfPoint(shared);
      c.Fwd = toInt.OfVector(Rot(c.RawQ, new XYZ(0, 0, -1))).Normalize();
      c.Up = toInt.OfVector(Rot(c.RawQ, new XYZ(0, 1, 0))).Normalize();
      c.FocalM = D(vp, "focal", 1);
      c.Target = c.Eye + c.Fwd.Multiply(c.FocalM / FT);
      if (cam != null && cam.GetAttribute("projection") != "orthographic") { c.FovH = D(cam, "height", 0); c.Aspect = D(cam, "aspect", 1); }

      // first enabled Navisworks clip plane: n . p = d (shared coordinates, metres); points on the side n.p >= d are kept
      foreach (XmlElement cp in v.SelectNodes("clipplaneset/clipplanes/clipplane")) {
        if (cp.GetAttribute("state") != "enabled") continue;
        var pl = (XmlElement)cp.SelectSingleNode("plane");
        var nv = pl == null ? null : (XmlElement)pl.SelectSingleNode("vec3f");
        if (pl == null || nv == null) continue;
        double d = D(pl, "distance", 0);
        double nx = D(nv, "x", 0), ny = D(nv, "y", 0), nz = D(nv, "z", 0);
        c.PlanePt = toInt.OfPoint(new XYZ(nx * d, ny * d, nz * d).Multiply(1.0 / FT));
        c.PlaneN = toInt.OfVector(new XYZ(nx, ny, nz)).Normalize();
        c.HasPlane = true;
        break;
      }
      list.Add(c);
    }
    return list;
  }

  // newest viewpoints XML in the folder (top level first, then sub-folders)
  public static string FindXml(string folder) {
    var top = new System.IO.DirectoryInfo(folder).GetFiles("*.xml").OrderByDescending(f => f.LastWriteTime).FirstOrDefault(f => IsViewpointXml(f.FullName));
    if (top != null) return top.FullName;
    try {
      var sub = new System.IO.DirectoryInfo(folder).GetFiles("*.xml", System.IO.SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTime).FirstOrDefault(f => IsViewpointXml(f.FullName));
      if (sub != null) return sub.FullName;
    } catch (Exception) { }
    return null;
  }
  static bool IsViewpointXml(string path) {
    try { using (var r = new System.IO.StreamReader(path)) { var buf = new char[20000]; int n = r.Read(buf, 0, buf.Length); var s = new string(buf, 0, n); return s.Contains("<view ") || s.Contains("<viewpoint"); } }
    catch (Exception) { return false; }
  }

  // Folder that holds the Navisworks pictures (searched under the project folder); falls back to the project folder.
  public static string FindPictureDir(string root, string explicitDir, List<VpCam> cams) {
    if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir.Trim().Trim('"');
    string first = cams.Count > 0 ? cams[0].Img : "vp0001.jpg";
    try {
      if (System.IO.File.Exists(System.IO.Path.Combine(root, first))) return root;
      var hit = System.IO.Directory.EnumerateFiles(root, first, System.IO.SearchOption.AllDirectories).FirstOrDefault();
      if (hit != null) return System.IO.Path.GetDirectoryName(hit);
    } catch (Exception) { }
    return root;
  }

  // Navisworks viewpoints report: picture order = viewpoint order. Use its picture names when the count matches.
  public static string MapReport(string root, List<VpCam> cams) {
    try {
      var rx = new System.Text.RegularExpressions.Regex("<img[^>]+src\\s*=\\s*[\"']([^\"']+)[\"']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
      string bestName = null; List<string> best = null;
      foreach (var f in System.IO.Directory.EnumerateFiles(root, "*.htm*", System.IO.SearchOption.AllDirectories)) {
        var fn = System.IO.Path.GetFileName(f);
        if (fn.StartsWith("Viewpoint_Navigator", StringComparison.OrdinalIgnoreCase) || fn.StartsWith("Viewpoint_Report", StringComparison.OrdinalIgnoreCase) || fn.EndsWith("_Navigator.html", StringComparison.OrdinalIgnoreCase)) continue;
        var imgs = rx.Matches(System.IO.File.ReadAllText(f)).Cast<System.Text.RegularExpressions.Match>()
          .Select(m => Uri.UnescapeDataString(m.Groups[1].Value.Split('/', '\\').Last()))
          .Where(s => s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
        if (imgs.Count > 0 && (best == null || Math.Abs(imgs.Count - cams.Count) < Math.Abs(best.Count - cams.Count))) { best = imgs; bestName = fn; }
      }
      if (best != null && best.Count == cams.Count) { for (int i = 0; i < cams.Count; i++) cams[i].Img = best[i]; return bestName; }
    } catch (Exception) { }
    return null;
  }

  public static string BeforePath(string dir, VpCam c) { return System.IO.Path.Combine(dir ?? "", c.Img); }
  public static string AfterPath(string dir, VpCam c) { return System.IO.Path.Combine(dir ?? "", System.IO.Path.GetFileNameWithoutExtension(c.Img) + "_after.jpg"); }

  static string J(string s) {
    var sb = new StringBuilder("\"");
    foreach (char ch in s ?? "") {
      if (ch == '"' || ch == '\\') { sb.Append('\\').Append(ch); }
      else if (ch < 32 || ch == '<' || ch == '>' || ch == '&' || ch == '\u2028' || ch == '\u2029') sb.Append("\\u").Append(((int)ch).ToString("x4"));
      else sb.Append(ch);
    }
    return sb.Append('"').ToString();
  }
  static string N(double d) { return d.ToString("R", Inv); }

  // The web navigator template is built into the routine (gzip + base64), so it works wherever the routine folder is installed.
  const string TplB64 = "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";
  static string Template() {
    var bytes = Convert.FromBase64String(TplB64);
    using (var ms = new System.IO.MemoryStream(bytes))
    using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
    using (var rd = new System.IO.StreamReader(gz, Encoding.UTF8)) return rd.ReadToEnd();
  }

  // Writes <htmlDir>\<htmlName> from the template, with this project's items embedded.
  public static string WriteNavigator(string projectDir, string htmlName, string picDir, string nwdName, string folderName, string reportName, List<VpCam> cams) {
    string tplText = Template();
    string prefix = "";
    var pd = System.IO.Path.GetFullPath(picDir).TrimEnd('\\'); var rd = System.IO.Path.GetFullPath(projectDir).TrimEnd('\\');
    if (pd.StartsWith(rd + "\\", StringComparison.OrdinalIgnoreCase)) prefix = pd.Substring(rd.Length + 1).Replace('\\', '/') + "/";
    var sb = new StringBuilder();
    string baseUrl = new Uri(pd + "\\").AbsoluteUri; // absolute picture folder, so pictures show wherever the page or report is saved
    sb.Append("{\"file\":").Append(J(nwdName)).Append(",\"folder\":").Append(J(folderName)).Append(",\"prefix\":").Append(J(prefix)).Append(",\"base\":").Append(J(baseUrl));
    if (reportName != null) sb.Append(",\"report\":").Append(J(reportName));
    sb.Append(",\"views\":[");
    for (int i = 0; i < cams.Count; i++) {
      var c = cams[i];
      if (i > 0) sb.Append(',');
      sb.Append("{\"n\":").Append(c.Index).Append(",\"name\":").Append(J(c.Name)).Append(",\"guid\":").Append(J(string.IsNullOrEmpty(c.Guid) ? "n" + c.Index : c.Guid))
        .Append(",\"pos\":[").Append(string.Join(",", c.RawPos.Select(N))).Append(']');
      if (c.RawQ != null) sb.Append(",\"q\":[").Append(string.Join(",", c.RawQ.Select(N))).Append(']');
      if (c.RawDir != null) sb.Append(",\"dir\":[").Append(string.Join(",", c.RawDir.Select(N))).Append(']');
      if (!string.IsNullOrEmpty(c.St0)) sb.Append(",\"st0\":").Append(J(c.St0));
      if (c.Models.Count > 0) sb.Append(",\"models\":[").Append(string.Join(",", c.Models.Select(J))).Append(']');
      else if (!string.IsNullOrEmpty(c.Model)) sb.Append(",\"model\":").Append(J(c.Model));
      if (c.IsClash) sb.Append(",\"test\":").Append(J(c.Test)).Append(c.DefaultTest ? ",\"dflt\":1" : "").Append(string.IsNullOrEmpty(c.Status) ? "" : ",\"cst\":" + J(c.Status));
      if (!c.IsClash && !string.IsNullOrEmpty(c.Status)) sb.Append(",\"cst\":").Append(J(c.Status));
      if (c.Zone != "") sb.Append(",\"zone\":").Append(J(c.Zone));
      if (c.Room != "") sb.Append(",\"room\":").Append(J(c.Room));
      if (c.Stamp != "") sb.Append(",\"stamp\":").Append(J(c.Stamp));
      if (!string.IsNullOrEmpty(c.Note)) sb.Append(",\"info\":").Append(J(c.Note));
      sb.Append(",\"focal\":").Append(N(c.FocalM)).Append(",\"img\":").Append(J(c.Img)).Append('}');
    }
    sb.Append("],\"state\":__VPNAV_STATE__}");
    string dest = System.IO.Path.Combine(projectDir, htmlName);
    LoadStates(picDir);
    lock (StLock) { HtmlCache = tplText.Replace("__VPNAV_DATA__", sb.ToString()); HtmlDest = dest; }
    WriteHtml();
    return dest;
  }

  // ---------- Done / comment, shared with the web navigator through <picture folder>\navigator_status.json ----------
  public class St { public string st = "open", c = "", by = ""; public long t; }
  public static Dictionary<string, St> States = new Dictionary<string, St>();
  static string StatusFile, HtmlCache, HtmlDest;
  static readonly object StLock = new object();
  public static long Now() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

  static Dictionary<string, St> ReadStatusFile() {
    var r = new Dictionary<string, St>();
    try {
      if (StatusFile == null || !System.IO.File.Exists(StatusFile)) return r;
      var root = MiniJson.Parse(System.IO.File.ReadAllText(StatusFile, Encoding.UTF8)) as Dictionary<string, object>;
      object items; if (root == null || !root.TryGetValue("items", out items)) return r;
      foreach (var kv in (Dictionary<string, object>)items) {
        var o = kv.Value as Dictionary<string, object>; if (o == null) continue;
        Func<string, string> S = k => { object v; return o.TryGetValue(k, out v) && v != null ? v.ToString() : ""; };
        object tv; long t = o.TryGetValue("t", out tv) && tv is double ? (long)(double)tv : 0;
        r[kv.Key] = new St { st = S("st") == "" ? "open" : S("st"), c = S("c"), by = S("by"), t = t };
      }
    } catch (Exception) { }
    return r;
  }

  public static void LoadStates(string picDir) {
    lock (StLock) { StatusFile = System.IO.Path.Combine(picDir ?? "", "navigator_status.json"); States = ReadStatusFile(); }
  }

  // newer marks from the web navigator; returns true when something changed
  public static bool ReloadStates() {
    lock (StLock) {
      bool ch = false;
      foreach (var kv in ReadStatusFile()) { St cur; if (!States.TryGetValue(kv.Key, out cur) || kv.Value.t > cur.t) { States[kv.Key] = kv.Value; ch = true; } }
      return ch;
    }
  }

  public static St GetState(VpCam c) { lock (StLock) { St s; return States.TryGetValue(c.Guid ?? "", out s) ? s : null; } }
  public static string StatusOf(VpCam c) { var s = GetState(c); return s != null ? s.st : (string.IsNullOrEmpty(c.St0) ? "open" : c.St0); }

  public static void SetState(VpCam c, string st, string comment) {
    lock (StLock) {
      var old = GetState(c);
      States[c.Guid ?? ""] = new St { st = st ?? (old != null ? old.st : StatusOf(c)), c = comment ?? (old != null ? old.c : ""), by = Environment.UserName, t = Now() };
    }
    System.Threading.ThreadPool.QueueUserWorkItem(_ => SaveAll());
  }

  static string StatesJson() {
    var sb = new StringBuilder("{");
    bool first = true;
    foreach (var kv in States) {
      if (!first) sb.Append(',');
      first = false;
      sb.Append(J(kv.Key)).Append(":{\"st\":").Append(J(kv.Value.st)).Append(",\"c\":").Append(J(kv.Value.c)).Append(",\"by\":").Append(J(kv.Value.by)).Append(",\"t\":").Append(kv.Value.t).Append('}');
    }
    return sb.Append('}').ToString();
  }

  static void WriteHtml() {
    lock (StLock) {
      if (HtmlCache == null || HtmlDest == null) return;
      string tmp = HtmlDest + ".tmp";
      System.IO.File.WriteAllText(tmp, HtmlCache.Replace("__VPNAV_STATE__", StatesJson()), new UTF8Encoding(false));
      if (System.IO.File.Exists(HtmlDest)) System.IO.File.Delete(HtmlDest);
      System.IO.File.Move(tmp, HtmlDest);
    }
  }

  static void SaveAll() {
    try {
      lock (StLock) {
        foreach (var kv in ReadStatusFile()) { St cur; if (!States.TryGetValue(kv.Key, out cur) || kv.Value.t > cur.t) States[kv.Key] = kv.Value; }
        if (StatusFile != null) {
          string tmp = StatusFile + ".tmp";
          System.IO.File.WriteAllText(tmp, "{\"items\":" + StatesJson() + "}", new UTF8Encoding(false));
          if (System.IO.File.Exists(StatusFile)) System.IO.File.Delete(StatusFile);
          System.IO.File.Move(tmp, StatusFile);
        }
        WriteHtml();
      }
    } catch (Exception) { }
  }

  // ElementId from a number in every Revit version (2022/2023: int constructor, 2024+: long constructor)
  public static ElementId Eid(long v) {
    var ctor = typeof(ElementId).GetConstructor(new[] { typeof(long) });
    if (ctor != null) return (ElementId)ctor.Invoke(new object[] { v });
    return (ElementId)typeof(ElementId).GetConstructor(new[] { typeof(int) }).Invoke(new object[] { (int)v });
  }

  public static SD.Image LoadImageNoLock(string path) {
    if (!System.IO.File.Exists(path)) return null;
    try { using (var ms = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(path))) using (var img = SD.Image.FromStream(ms)) return new SD.Bitmap(img); }
    catch (Exception) { return null; }
  }
}

public class NavHandler : IExternalEventHandler {
  const double FT = 0.3048;
  const double CropPlane = 0.1; // Revit measures a perspective crop box's X/Y on a plane 0.1 ft in front of the eye (verified against Navisworks pictures)
  public List<VpCam> Cams = new List<VpCam>();
  public List<int> Order = new List<int>();   // 1-based indexes into Cams that the current model filter shows
  public string Action = "Next";
  public int GoTo = 1;                        // for "Goto": 1-based position in Order
  public bool UseBox = true;
  public double BoxM = 3.0;
  public NavForm Form;
  public int Cur = 0;                         // 1-based index into Cams
  public string PicDir = "";
  public string HtmlPath = "";
  public string Source = "Navisworks viewpoints";
  public string ModelPattern;                 // model-name search typed in the Search box (e.g. the building code)
  public HashSet<int> SearchHit = new HashSet<int>();  // items the search shows: navigable even when anchored to another model
  public string HtmlError = "";
  public bool Isolated;                       // "Isolate in 3D view" is on: every clash shown isolates its elements
  public int PickIndex;                       // for "Pick": index into Hits
  public List<Hit> Hits = new List<Hit>();    // elements of the current item, as found in the open model / its links

  public class Hit {
    public string Model = ""; public int Id; public string Where = "";
    public ElementId Eid; public RevitLinkInstance Link;
    public bool Found { get { return Eid != null; } }
  }

  static List<RevitLinkInstance> Links(Document doc) {
    return new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().Where(l => { try { return l.GetLinkDocument() != null; } catch (Exception) { return false; } }).ToList();
  }

  // find each element by its Element ID: in the open model when the item's model is this file, else in the loaded link of that model
  List<Hit> FindHits(Document doc, VpCam c) {
    var res = new List<Hit>();
    var src = c.IsClash ? c.Items : c.Select.Select(i => new KeyValuePair<string, int>(doc.Title, i)).ToList();
    List<RevitLinkInstance> links = null;
    foreach (var it in src) {
      var h = new Hit { Model = it.Key, Id = it.Value };
      var id = NavData.Eid(it.Value);
      if (ReviztoData.ModelMatches(it.Key, doc.Title) || !c.IsClash) {
        if (doc.GetElement(id) != null) { h.Eid = id; h.Where = "found in this model"; } else h.Where = "not found in this model";
      } else {
        if (links == null) links = Links(doc);
        foreach (var li in links.Where(l => ReviztoData.ModelMatches(it.Key, l.GetLinkDocument().Title))) {
          if (li.GetLinkDocument().GetElement(id) != null) { h.Eid = id; h.Link = li; h.Where = "found in link " + li.GetLinkDocument().Title; break; }
        }
        if (!h.Found) h.Where = links.Any(l => ReviztoData.ModelMatches(it.Key, l.GetLinkDocument().Title)) ? "not found in the link" : "model not open / not linked";
      }
      res.Add(h);
    }
    return res;
  }

  static BoundingBoxXYZ HitBox(Document doc, Hit h) {
    if (!h.Found) return null;
    if (h.Link == null) { var e = doc.GetElement(h.Eid); return e == null ? null : e.get_BoundingBox(null); }
    var le = h.Link.GetLinkDocument().GetElement(h.Eid);
    var bb = le == null ? null : le.get_BoundingBox(null);
    if (bb == null) return null;
    var T = h.Link.GetTotalTransform();
    XYZ lo = null, hi = null;
    foreach (double x in new[] { bb.Min.X, bb.Max.X }) foreach (double y in new[] { bb.Min.Y, bb.Max.Y }) foreach (double z in new[] { bb.Min.Z, bb.Max.Z }) {
      var q = T.OfPoint(new XYZ(x, y, z));
      lo = lo == null ? q : new XYZ(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y), Math.Min(lo.Z, q.Z));
      hi = hi == null ? q : new XYZ(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y), Math.Max(hi.Z, q.Z));
    }
    var r = new BoundingBoxXYZ(); r.Min = lo; r.Max = hi; return r;
  }

  // select one element; linked elements need Selection.SetReferences (Revit 2023+)
  string Pick(UIDocument uidoc, Hit h) {
    if (!h.Found) return "Item #" + h.Id + " (" + h.Model + ") is " + h.Where + ".";
    if (h.Link == null) { uidoc.Selection.SetElementIds(new List<ElementId> { h.Eid }); return "Selected #" + h.Id + " in this model."; }
    var m = uidoc.Selection.GetType().GetMethod("SetReferences");
    if (m != null) {
      var r = new Reference(h.Link.GetLinkDocument().GetElement(h.Eid)).CreateLinkReference(h.Link);
      m.Invoke(uidoc.Selection, new object[] { new List<Reference> { r } });
      return "Selected #" + h.Id + " in link " + h.Link.GetLinkDocument().Title + ".";
    }
    uidoc.Selection.SetElementIds(new List<ElementId> { h.Link.Id });
    return "Revit 2022 cannot select an element inside a link: selected the link " + h.Link.GetLinkDocument().Title + " (element #" + h.Id + ").";
  }

  public string GetName() { return "Viewpoint Navigator"; }

  // Linked elements cannot be isolated one by one in Revit: the link instance is isolated, and every model category
  // other than the clash elements' categories is hidden in the view (linked models shown "By host view" follow it).
  // keep == null shows every model category again.
  static void ApplyCategories(Document doc, View v, HashSet<string> keep) {
    string links = new ElementId(BuiltInCategory.OST_RvtLinks).ToString(), box = new ElementId(BuiltInCategory.OST_SectionBox).ToString();
    foreach (Category cat in doc.Settings.Categories) {
      try {
        if (cat.CategoryType != CategoryType.Model) continue;
        string id = cat.Id.ToString();
        bool hide = keep != null && id != links && id != box && !keep.Contains(id);
        if (!v.CanCategoryBeHidden(cat.Id) || v.GetCategoryHidden(cat.Id) == hide) continue;
        v.SetCategoryHidden(cat.Id, hide);
      } catch (Exception) { }
    }
  }

  static string CatOf(Document doc, Hit h) {
    var d = h.Link != null ? h.Link.GetLinkDocument() : doc;
    var e = d == null ? null : d.GetElement(h.Eid);
    var c = e == null ? null : e.Category;
    if (c != null && c.Parent != null) c = c.Parent;
    return c == null ? null : c.Id.ToString();
  }

  // isolate the found elements of the item in view v (clash: always). Host elements by temporary isolate; linked ones as above.
  void ApplyIsolation(Document doc, View3D v, List<Hit> hits, bool on) {
    if (v.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate)) v.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
    var found = hits.Where(x => x.Found).ToList();
    var iso = found.Select(x => x.Link == null ? x.Eid : x.Link.Id).Distinct().ToList();
    if (on && iso.Count > 0) v.IsolateElementsTemporary(iso);
    HashSet<string> keep = null;
    if (on && found.Any(x => x.Link != null)) {
      keep = new HashSet<string>(found.Select(x => CatOf(doc, x)).Where(x => x != null));
      if (keep.Count == 0) keep = null;
    }
    ApplyCategories(doc, v, keep);
  }

  // orthographic copy of the VP view to work in: same look direction, same section box (or one around the item), found elements isolated if Isolate is on
  string DuplicateOrtho(UIDocument uidoc, View3D tv, VpCam c) {
    var doc = uidoc.Document;
    var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
    string baseName = "Work - " + tv.Name, nm = baseName;
    for (int i = 2; names.Contains(nm); i++) nm = baseName + " (" + i + ")";
    View3D nv;
    using (var tx = new Transaction(doc, "Viewpoint Navigator - orthographic work view")) {
      tx.Start();
      var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
      nv = View3D.CreateIsometric(doc, vft.Id);
      try { nv.Name = nm; } catch (Exception) { }
      // work view: no view template, every annotation category off, only the section box shown
      try { if (nv.ViewTemplateId != ElementId.InvalidElementId) nv.ViewTemplateId = ElementId.InvalidElementId; } catch (Exception) { }
      var sbCat = new ElementId(BuiltInCategory.OST_SectionBox);
      foreach (Category cat in doc.Settings.Categories) {
        if (cat.CategoryType != CategoryType.Annotation || cat.Id == sbCat) continue;
        try { if (nv.CanCategoryBeHidden(cat.Id)) nv.SetCategoryHidden(cat.Id, true); } catch (Exception) { }
      }
      try { nv.SetCategoryHidden(sbCat, false); } catch (Exception) { }
      var o = tv.GetOrientation();
      nv.SetOrientation(new ViewOrientation3D(o.EyePosition, o.UpDirection, o.ForwardDirection));
      nv.CropBoxActive = false; nv.CropBoxVisible = false;
      if (tv.IsSectionBoxActive) { nv.SetSectionBox(tv.GetSectionBox()); nv.IsSectionBoxActive = true; }
      else {
        XYZ p = c.HasPoint ? c.Point : c.Target;
        double h = Math.Max(BoxM, 1.0) / FT / 2.0;
        var bb = new BoundingBoxXYZ(); bb.Min = new XYZ(p.X - h, p.Y - h, p.Z - h); bb.Max = new XYZ(p.X + h, p.Y + h, p.Z + h);
        nv.SetSectionBox(bb); nv.IsSectionBoxActive = true;
      }
      ApplyIsolation(doc, nv, Hits, Isolated || c.IsClash);
      tx.Commit();
    }
    uidoc.ActiveView = nv;
    try { var uiv = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == nv.Id); if (uiv != null) uiv.ZoomToFit(); } catch (Exception) { }
    var sel = Hits.Where(x => x.Found && x.Link == null).Select(x => x.Eid).ToList();
    if (sel.Count > 0) { try { uidoc.Selection.SetElementIds(sel); } catch (Exception) { } }
    return nv.Name;
  }

  // width / height of the picture (falls back to the camera aspect)
  double FrameAspect(VpCam c) {
    try {
      using (var img = NavData.LoadImageNoLock(NavData.BeforePath(PicDir, c)))
        if (img != null && img.Height > 0) return (double)img.Width / img.Height;
    } catch (Exception) { }
    return c.Aspect > 0.1 ? c.Aspect : 1.0;
  }

  // ISO 19650 style code search: "D0", "33", "03306" match a whole field of the name (fields split at - _ . space),
  // so D0 finds ...-AMP-D0-ZZ-..., "Zone 5 - D0" and "COURTYARD D0-GF-04" but not "D01". A search with - or . inside
  // ("RB-08", "G2.GF") matches that text anywhere; * and ? are wildcards; several searches separated by ; or ,
  public static bool PatternMatch(string text, string pat) {
    if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(pat)) return false;
    string[] fields = null;
    foreach (var p0 in pat.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)) {
      string p = p0.Trim(); if (p == "") continue;
      if (p.IndexOf('*') >= 0 || p.IndexOf('?') >= 0) {
        string rx = System.Text.RegularExpressions.Regex.Escape(p).Replace("\\*", "[^-_. ]*").Replace("\\?", ".");
        if (System.Text.RegularExpressions.Regex.IsMatch(text, "(^|[-_. ])" + rx + "($|[-_. ])", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
      } else if (p.IndexOfAny(new[] { '-', '_', '.', ' ' }) >= 0) {
        if (text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
      } else {
        if (fields == null) fields = text.Split(new[] { '-', '_', '.', ' ', '/', '(', ')', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Any(f => string.Equals(f, p, StringComparison.OrdinalIgnoreCase))) return true;
      }
    }
    return false;
  }

  // search group of an item: "M:<model>" anchor model matches; "X" anchored to another model but zone / room match;
  // no anchor model -> "Z" zone matches, else "R" room matches,
  // else "U" when zone and room say nothing about the building (empty, or only "Zone 4"); null = not this building
  public static string SearchGroup(VpCam c, string pat) {
    var ms = (c.Models.Count > 0 ? c.Models : new List<string> { c.Model ?? "" }).Where(m => m != "").ToList();
    if (ms.Count > 0) {
      var hit = ms.FirstOrDefault(m => PatternMatch(m, pat));
      if (hit != null) return "M:" + hit;
      return PatternMatch(c.Zone, pat) || PatternMatch(c.Room, pat) ? "X" : null;   // anchored to another model, but zone / room say this building
    }
    if (PatternMatch(c.Zone, pat)) return "Z";
    if (PatternMatch(c.Room, pat)) return "R";
    bool zoneEmpty = c.Zone == "" || System.Text.RegularExpressions.Regex.IsMatch(c.Zone, @"^\s*zone\s*\d*\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return zoneEmpty && c.Room == "" ? "U" : null;
  }

  // preview only: item of another model, unless the search shows it (e.g. an issue anchored to the wrong model of the same building)
  bool NotHere(VpCam c, Document doc) { return IsOtherModel(c, doc) && !SearchHit.Contains(c.Index); }

  static bool IsOtherModel(VpCam c, Document doc) {
    if (c.IsClash) {
      if (c.Models.Any(m => ReviztoData.ModelMatches(m, doc.Title))) return false;
      var lt = Links(doc).Select(l => l.GetLinkDocument().Title).ToList();
      return !c.Models.Any(m => lt.Any(t => ReviztoData.ModelMatches(m, t)));   // a clash between linked models can still be shown
    }
    if (c.Models.Count > 0) return !c.Models.Any(m => ReviztoData.ModelMatches(m, doc.Title));
    return !string.IsNullOrEmpty(c.Model) && !ReviztoData.ModelMatches(c.Model, doc.Title);
  }

  // clash: elements of the open model, section box around them (capped around the clash point), camera looking at it
  List<ElementId> ClashSetup(Document doc, VpCam c, List<Hit> hits, out BoundingBoxXYZ box) {
    var ids = hits.Where(x => x.Found && x.Link == null).Select(x => x.Eid).Distinct().ToList();   // host elements to select
    var boxes = hits.Select(x => HitBox(doc, x)).Where(b => b != null).ToList();
    double pad = 0.3 / FT, cap = Math.Max(BoxM, 1.0) / FT;
    XYZ p = c.Point;
    XYZ lo = new XYZ(p.X - pad, p.Y - pad, p.Z - pad), hi = new XYZ(p.X + pad, p.Y + pad, p.Z + pad);
    foreach (var bb in boxes) {
      lo = new XYZ(Math.Min(lo.X, bb.Min.X - pad), Math.Min(lo.Y, bb.Min.Y - pad), Math.Min(lo.Z, bb.Min.Z - pad));
      hi = new XYZ(Math.Max(hi.X, bb.Max.X + pad), Math.Max(hi.Y, bb.Max.Y + pad), Math.Max(hi.Z, bb.Max.Z + pad));
    }
    if (boxes.Count == 0) { lo = new XYZ(p.X - cap / 2, p.Y - cap / 2, p.Z - cap / 2); hi = new XYZ(p.X + cap / 2, p.Y + cap / 2, p.Z + cap / 2); }
    // a long wall or a slab would give a huge box: keep it within Box size around the clash point
    lo = new XYZ(Math.Max(lo.X, p.X - cap), Math.Max(lo.Y, p.Y - cap), Math.Max(lo.Z, p.Z - cap));
    hi = new XYZ(Math.Min(hi.X, p.X + cap), Math.Min(hi.Y, p.Y + cap), Math.Min(hi.Z, p.Z + cap));
    box = new BoundingBoxXYZ(); box.Min = lo; box.Max = hi;
    XYZ center = (lo + hi) * 0.5;
    double diag = Math.Max((hi - lo).GetLength(), 1.0 / FT);
    XYZ dir = new XYZ(1, -1, 0.9).Normalize();                    // from the south-east, above
    c.Eye = center + dir * (diag * 1.15);
    c.Fwd = (center - c.Eye).Normalize();
    c.Up = c.Fwd.CrossProduct(XYZ.BasisZ).CrossProduct(c.Fwd).Normalize();
    c.Target = center;
    return ids;
  }

  string ViewName(int index, VpCam c) {
    string clean = System.Text.RegularExpressions.Regex.Replace(c.Name ?? "", @"[\\:{}\[\]|;<>?`~]", "-");
    int digits = Math.Max(2, Cams.Count.ToString().Length);
    return "VP-" + index.ToString(new string('0', digits)) + " " + clean;
  }
  static int ViewIndex(string name) {
    var m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"^VP-(\d+)");
    int n; return m.Success && int.TryParse(m.Groups[1].Value, out n) ? n : 0;
  }

  public void Execute(UIApplication app) {
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try {
      var uidoc = app.ActiveUIDocument;
      if (uidoc == null) return;
      var doc = uidoc.Document;
      int count = Cams.Count;
      if (count == 0) return;
      if (Order == null || Order.Count == 0) Order = Enumerable.Range(1, count).ToList();

      var vpViews = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
        .Where(x => !x.IsTemplate && x.IsPerspective && x.Name.StartsWith("VP-")).ToList();
      View3D tv = null;
      var av = uidoc.ActiveView;
      if (av != null && av.Name.StartsWith("VP-")) tv = vpViews.FirstOrDefault(x => x.Id == av.Id);
      if (tv == null) tv = vpViews.OrderBy(x => x.Name).FirstOrDefault();

      int cur = Cur;
      if (cur < 1 && tv != null) cur = ViewIndex(tv.Name);
      string a = (Action ?? "Next");

      if (a == "Snapshot") {
        if (cur >= 1 && cur <= count && NotHere(Cams[cur - 1], doc)) {
          if (Form != null && !Form.IsDisposed) Form.ShowOtherModel(Cams[cur - 1].Model, doc.Title);
          return;
        }
        if (tv == null || cur < 1 || cur > count) {
          if (Form != null && !Form.IsDisposed) Form.ShowError("Open a VP view first (click Next or Previous), then save the snapshot.");
          return;
        }
        string saved = SaveSnapshot(doc, tv, Cams[cur - 1]);
        Cur = cur;
        if (Form != null && !Form.IsDisposed) { Form.Sync(cur); Form.ShowSaved(saved); }
        return;
      }
      if (a == "Pick") {
        string msg = (PickIndex >= 0 && PickIndex < Hits.Count) ? Pick(uidoc, Hits[PickIndex]) : "Nothing to select.";
        if (Form != null && !Form.IsDisposed) Form.ShowMessage(msg);
        return;
      }
      if (a == "Isolate") { Isolated = !Isolated; a = "Refresh"; }
      if (a == "Duplicate") {
        if (tv == null || cur < 1 || cur > count) { if (Form != null && !Form.IsDisposed) Form.ShowError("Open a VP view first (click Next or Previous), then duplicate it."); return; }
        string made = DuplicateOrtho(uidoc, tv, Cams[cur - 1]);
        if (Form != null && !Form.IsDisposed) Form.ShowMessage("Created \"" + made + "\": orthographic, no crop, annotations off, same section box" + (Cams[cur - 1].IsClash ? ", clashing elements isolated" : "") + ". Next / Previous go back to the VP view.");
        return;
      }

      int pos = Order.IndexOf(cur); // -1 if the current item is not in the filter
      int tpos;
      if (a == "Goto") tpos = Math.Min(Math.Max(GoTo, 1), Order.Count) - 1;
      else if (a == "Prev") tpos = pos <= 0 ? Order.Count - 1 : pos - 1;
      else if (a == "Refresh") tpos = pos < 0 ? 0 : pos;
      else tpos = (pos < 0 || pos >= Order.Count - 1) ? 0 : pos + 1;
      int target = Order[tpos];

      var c = Cams[target - 1];
      // item anchored to a different model: preview only, the Revit view does not move
      if (NotHere(c, doc)) {
        Cur = target;
        Hits = (c.IsClash ? c.Items : c.Select.Select(i => new KeyValuePair<string, int>(c.Model, i)).ToList())
          .Select(it => new Hit { Model = it.Key, Id = it.Value, Where = "model not open / not linked" }).ToList();
        if (Form != null && !Form.IsDisposed) { Form.Sync(target); Form.ShowOtherModel(c.Model, doc.Title); }
        return;
      }
      string nm = ViewName(target, c);
      BoundingBoxXYZ clashBox = null;
      List<ElementId> clashIds = null;
      var hits = FindHits(doc, c);
      Hits = hits;
      if (c.IsClash) clashIds = ClashSetup(doc, c, hits, out clashBox);

      using (var tx = new Transaction(doc, "Viewpoint Navigator")) {
        tx.Start();
        if (tv == null) {
          var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
          tv = View3D.CreatePerspective(doc, vft.Id);
        }
        if (tv.Name != nm) { try { tv.Name = nm; } catch (Exception) { tv.Name = nm + " (" + tv.Id.ToString() + ")"; } }
        tv.SetOrientation(new ViewOrientation3D(c.Eye, c.Up, c.Fwd));

        // same field of view and picture shape as the source picture
        if (c.FovH > 0.01 && c.FovH < 3.0) {
          doc.Regenerate();
          var cb = tv.CropBox;
          double hh = CropPlane * Math.Tan(c.FovH / 2.0), hw = hh * FrameAspect(c);
          cb.Min = new XYZ(-hw, -hh, cb.Min.Z);
          cb.Max = new XYZ(hw, hh, cb.Max.Z);
          tv.CropBox = cb;
          tv.CropBoxActive = true;
        }

        if (UseBox && clashBox != null) {
          tv.SetSectionBox(clashBox);
          tv.IsSectionBoxActive = true;
        } else if (UseBox && c.HasPlane) {
          XYZ nrm = c.PlaneN;
          XYZ bx = nrm;
          XYZ by = Math.Abs(bx.Z) > 0.9 ? XYZ.BasisX : XYZ.BasisZ.CrossProduct(bx).Normalize();
          XYZ bz = bx.CrossProduct(by).Normalize();
          double dist = nrm.DotProduct(c.Target - c.PlanePt);
          XYZ origin = c.Target - nrm.Multiply(dist);
          double h = BoxM / FT / 2.0;
          double depth = BoxM / FT;
          if (dist > depth / 2.0) depth = dist + h;
          var t = Transform.Identity;
          t.Origin = origin; t.BasisX = bx; t.BasisY = by; t.BasisZ = bz;
          var bb = new BoundingBoxXYZ();
          bb.Transform = t;
          bb.Min = new XYZ(0, -h, -h);
          bb.Max = new XYZ(depth, h, h);
          tv.SetSectionBox(bb);
          tv.IsSectionBoxActive = true;
        } else if (UseBox && c.HasPoint) {
          double h = BoxM / FT / 2.0;
          var bb = new BoundingBoxXYZ();
          bool lookingDown = c.Eye.Z > c.Point.Z + 0.3 / FT && c.Fwd.Z < -0.2;
          if (lookingDown) {
            // Revizto issues sit on the section cut: top of the box at the issue point, camera outside (above) it,
            // wide enough to fill the picture, Box size (min 3 m) deep
            double reach = Math.Max(h, 2.0 * (c.Eye - c.Point).GetLength());
            bb.Min = new XYZ(c.Point.X - reach, c.Point.Y - reach, c.Point.Z - Math.Max(BoxM, 3.0) / FT);
            bb.Max = new XYZ(c.Point.X + reach, c.Point.Y + reach, c.Point.Z + 0.02 / FT);
          } else {
            // looking sideways: box of Box size around the issue point, grown so the camera stays inside
            XYZ lo = new XYZ(c.Point.X - h, c.Point.Y - h, c.Point.Z - h), hi = new XYZ(c.Point.X + h, c.Point.Y + h, c.Point.Z + h);
            double m = 0.5 / FT;
            lo = new XYZ(Math.Min(lo.X, c.Eye.X - m), Math.Min(lo.Y, c.Eye.Y - m), Math.Min(lo.Z, c.Eye.Z - m));
            hi = new XYZ(Math.Max(hi.X, c.Eye.X + m), Math.Max(hi.Y, c.Eye.Y + m), Math.Max(hi.Z, c.Eye.Z + m));
            bb.Min = lo; bb.Max = hi;
          }
          tv.SetSectionBox(bb);
          tv.IsSectionBoxActive = true;
        } else {
          tv.IsSectionBoxActive = false;
        }
        // clash: always only the clashing elements; Revizto: when "Isolate in 3D view" is on
        ApplyIsolation(doc, tv, hits, Isolated || c.IsClash);
        tx.Commit();
      }

      if (uidoc.ActiveView == null || uidoc.ActiveView.Id != tv.Id) uidoc.ActiveView = tv;
      try { var uiv = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == tv.Id); if (uiv != null) uiv.ZoomToFit(); } catch (Exception) { }
      // close the tabs of earlier work views (they stay in the project): every extra open 3D view with links makes Revit slower
      try {
        foreach (var u in uidoc.GetOpenUIViews()) {
          var vv = doc.GetElement(u.ViewId) as View;
          if (vv != null && vv.Id != tv.Id && vv.Name.StartsWith("Work - VP-")) u.Close();
        }
      } catch (Exception) { }
      var sel = hits.Where(x => x.Found && x.Link == null).Select(x => x.Eid).Distinct().ToList();
      if (sel.Count > 0) { try { uidoc.Selection.SetElementIds(sel); } catch (Exception) { } }
      Cur = target;
      if (Form != null && !Form.IsDisposed) { Form.Sync(target); if (hits.Count > 0) Form.ShowSelection(hits.Count(x => x.Found), hits.Count); Form.ShowTime(sw.ElapsedMilliseconds); }
    } catch (Exception ex) {
      if (Form != null && !Form.IsDisposed) Form.ShowError(ex.Message);
    }
  }

  // Exports exactly the VP view's crop frame (= the source picture's frame) and saves it at that picture's shape.
  string SaveSnapshot(Document doc, View3D v, VpCam c) {
    if (string.IsNullOrWhiteSpace(PicDir)) throw new Exception("No picture folder is set.");
    System.IO.Directory.CreateDirectory(PicDir);
    string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vpnav_" + System.Guid.NewGuid().ToString("N"));
    System.IO.Directory.CreateDirectory(tmp);
    try {
      int outW = 1024, outH = 1024;
      using (var before = NavData.LoadImageNoLock(NavData.BeforePath(PicDir, c))) {
        if (before != null) { double k = 1024.0 / Math.Max(before.Width, before.Height); outW = (int)Math.Round(before.Width * k); outH = (int)Math.Round(before.Height * k); }
        else { double asp = c.Aspect > 0.1 ? c.Aspect : 1.0; outH = (int)Math.Round(outW / asp); }
      }
      var o = new ImageExportOptions();
      o.ExportRange = ExportRange.SetOfViews;
      o.SetViewsAndSheets(new List<ElementId> { v.Id });
      o.FilePath = System.IO.Path.Combine(tmp, "snap");
      o.ZoomType = ZoomFitType.FitToPage;
      o.FitDirection = FitDirectionType.Horizontal;
      o.PixelSize = Math.Max(outW, 512);
      o.ImageResolution = ImageResolution.DPI_150;
      o.HLRandWFViewsFileType = ImageFileType.JPEGLossless;
      o.ShadowViewsFileType = ImageFileType.JPEGLossless;
      doc.ExportImage(o);
      var f = new System.IO.DirectoryInfo(tmp).GetFiles().OrderByDescending(x => x.Length).FirstOrDefault();
      if (f == null) throw new Exception("Revit did not produce an image.");
      string dest = NavData.AfterPath(PicDir, c);
      string tmpOut = System.IO.Path.Combine(tmp, "out.jpg");
      using (var src = NavData.LoadImageNoLock(f.FullName))
      using (var bmp = new SD.Bitmap(outW, outH))
      using (var g = SD.Graphics.FromImage(bmp)) {
        g.InterpolationMode = SD.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.Clear(SD.Color.White);
        g.DrawImage(src, new SD.Rectangle(0, 0, outW, outH));
        var enc = SD.Imaging.ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == SD.Imaging.ImageFormat.Jpeg.Guid);
        var ps = new SD.Imaging.EncoderParameters(1);
        ps.Param[0] = new SD.Imaging.EncoderParameter(SD.Imaging.Encoder.Quality, 92L);
        bmp.Save(tmpOut, enc, ps);
      }
      System.IO.File.Copy(tmpOut, dest, true);
      return dest;
    } finally {
      try { System.IO.Directory.Delete(tmp, true); } catch (Exception) { }
    }
  }
}

public class NavForm : WF.Form {
  public static NavForm Instance;
  const double FT = 0.3048;
  readonly NavHandler H;
  readonly ExternalEvent Ev;
  WF.Label lblName, lblCount, lblInfo, lblPic;
  WF.Button btnPrev, btnNext, btnSnap, btnOpen, btnWeb;
  WF.PictureBox pic;
  WF.RadioButton rbBefore, rbAfter;
  string lastSaved;
  WF.ComboBox cmb, cmbModel, cmbTest;
  List<string> testKeys = new List<string>();
  bool isClash;
  WF.CheckBox chk;
  WF.NumericUpDown num;
  bool syncing;
  int curN;
  List<string> modelKeys = new List<string>(); // "" = all
  string appliedKey = null;                    // model filter that H.Order currently reflects
  string sourceWord;

  WF.Button btnIso, btnDup;
  WF.ComboBox cmbSt;
  static readonly string[] StKeys = { "open", "closed", "clarify", "others" };
  WF.Label lblDone;
  WF.TextBox txtComment, txtSub;
  WF.Button btnPost;
  bool editingComment = true;
  WF.ComboBox cmbSub, cmbSubSt;
  WF.Button[] btnPick = new WF.Button[2];
  WF.Label[] lblItem = new WF.Label[2];
  WF.Timer subTimer = new WF.Timer { Interval = 350 };
  WF.TextBox txtMS;                                    // model-name search (building code)
  WF.Timer msTimer = new WF.Timer { Interval = 700 };
  string modelPat = "";                                // applied model-name search
  bool anyModel, isRevizto, srcStatus;
  WF.FlowLayoutPanel tagPanel;                         // search results as tags (models / zone / room / not located)
  Dictionary<int, string> hitGroup = new Dictionary<int, string>();   // VpCam.Index -> search group
  List<string> tagKeys = new List<string>();
  Dictionary<string, bool> tagOn = new Dictionary<string, bool>();
  List<string> fullItems = new List<string>(), fullKeys = new List<string>();   // model list without a search
  List<string> searchKeys = new List<string>();        // model list entries while a search is active ("*" = all results, "?" = custom)
  int fullIndex = 0;
  WF.ComboBox cmbStamp;
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
  int pendingN = 0;   // item whose comment is waiting to be saved

  public NavForm(NavHandler h, ExternalEvent ev, string defaultModel) {
    H = h; Ev = ev;
    isClash = H.Source.StartsWith("Navisworks clash");
    isRevizto = H.Source.StartsWith("Revizto");
    srcStatus = isClash || isRevizto;
    bool isVp = H.Source.StartsWith("Navisworks viewpoints");
    sourceWord = H.Source.StartsWith("Revizto") ? "issues" : isClash ? "clashes" : "viewpoints";
    Text = "Viewpoint Navigator - " + H.Source;
    FormBorderStyle = WF.FormBorderStyle.FixedSingle;
    MinimizeBox = true; MaximizeBox = false;
    ShowInTaskbar = true; // so a minimized window can be restored from the taskbar
    StartPosition = WF.FormStartPosition.Manual;
    var wa = WF.Screen.PrimaryScreen.WorkingArea;
    int clientH = Math.Min(isClash ? 1048 : 968, wa.Height - 50);
    ClientSize = new SD.Size(380, clientH);
    Location = new SD.Point(wa.Right - Width - 24, Math.Max(wa.Top, Math.Min(wa.Top + 30, wa.Bottom - Height)));
    Font = new SD.Font("Segoe UI", 9.5f);
    KeyPreview = true;
    var blue = SD.Color.FromArgb(0, 120, 212);

    lblName = new WF.Label { Left = 12, Top = 10, Width = 356, Height = 26, Font = new SD.Font("Segoe UI Semibold", 12f), AutoEllipsis = true, Text = "Loading..." };
    lblCount = new WF.Label { Left = 12, Top = 38, Width = 356, Height = 20, ForeColor = SD.Color.DimGray, AutoEllipsis = true, Text = "" };
    lblInfo = new WF.Label { Left = 12, Top = 60, Width = 356, Height = 34, ForeColor = SD.Color.DimGray, Text = "" };
    btnPrev = new WF.Button { Left = 12, Top = 102, Width = 176, Height = 46, Text = "<  Previous", Font = new SD.Font("Segoe UI Semibold", 11f) };
    btnNext = new WF.Button { Left = 192, Top = 102, Width = 176, Height = 46, Text = "Next  >", Font = new SD.Font("Segoe UI Semibold", 11f), FlatStyle = WF.FlatStyle.Flat, BackColor = blue, ForeColor = SD.Color.White, UseVisualStyleBackColor = false };
    btnNext.FlatAppearance.BorderColor = SD.Color.FromArgb(0, 95, 184);
    int y = 160;
    var lblModel = new WF.Label { Left = 12, Top = y + 3, Width = 48, Height = 20, Text = "Model" };
    cmbModel = new WF.ComboBox { Left = 62, Top = y, Width = 306, DropDownStyle = WF.ComboBoxStyle.DropDownList, DropDownWidth = 520 };
    y += 34;
    var lblMS = new WF.Label { Left = 12, Top = y + 3, Width = 50, Height = 20, Text = "Search" };
    txtMS = new WF.TextBox { Left = 62, Top = y, Width = 306 };
    txtMS.HandleCreated += (s, e) => { try { SendMessage(txtMS.Handle, 0x1501, (IntPtr)1, "Building / Volume code / Sequential number"); } catch (Exception) { } };
    y += 30;
    tagPanel = new WF.FlowLayoutPanel { Left = 12, Top = y, Width = 356, Height = 58, AutoScroll = true, WrapContents = true, BackColor = SD.Color.FromArgb(248, 248, 248), BorderStyle = WF.BorderStyle.None };
    y += 62;
    var lblTest = new WF.Label { Left = 12, Top = y + 3, Width = 48, Height = 20, Text = "Test", Visible = isClash };
    cmbTest = new WF.ComboBox { Left = 62, Top = y, Width = 306, DropDownStyle = WF.ComboBoxStyle.DropDownList, DropDownWidth = 420, Visible = isClash };
    if (isClash) y += 34;
    var lblSub = new WF.Label { Left = 12, Top = y + 3, Width = 48, Height = 20, Text = "Filter" };
    cmbSub = new WF.ComboBox { Left = 62, Top = y, Width = 112, DropDownStyle = WF.ComboBoxStyle.DropDownList };
    cmbSub.Items.AddRange(new object[] { "No filter", isClash ? "Clash name" : "Name", isClash ? "Clash status" : isRevizto ? "Revizto status" : "Status" });
    if (isRevizto) cmbSub.Items.Add("Stamp");
    cmbStamp = new WF.ComboBox { Left = 180, Top = y, Width = 188, DropDownStyle = WF.ComboBoxStyle.DropDown, Visible = false };
    foreach (var g in H.Cams.Where(c => c.Stamp != "").GroupBy(c => c.Stamp, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key)) cmbStamp.Items.Add(g.Key + "   (" + g.Count() + ")");
    txtSub = new WF.TextBox { Left = 180, Top = y, Width = 188, Visible = false };
    cmbSubSt = new WF.ComboBox { Left = 180, Top = y, Width = 188, DropDownStyle = WF.ComboBoxStyle.DropDownList, Visible = false };
    if (srcStatus) {
      // status from the source (Navisworks clash status / Revizto status), not the report status below
      var order = new List<string> { "New", "Open", "Active", "In progress", "Reviewed", "Approved", "Solved", "Resolved", "Closed" };
      foreach (var s0 in H.Cams.Select(c => c.Status ?? "").Where(s0 => s0 != "").Distinct().OrderBy(s0 => order.IndexOf(s0) < 0 ? 99 : order.IndexOf(s0))) cmbSubSt.Items.Add(s0);
      if (cmbSubSt.Items.Count == 0) cmbSubSt.Items.Add("(no status in the report)");
    } else {
      cmbSubSt.DrawMode = WF.DrawMode.OwnerDrawFixed; cmbSubSt.ItemHeight = 17;
      cmbSubSt.DrawItem += DrawStatusItem;
      cmbSubSt.Items.AddRange(new object[] { "Open", "Closed", "Need clarification", "Action by others" });
    }
    y += 34;
    cmb = new WF.ComboBox { Left = 12, Top = y, Width = 356, DropDownStyle = WF.ComboBoxStyle.DropDownList, DropDownWidth = 520 };
    y += 36;
    for (int i = 0; i < 2; i++) {
      int k = i;
      btnPick[i] = new WF.Button { Left = 12, Top = y, Width = 30, Height = 26, Text = "", Image = IdIcon(), ImageAlign = SD.ContentAlignment.MiddleCenter, Visible = isClash, Enabled = false };
      lblItem[i] = new WF.Label { Left = 48, Top = y + 4, Width = 320, Height = 20, AutoEllipsis = true, Visible = isClash, Text = "Item " + (i + 1) };
      btnPick[i].Click += (s, e) => { H.PickIndex = k; H.Action = "Pick"; Ev.Raise(); };
      if (isClash) y += 30;
    }
    chk = new WF.CheckBox { Left = 12, Top = y, Width = 150, Height = 24, Text = "Section box", Checked = true };
    var lblSize = new WF.Label { Left = 170, Top = y + 3, Width = 110, Height = 20, Text = "Box size (m):" };
    num = new WF.NumericUpDown { Left = 280, Top = y - 1, Width = 88, DecimalPlaces = 1, Increment = 0.5m, Minimum = 0.5m, Maximum = 50m, Value = (decimal)Math.Min(50.0, Math.Max(0.5, H.BoxM)) };
    y += 32;
    bool noIso = isVp || isClash;   // clashes are always isolated: no button
    btnIso = new WF.Button { Left = 12, Top = y, Width = 176, Height = 34, Text = "Isolate in 3D view", Visible = !noIso, Enabled = false };
    btnSnap = new WF.Button { Left = noIso ? 12 : 192, Top = y, Width = noIso ? 356 : 176, Height = 34, Text = "Save solved snapshot", Font = new SD.Font("Segoe UI Semibold", 9.5f) };
    y += 40;
    btnDup = new WF.Button { Left = 12, Top = y, Width = 356, Height = 32, Text = "Duplicate 3D view" };
    y += 38;
    var lblAct = new WF.Label { Left = 12, Top = y, Width = 84, Height = 16, ForeColor = SD.Color.DimGray, Font = new SD.Font("Segoe UI", 8.5f), Text = "Action status" };
    y += 16;
    cmbSt = new WF.ComboBox { Left = 12, Top = y + 4, Width = 124, DropDownWidth = 160, DropDownStyle = WF.ComboBoxStyle.DropDownList, Font = new SD.Font("Segoe UI Semibold", 9.5f), DrawMode = WF.DrawMode.OwnerDrawFixed, ItemHeight = 18 };
    cmbSt.DrawItem += DrawStatusItem;
    cmbSt.Items.AddRange(new object[] { "Open", "Closed", "Need clarification", "Action by others" });
    lblDone = new WF.Label { Left = 96, Top = y - 16, Width = 272, Height = 16, ForeColor = SD.Color.Gray, Font = new SD.Font("Segoe UI", 8f), TextAlign = SD.ContentAlignment.TopRight, AutoEllipsis = true, Text = "" };
    btnWeb = new WF.Button { Left = 214, Top = y, Width = 86, Height = 32, Text = "Web report" };
    btnOpen = new WF.Button { Left = 304, Top = y, Width = 64, Height = 32, Text = "Folder" };
    y += 38;
    var lblCm = new WF.Label { Left = 12, Top = y, Width = 356, Height = 18, ForeColor = SD.Color.DimGray, Font = new SD.Font("Segoe UI", 8.5f), Text = "Comment  (Enter = post, Shift+Enter = new line)" };
    y += 18;
    txtComment = new WF.TextBox { Left = 12, Top = y, Width = 288, Height = 44, Multiline = true, ScrollBars = WF.ScrollBars.Vertical };
    btnPost = new WF.Button { Left = 304, Top = y, Width = 64, Height = 44, Text = "Post", Font = new SD.Font("Segoe UI Semibold", 9.5f) };
    y += 50;
    rbBefore = new WF.RadioButton { Left = 12, Top = y, Width = 150, Height = 22, Text = H.Source.StartsWith("Revizto") ? "Revizto snapshot" : isClash ? "Clash picture" : "Navisworks", Checked = true };
    rbAfter = new WF.RadioButton { Left = 166, Top = y, Width = 150, Height = 22, Text = "Solved snapshot" };
    y += 26;
    int picH = Math.Max(140, clientH - y - 40);
    pic = new WF.PictureBox { Left = 12, Top = y, Width = 356, Height = picH, SizeMode = WF.PictureBoxSizeMode.Zoom, BorderStyle = WF.BorderStyle.FixedSingle, BackColor = SD.Color.FromArgb(30, 30, 30), Cursor = WF.Cursors.Hand };
    lblPic = new WF.Label { Left = 12, Top = y + picH + 4, Width = 356, Height = 32, ForeColor = SD.Color.Gray, Font = new SD.Font("Segoe UI", 8.5f), Text = "" };
    if (y + picH + 40 > clientH) ClientSize = new SD.Size(380, y + picH + 40);

    var tip = new WF.ToolTip();
    tip.SetToolTip(btnSnap, "Exports the VP view's frame (same field of view as the source picture) to <picture>_after.jpg in:\n" + (H.PicDir ?? "") + "\nShortcut: Ctrl+S");
    tip.SetToolTip(btnWeb, "Open the web navigator for this project");
    tip.SetToolTip(btnOpen, H.PicDir ?? "");
    tip.SetToolTip(pic, "Click to open the picture full size");
    tip.SetToolTip(cmbModel, "One model, or all models. Use Search below to show several models by part of their name.");
    tip.SetToolTip(txtMS, "Building / volume code or sequential number, as in the ISO 19650 file name (e.g. D0, 33, 03306): matches a whole field of the name.\n1st the anchor model; issues without an anchor model by Zone, then Room. 'Other anchor' = anchored to another model but zone / room match.\nIssues without any location show as 'Not located' (off until you click it).\n* and ? are wildcards, ; separates several codes (D0; D1). Results show as tags below: click a tag to hide / show it.\nShown items open in 3D even when anchored to another model. Remembered for next time.");
    if (isClash) tip.SetToolTip(cmbSub, "Clash status = the Navisworks status from the clash report (New / Active / Reviewed / Approved / Resolved). The coloured Status below is your report status.");
    if (isRevizto) tip.SetToolTip(cmbSub, "Revizto status = the status in the Revizto Excel (Open / In progress / Solved / Closed). Stamp = stamp abbreviation; several with ; (AMP; SEC = either). The coloured Status below is your report status.");
    tip.SetToolTip(cmbStamp, "Stamp abbreviation. Several separated by ; show any of them (AMP; SEC).");
    tip.SetToolTip(btnIso, "Temporarily isolate the found elements in the VP view. Click again to restore the original clash view.");
    tip.SetToolTip(btnPick[0], "Select by ID: select this element in Revit (found by its Element ID)");
    tip.SetToolTip(btnPick[1], "Select by ID: select this element in Revit (found by its Element ID)");
    tip.SetToolTip(btnDup, "Orthographic copy of this view (Work - VP-...): same direction and section box, all annotations off. Fix the issue there; the VP view stays for the snapshot.");
    tip.SetToolTip(cmbSt, "Action status (your own): Open / Closed / Need clarification / Action by others (another contractor has to act). The Revizto / clash status from the source is shown in the info line above and in Filter. Shared with the web navigator (navigator_status.json in the picture folder). New items start as Open.");
    Controls.AddRange(new WF.Control[] { lblName, lblCount, lblInfo, btnPrev, btnNext, lblModel, cmbModel, lblMS, txtMS, tagPanel, lblTest, cmbTest, lblSub, cmbSub, txtSub, cmbSubSt, cmbStamp, lblAct, cmb,
      btnPick[0], lblItem[0], btnPick[1], lblItem[1], chk, lblSize, num, btnIso, btnSnap, btnDup, cmbSt, lblDone, btnWeb, btnOpen, lblCm, txtComment, btnPost, rbBefore, rbAfter, pic, lblPic });

    // model list: "All" first, then every model with its count; default = the model that matches the open file
    var groups = H.Cams.SelectMany(c => c.Models.Count > 0 ? c.Models : new List<string> { c.Model ?? "" }).GroupBy(m => m, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key == "" ? "~" : g.Key, StringComparer.OrdinalIgnoreCase).ToList();
    anyModel = groups.Any(g => g.Key != "");
    cmbModel.Items.Add("All models (" + H.Cams.Count + " " + sourceWord + ")"); modelKeys.Add("");
    foreach (var g in groups) { cmbModel.Items.Add((g.Key == "" ? "(no anchored model)" : g.Key) + "   (" + g.Count() + ")"); modelKeys.Add(g.Key == "" ? "\u0001none" : g.Key); }
    int def = 0;
    if (!string.IsNullOrEmpty(defaultModel)) { int k = modelKeys.IndexOf(defaultModel); if (k > 0) def = k; }
    cmbModel.SelectedIndex = def;
    fullItems = cmbModel.Items.Cast<object>().Select(o => o.ToString()).ToList(); fullKeys = modelKeys.ToList(); fullIndex = def;
    anyModel = anyModel || H.Cams.Any(c => c.Zone != "" || c.Room != "");
    cmbModel.Enabled = anyModel;
    txtMS.Enabled = anyModel;
    string savedPat = ReadPref();
    if (savedPat != "" && BuildSearch(savedPat)) { modelPat = savedPat; txtMS.Text = savedPat; }
    else ShowTagsHint();
    cmbSub.SelectedIndex = 0; cmbSubSt.SelectedIndex = 0;
    if (isClash) cmbTest.SelectedIndexChanged += (s, e) => { if (!syncing) ApplyFilter(true); };
    ApplyFilter(false);

    btnNext.Click += (s, e) => Go("Next", 0);
    btnPrev.Click += (s, e) => Go("Prev", 0);
    btnSnap.Click += (s, e) => Go("Snapshot", 0);
    btnIso.Click += (s, e) => Go("Isolate", 0);
    btnDup.Click += (s, e) => Go("Duplicate", 0);
    btnOpen.Click += (s, e) => OpenFolder();
    btnWeb.Click += (s, e) => OpenWeb();
    rbBefore.CheckedChanged += (s, e) => ShowPicture();
    rbAfter.CheckedChanged += (s, e) => ShowPicture();
    pic.Click += (s, e) => { var p = CurrentPicturePath(); if (p != null && System.IO.File.Exists(p)) Start(p, null); };
    // any way the model changes (click, keyboard, autocomplete) re-applies the filter and jumps to that model's first item
    cmbModel.SelectedIndexChanged += (s, e) => {
      if (syncing || cmbModel.SelectedIndex < 0) return;
      if (modelPat != "") {
        string k = searchKeys[cmbModel.SelectedIndex];
        if (k == "?") return;
        foreach (var t in tagKeys.ToList()) tagOn[t] = k == "*" ? t != "U" : t == k;
        RefreshTags();
      } else fullIndex = cmbModel.SelectedIndex;
      if (CurrentKey() != appliedKey) ApplyFilter(true);
    };
    txtMS.TextChanged += (s, e) => { msTimer.Stop(); msTimer.Start(); };
    msTimer.Tick += (s, e) => { msTimer.Stop(); ApplyModelSearch(); };
    txtMS.KeyDown += (s, e) => { if (e.KeyCode == WF.Keys.Enter) { msTimer.Stop(); ApplyModelSearch(); e.Handled = true; e.SuppressKeyPress = true; } };
    cmb.SelectionChangeCommitted += (s, e) => { if (!syncing && cmb.SelectedIndex >= 0) Go("Goto", cmb.SelectedIndex + 1); };
    chk.CheckedChanged += (s, e) => { if (!syncing) Go("Refresh", 0); };
    num.ValueChanged += (s, e) => { if (!syncing) Go("Refresh", 0); };
    // sub-filter: name text or Done status
    cmbSub.SelectedIndexChanged += (s, e) => { txtSub.Visible = cmbSub.SelectedIndex == 1; cmbSubSt.Visible = cmbSub.SelectedIndex == 2; cmbStamp.Visible = cmbSub.SelectedIndex == 3; if (!syncing) SubFilterChanged(); };
    cmbStamp.SelectedIndexChanged += (s, e) => { if (!syncing) SubFilterChanged(); };
    cmbStamp.TextUpdate += (s, e) => { subTimer.Stop(); subTimer.Start(); };
    cmbSubSt.SelectedIndexChanged += (s, e) => { if (!syncing) SubFilterChanged(); };
    txtSub.TextChanged += (s, e) => { subTimer.Stop(); subTimer.Start(); };
    subTimer.Tick += (s, e) => { subTimer.Stop(); SubFilterChanged(); };
    // Done + comment, saved to navigator_status.json and the web navigator
    cmbSt.SelectedIndexChanged += (s, e) => {
      if (syncing || curN < 1 || cmbSt.SelectedIndex < 0) return;
      string st = StKeys[cmbSt.SelectedIndex];
      NavData.SetState(H.Cams[curN - 1], st, null);
      ShowDoneInfo(H.Cams[curN - 1]);
      if (st == "closed" && WF.MessageBox.Show(this, "Marked as Closed.\n\nSave the solved snapshot now (same camera as the VP view)?", "Viewpoint Navigator", WF.MessageBoxButtons.YesNo, WF.MessageBoxIcon.Question) == WF.DialogResult.Yes)
        Go("Snapshot", 0);
    };
    // comment: type, then Post (or Enter); Edit re-opens a posted comment. Text not posted is still kept when moving on.
    txtComment.TextChanged += (s, e) => { if (syncing || curN < 1 || !editingComment) return; pendingN = curN; };
    txtComment.KeyDown += (s, e) => { if (e.KeyCode == WF.Keys.Enter && !e.Shift && editingComment) { e.Handled = true; e.SuppressKeyPress = true; PostComment(); } };
    btnPost.Click += (s, e) => {
      if (editingComment) PostComment();
      else { SetCommentMode(true); txtComment.Focus(); txtComment.SelectionStart = txtComment.TextLength; }
    };
    tip.SetToolTip(btnPost, "Post the comment (saved to the web navigator and its report). After posting, Edit opens it again.");
    Activated += (s, e) => { FlushComment(); if (NavData.ReloadStates() && curN > 0) Sync(curN); };
    FormClosed += (s, e) => { FlushComment(); Instance = null; if (pic.Image != null) pic.Image.Dispose(); try { Ev.Dispose(); } catch (Exception) { } };
  }

  // "select by ID" button picture: small ID card with a pointer
  static SD.Bitmap IdIcon() {
    var b = new SD.Bitmap(22, 20);
    using (var g = SD.Graphics.FromImage(b)) {
      g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
      g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
      var green = SD.Color.FromArgb(46, 140, 70);
      using (var br = new SD.SolidBrush(SD.Color.FromArgb(225, 242, 228))) g.FillRectangle(br, 1, 2, 15, 12);
      using (var p = new SD.Pen(green)) g.DrawRectangle(p, 1, 2, 15, 12);
      using (var f = new SD.Font("Segoe UI", 6.5f, SD.FontStyle.Bold)) using (var tb = new SD.SolidBrush(green)) g.DrawString("ID", f, tb, 1.5f, 2.5f);
      var pts = new[] { new SD.PointF(12, 8), new SD.PointF(12, 18), new SD.PointF(14.5f, 15.5f), new SD.PointF(16.5f, 19.2f), new SD.PointF(18.2f, 18.4f), new SD.PointF(16.2f, 14.8f), new SD.PointF(19.6f, 14.6f) };
      g.FillPolygon(SD.Brushes.White, pts);
      g.DrawPolygon(SD.Pens.Black, pts);
    }
    return b;
  }

  // status colours, same as the web navigator and the Excel export: Open orange, Closed green, Need clarification red, Action by others light blue
  static readonly SD.Color[] StCol = { SD.Color.FromArgb(245, 158, 11), SD.Color.FromArgb(46, 158, 91), SD.Color.FromArgb(214, 69, 69), SD.Color.FromArgb(56, 163, 224) };

  void DrawStatusItem(object sender, WF.DrawItemEventArgs e) {
    var cb = (WF.ComboBox)sender;
    e.DrawBackground();
    if (e.Index < 0 || e.Index >= cb.Items.Count) return;
    var g = e.Graphics;
    g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
    int d = Math.Max(6, Math.Min(10, e.Bounds.Height - 6));
    var r = new SD.Rectangle(e.Bounds.Left + 4, e.Bounds.Top + (e.Bounds.Height - d) / 2, d, d);
    using (var br = new SD.SolidBrush(StCol[Math.Min(e.Index, StCol.Length - 1)])) g.FillEllipse(br, r);
    var fc = (e.State & WF.DrawItemState.Selected) != 0 ? SD.SystemColors.HighlightText : cb.ForeColor;
    WF.TextRenderer.DrawText(g, cb.Items[e.Index].ToString(), e.Font, new SD.Rectangle(r.Right + 6, e.Bounds.Top, e.Bounds.Width - d - 10, e.Bounds.Height), fc, WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.Left | WF.TextFormatFlags.EndEllipsis);
    e.DrawFocusRectangle();
  }

  void SetCommentMode(bool editing) {
    editingComment = editing;
    txtComment.ReadOnly = !editing;
    txtComment.BackColor = editing ? SD.SystemColors.Window : SD.Color.FromArgb(242, 242, 242);
    btnPost.Text = editing ? "Post" : "Edit";
  }

  void PostComment() {
    if (curN < 1) return;
    pendingN = curN;
    FlushComment();
    SetCommentMode(txtComment.Text.Trim() == "");
    ShowMessage(txtComment.Text.Trim() == "" ? "Comment cleared." : "Comment posted.");
  }

  void FlushComment() {
    if (pendingN < 1 || pendingN > H.Cams.Count) return;
    var c = H.Cams[pendingN - 1];
    pendingN = 0;
    var st = NavData.GetState(c);
    if ((st != null ? st.c : "") == txtComment.Text) return;
    NavData.SetState(c, null, txtComment.Text);
    ShowDoneInfo(c);
  }

  void ShowDoneInfo(VpCam c) {
    var st = NavData.GetState(c);
    lblDone.Text = st == null || string.IsNullOrEmpty(st.by) ? "" : "Action by " + st.by + ",  " + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(st.t).ToLocalTime().ToString("dd MMM yyyy HH:mm");
  }

  bool SubOk(VpCam c) {
    int m = cmbSub.SelectedIndex;
    if (m == 1) { string q = txtSub.Text.Trim(); return q == "" || (c.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0; }
    if (m == 3) {
      var want = cmbStamp.Text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => System.Text.RegularExpressions.Regex.Replace(x, @"\(\d+\)\s*$", "").Trim()).Where(x => x != "").ToList();
      return want.Count == 0 || want.Any(w => string.Equals(w, c.Stamp, StringComparison.OrdinalIgnoreCase));
    }
    if (m == 2 && srcStatus) return cmbSubSt.SelectedItem != null && string.Equals(c.Status ?? "", cmbSubSt.SelectedItem.ToString(), StringComparison.OrdinalIgnoreCase);
    if (m == 2) { string st = NavData.StatusOf(c); int k = cmbSubSt.SelectedIndex; return st == StKeys[Math.Max(0, k)]; }
    return true;
  }

  void SubFilterChanged() {
    ApplyFilter(false);
    if (H.Order.Count == 0) { lblCount.Text = "No " + sourceWord + " match the filter."; return; }
    if (!H.Order.Contains(curN)) { H.Cur = 0; Go("Goto", 1); } else Sync(curN);
  }

  void ShowHits() {
    if (!isClash || curN < 1) return;
    var c = H.Cams[curN - 1];
    bool cur = H.Cur == curN;
    for (int i = 0; i < 2; i++) {
      if (i >= c.Items.Count) { lblItem[i].Text = i == 0 ? "No Element ID in the report for this clash" : ""; lblItem[i].ForeColor = SD.Color.Gray; btnPick[i].Enabled = false; continue; }
      var it = c.Items[i];
      var h = cur && i < H.Hits.Count && H.Hits[i].Id == it.Value ? H.Hits[i] : null;
      lblItem[i].Text = (i + 1) + ". " + it.Key + "  #" + it.Value + (h != null ? "  -  " + h.Where : "");
      lblItem[i].ForeColor = h == null ? SD.Color.DimGray : h.Found ? SD.Color.SeaGreen : SD.Color.Firebrick;
      btnPick[i].Enabled = h != null && h.Found;
    }
  }

  int FindModelItem(string text) {
    text = (text ?? "").Trim();
    if (text == "") return 0;
    for (int i = 0; i < cmbModel.Items.Count; i++) if (cmbModel.Items[i].ToString().StartsWith(text, StringComparison.OrdinalIgnoreCase)) return i;
    for (int i = 0; i < cmbModel.Items.Count; i++) if (cmbModel.Items[i].ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return i;
    return -1;
  }

  string testsForModel = null;   // model key the Test list was last built for

  bool InModel(VpCam c0, string key) {
    var ms = c0.Models.Count > 0 ? c0.Models : new List<string> { c0.Model ?? "" };
    if (key.StartsWith("\u0003")) { string g; return hitGroup.TryGetValue(c0.Index, out g) && tagOn.ContainsKey(g) && tagOn[g]; }
    return key == "" || (key == "\u0001none" && ms.All(x => x == "")) || ms.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
  }

  // Test list = only the tests that have clashes of the chosen model, counted for that model
  void RebuildTests(string key) {
    testsForModel = key;
    string keep = cmbTest.SelectedIndex >= 0 && cmbTest.SelectedIndex < testKeys.Count ? testKeys[cmbTest.SelectedIndex] : null;
    var inM = H.Cams.Where(c => InModel(c, key)).ToList();
    syncing = true;
    try {
      cmbTest.BeginUpdate();
      cmbTest.Items.Clear(); testKeys.Clear();
      int nDef = inM.Count(c => c.DefaultTest);
      if (nDef > 0) { cmbTest.Items.Add("New / Active (" + nDef + ")"); testKeys.Add("\u0002open"); }
      cmbTest.Items.Add("All tests (" + inM.Count + ")"); testKeys.Add("");
      foreach (var g in inM.GroupBy(c => c.Test)) { cmbTest.Items.Add(g.Key + "   (" + g.Count() + ")"); testKeys.Add(g.Key); }
      int k = keep == null ? -1 : testKeys.IndexOf(keep);
      cmbTest.SelectedIndex = k >= 0 ? k : 0;
      cmbTest.EndUpdate();
    } finally { syncing = false; }
  }

  string CurrentKey() { if (modelPat != "") return "\u0003" + modelPat + "|" + string.Join(",", tagKeys.Where(t => tagOn[t])); return (cmbModel.SelectedIndex >= 0 && cmbModel.SelectedIndex < modelKeys.Count) ? modelKeys[cmbModel.SelectedIndex] : ""; }

  // returns true if the model filter changed (and jumped to its first item)
  bool EnsureFilter() {
    if (CurrentKey() != appliedKey) { ApplyFilter(true); return true; }
    return false;
  }

  int PatternModels(string pat) {
    return H.Cams.SelectMany(c => c.Models.Count > 0 ? c.Models : new List<string> { c.Model ?? "" }).Distinct(StringComparer.OrdinalIgnoreCase).Count(m => NavHandler.PatternMatch(m, pat));
  }

  void ApplyModelSearch() {
    string q = txtMS.Text.Trim();
    if (q == modelPat) { txtMS.BackColor = SD.SystemColors.Window; return; }
    if (q != "" && !BuildSearch(q)) { txtMS.BackColor = SD.Color.FromArgb(255, 228, 228); ShowError("Nothing matches \"" + q + "\" (anchor model, zone or room)."); return; }
    txtMS.BackColor = SD.SystemColors.Window;
    modelPat = q;
    if (q == "") {
      hitGroup.Clear(); tagKeys.Clear(); tagOn.Clear(); searchKeys.Clear();
      syncing = true;
      try { cmbModel.BeginUpdate(); cmbModel.Items.Clear(); foreach (var it in fullItems) cmbModel.Items.Add(it); modelKeys = fullKeys.ToList(); cmbModel.SelectedIndex = Math.Min(fullIndex, cmbModel.Items.Count - 1); cmbModel.EndUpdate(); } finally { syncing = false; }
      ShowTagsHint();
    }
    SavePref();
    if (CurrentKey() != appliedKey) {
      ApplyFilter(true);
      if (q != "") ShowMessage("Code \"" + q + "\": " + H.Order.Count + " " + sourceWord + " shown." + (tagOn.ContainsKey("U") && !tagOn["U"] ? " Not located: " + hitGroup.Count(kv => kv.Value == "U") + " (click the orange tag to add them)." : ""));
    }
  }

  static string ShortModel(string m) {
    var f = System.IO.Path.GetFileNameWithoutExtension(m ?? "").Split('-');
    return f.Length >= 8 ? f[2] + "-" + f[3] + "-" + f[6] + (f[7].TrimStart('0') == "1" ? "" : "-" + f[7]) : m;
  }

  string TagText(string k, int n) {
    if (k == "X") return "Other anchor  (" + n + ")";
    if (k == "Z") return "Zone  (" + n + ")";
    if (k == "R") return "Room  (" + n + ")";
    if (k == "U") return "Not located  (" + n + ")";
    return ShortModel(k.Substring(2)) + "  (" + n + ")";
  }

  // groups the search finds; false when nothing (apart from 'not located') matches
  bool BuildSearch(string q) {
    var map = new Dictionary<int, string>();
    foreach (var c in H.Cams) { var g = NavHandler.SearchGroup(c, q); if (g != null) map[c.Index] = g; }
    if (!map.Values.Any(g => g != "U")) return false;
    hitGroup = map;
    var keys = map.Values.Distinct().Where(g => g.StartsWith("M:")).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
    foreach (var g in new[] { "X", "Z", "R", "U" }) if (map.ContainsValue(g)) keys.Add(g);
    var old = tagOn;
    tagKeys = keys;
    tagOn = keys.ToDictionary(k => k, k => old.ContainsKey(k) ? old[k] : k != "U");
    tagPanel.SuspendLayout();
    tagPanel.Controls.Clear();
    var tip = new WF.ToolTip();
    foreach (var k in keys) {
      int n = map.Count(kv => kv.Value == k);
      var cb = new WF.CheckBox { Appearance = WF.Appearance.Button, AutoSize = true, Text = TagText(k, n), Tag = k, FlatStyle = WF.FlatStyle.Flat, Font = new SD.Font("Segoe UI", 8f), Margin = new WF.Padding(2, 2, 2, 1), Padding = new WF.Padding(0), MinimumSize = new SD.Size(0, 24), Cursor = WF.Cursors.Hand };
      string kk = k;
      string tipText = k.StartsWith("M:") ? k.Substring(2) : string.Join("\n", H.Cams.Where(c => map.ContainsKey(c.Index) && map[c.Index] == kk).Select(c => kk == "Z" ? c.Zone : kk == "R" ? c.Room : kk == "X" ? ShortModel(c.Model) + ": " + (c.Zone + " " + c.Room).Trim() : "No anchor model / zone code / room: " + c.Name).Distinct().Take(25));
      tip.SetToolTip(cb, tipText + "\nClick to hide / show.");
      cb.CheckedChanged += (s, e) => { if (syncing) return; var b = (WF.CheckBox)s; tagOn[(string)b.Tag] = b.Checked; RefreshTags(); if (CurrentKey() != appliedKey) ApplyFilter(true); };
      tagPanel.Controls.Add(cb);
    }
    tagPanel.ResumeLayout();
    syncing = true;
    try {
      cmbModel.BeginUpdate(); cmbModel.Items.Clear(); searchKeys.Clear();
      cmbModel.Items.Add("All search results  (" + map.Count(kv => kv.Value != "U") + ")"); searchKeys.Add("*");
      foreach (var k in keys) { int n = map.Count(kv => kv.Value == k); cmbModel.Items.Add(k.StartsWith("M:") ? k.Substring(2) + "   (" + n + ")" : k == "X" ? "Anchored to another model - zone / room match  (" + n + ")" : k == "Z" ? "No anchor - zone matches  (" + n + ")" : k == "R" ? "No anchor - room matches  (" + n + ")" : "Not located  (" + n + ")"); searchKeys.Add(k); }
      cmbModel.Items.Add("Tags selected below"); searchKeys.Add("?");
      cmbModel.EndUpdate();
    } finally { syncing = false; }
    RefreshTags();
    return true;
  }

  // tag colours + the model list entry that matches the tag selection
  void RefreshTags() {
    syncing = true;
    try {
      foreach (WF.CheckBox cb in tagPanel.Controls.OfType<WF.CheckBox>()) {
        string k = (string)cb.Tag; bool on = tagOn.ContainsKey(k) && tagOn[k];
        cb.Checked = on;
        var col = k == "U" ? SD.Color.FromArgb(214, 120, 30) : k == "Z" || k == "R" || k == "X" ? SD.Color.FromArgb(120, 90, 180) : SD.Color.FromArgb(0, 120, 212);
        cb.BackColor = on ? col : SD.Color.White; cb.ForeColor = on ? SD.Color.White : col; cb.FlatAppearance.BorderColor = col; cb.FlatAppearance.CheckedBackColor = col;
      }
      if (searchKeys.Count > 0) {
        var onKeys = tagKeys.Where(t => tagOn[t]).ToList();
        int idx = onKeys.Count == 1 ? searchKeys.IndexOf(onKeys[0]) : (onKeys.Count == tagKeys.Count(t => t != "U") && !onKeys.Contains("U")) ? 0 : searchKeys.IndexOf("?");
        if (idx >= 0 && cmbModel.SelectedIndex != idx) cmbModel.SelectedIndex = idx;
      }
    } finally { syncing = false; }
  }

  void ShowTagsHint() {
    tagPanel.Controls.Clear();
    tagPanel.Controls.Add(new WF.Label { AutoSize = true, ForeColor = SD.Color.Gray, Font = new SD.Font("Segoe UI", 8f), Margin = new WF.Padding(3, 6, 3, 3), Text = anyModel ? "Type a building / volume code above (e.g. D0, 33, 03306):\nmatching models, zones and rooms show here as tags." : "No model names in this source." });
  }

  string PrefFile() { return System.IO.Path.Combine(NavData.AppDir, "model_search_" + (isClash ? "clash" : H.Source.StartsWith("Revizto") ? "revizto" : "viewpoints") + ".txt"); }
  string ReadPref() { try { var p = PrefFile(); return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p).Trim() : ""; } catch (Exception) { return ""; } }
  void SavePref() { try { System.IO.Directory.CreateDirectory(NavData.AppDir); System.IO.File.WriteAllText(PrefFile(), modelPat); } catch (Exception) { } }

  // rebuild the Order (filtered list) and the item drop-down; optionally jump to the first item of the new filter
  void ApplyFilter(bool go) {
    string key = CurrentKey();
    appliedKey = key;
    H.ModelPattern = modelPat != "" ? modelPat : null;
    H.SearchHit = modelPat != "" ? new HashSet<int>(hitGroup.Where(kv => tagOn.ContainsKey(kv.Value) && tagOn[kv.Value]).Select(kv => kv.Key)) : new HashSet<int>();
    H.Order = new List<int>();
    if (isClash && testsForModel != key) RebuildTests(key);
    string tkey = (isClash && cmbTest.SelectedIndex >= 0) ? testKeys[cmbTest.SelectedIndex] : "";
    for (int i = 0; i < H.Cams.Count; i++) {
      var c0 = H.Cams[i];
      bool okModel = InModel(c0, key);
      bool okTest = tkey == "" || (tkey == "\u0002open" ? c0.DefaultTest : c0.Test == tkey);
      if (okModel && okTest && SubOk(c0)) H.Order.Add(i + 1);
    }
    syncing = true;
    try {
      cmb.BeginUpdate();
      cmb.Items.Clear();
      for (int p = 0; p < H.Order.Count; p++) { var c = H.Cams[H.Order[p] - 1]; cmb.Items.Add((p + 1).ToString("00") + "   " + (c.IsClash ? c.Test + " · " : "") + c.Name); }
      cmb.EndUpdate();
    } finally { syncing = false; }
    if (go && H.Order.Count > 0) { H.Cur = 0; Go("Goto", 1); }
  }

  void Go(string action, int idx) {
    if (action != "Goto" && action != "Snapshot" && EnsureFilter()) return; // model changed: already jumped to its first item
    H.Action = action; H.GoTo = idx;
    H.UseBox = chk.Checked; H.BoxM = (double)num.Value;
    Ev.Raise();
  }

  protected override bool ProcessCmdKey(ref WF.Message msg, WF.Keys keyData) {
    if ((ActiveControl is WF.TextBoxBase) && (keyData == WF.Keys.Right || keyData == WF.Keys.Left || keyData == WF.Keys.PageDown || keyData == WF.Keys.PageUp)) return base.ProcessCmdKey(ref msg, keyData);
    if (keyData == WF.Keys.Right || keyData == WF.Keys.PageDown) { Go("Next", 0); return true; }
    if (keyData == WF.Keys.Left || keyData == WF.Keys.PageUp) { Go("Prev", 0); return true; }
    if (keyData == (WF.Keys.Control | WF.Keys.S)) { Go("Snapshot", 0); return true; }
    return base.ProcessCmdKey(ref msg, keyData);
  }

  string CurrentPicturePath() {
    if (curN < 1 || curN > H.Cams.Count) return null;
    var c = H.Cams[curN - 1];
    return rbAfter.Checked ? NavData.AfterPath(H.PicDir, c) : NavData.BeforePath(H.PicDir, c);
  }

  public void ShowPicture() {
    var old = pic.Image; pic.Image = null; if (old != null) old.Dispose();
    var p = CurrentPicturePath();
    if (p == null) return;
    var img = NavData.LoadImageNoLock(p);
    pic.Image = img;
    lblPic.Text = img != null ? System.IO.Path.GetFileName(p) + "   (" + img.Width + " x " + img.Height + ")" : (rbAfter.Checked ? "No solved snapshot yet: " : "Picture not found (yet): ") + System.IO.Path.GetFileName(p);
  }

  // called from the download thread when a picture has arrived
  public void PictureArrived(int index, int done, int total) {
    if (IsDisposed) return;
    Text = "Viewpoint Navigator - " + H.Source + (done < total ? "  (downloading pictures " + done + "/" + total + ")" : "");
    if (index == curN && !rbAfter.Checked) ShowPicture();
  }

  public void Sync(int n) {
    if (pendingN > 0 && pendingN != n) FlushComment();
    syncing = true;
    try {
      if (n != curN) rbBefore.Checked = true;   // new item: show the source picture first
      curN = n;
      var c = H.Cams[n - 1];
      lblName.Text = c.Name;
      lblInfo.ForeColor = SD.Color.DimGray;
      bool hasAfter = false;
      try { hasAfter = System.IO.File.Exists(NavData.AfterPath(H.PicDir, c)); } catch (Exception) { }
      int pos = H.Order.IndexOf(n);
      lblCount.Text = (pos >= 0 ? (pos + 1) + " of " + H.Order.Count : "?") + "  |  " + c.Img + (hasAfter ? "  |  solved snapshot saved" : "");
      if (pos >= 0 && cmb.SelectedIndex != pos) cmb.SelectedIndex = pos;
      if (c.HasPlane) {
        double dist = c.PlaneN.DotProduct(c.Target - c.PlanePt) * FT;
        string kind = Math.Abs(c.PlaneN.Z) > 0.9 ? "horizontal (top) cut" : "vertical cut";
        lblInfo.Text = "Navisworks section plane: " + kind + "; focus point is " + dist.ToString("0.00") + " m " + (dist >= 0 ? "inside" : "outside") + " the kept side.";
      } else if (!string.IsNullOrEmpty(c.Note)) lblInfo.Text = c.Note;
      else lblInfo.Text = "No section plane was saved for this viewpoint.";
      var st = NavData.GetState(c);
      cmbSt.SelectedIndex = Math.Max(0, Array.IndexOf(StKeys, NavData.StatusOf(c)));
      if (pendingN != n) { txtComment.Text = st != null ? st.c : ""; SetCommentMode(txtComment.Text.Trim() == ""); }
      ShowDoneInfo(c);
      ShowHits();
      btnIso.Text = H.Isolated ? "Restore original view" : "Isolate in 3D view";
      btnIso.BackColor = H.Isolated ? SD.Color.FromArgb(255, 236, 179) : SD.Color.Empty; btnIso.UseVisualStyleBackColor = !H.Isolated;
      btnIso.Enabled = H.Isolated || (H.Cur == n && H.Hits.Any(x => x.Found));
      ShowPicture();
    } finally { syncing = false; }
  }

  public void ShowSelection(int found, int total) {
    lblInfo.Text = (found > 0 ? "Found " + found + " of " + total + " element(s) by Element ID. " : "Element(s) not found in this model or its links. ") + lblInfo.Text;
  }

  public void ShowTime(long ms) { lblCount.Text += "  |  " + (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s"; }

  public void ShowMessage(string m) { lblInfo.ForeColor = SD.Color.DimGray; lblInfo.Text = m; }

  public void ShowError(string m) { lblInfo.ForeColor = SD.Color.Firebrick; lblInfo.Text = "Error: " + m; }

  public void ShowOtherModel(string model, string openTitle) {
    lblInfo.ForeColor = SD.Color.Firebrick;
    lblInfo.Text = "Preview only: this " + (isClash ? "clash" : "issue") + " is from another model (" + model + "). Open that model, or type part of its name (e.g. the building code) in Search to open it here.";
  }

  public void ShowSaved(string path) {
    lastSaved = path;
    lblInfo.Text = "Saved " + System.IO.Path.GetFileName(path) + " to " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) + ".";
    rbAfter.Checked = true; ShowPicture();
  }

  void Start(string file, string args) {
    try {
      if (args == null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
      else System.Diagnostics.Process.Start(file, args);
    } catch (Exception ex) { ShowError(ex.Message); }
  }

  void OpenWeb() {
    if (!string.IsNullOrEmpty(H.HtmlPath) && System.IO.File.Exists(H.HtmlPath)) Start(H.HtmlPath, null);
    else ShowError("The web navigator was not created" + (string.IsNullOrEmpty(H.HtmlError) ? "." : ": " + H.HtmlError));
  }

  void OpenFolder() {
    if (!string.IsNullOrEmpty(lastSaved) && System.IO.File.Exists(lastSaved)) Start("explorer.exe", "/select,\"" + lastSaved + "\"");
    else if (!string.IsNullOrEmpty(H.PicDir) && System.IO.Directory.Exists(H.PicDir)) Start("explorer.exe", "\"" + H.PicDir + "\"");
    else ShowError("Picture folder not found: " + H.PicDir);
  }
}

// "Which kind of data?" dialog shown when the routine starts
public class SourceDialog : WF.Form {
  public string Choice = null;
  static readonly string[] Keys = { "clash", "viewpoints", "revizto" };
  static readonly string[] Titles = { "Navisworks clash test  (clash report, HTML)", "Navisworks viewpoints  (viewpoints XML + viewpoints report)", "Revizto issues  (Excel export)" };
  static readonly string[] Guides = {
    "1. Create a new empty folder, e.g. ...\\Clash 2026-11-23\\\n" +
    "2. Navisworks: Clash Detective > Report tab > Contents: tick Image, Clash Point, Status,\n" +
    "    Item ID (and Grid Location). Report Format: HTML (Tabular).\n" +
    "3. Write Report > save it into the new folder. Only the HTML is needed (no XML).\n" +
    "4. Click Next and pick that .html. Its pictures folder (..._files) and the\n" +
    "    navigator page (web report) are kept in the same folder.",
    "1. Create a new empty folder, e.g. ...\\VP 2026-11-23\\\n" +
    "2. Navisworks: Saved Viewpoints > select the viewpoints you need\n" +
    "    (delete the ones you don't need, then select all).\n" +
    "3. Right-click the selection > Export Viewpoints Report...  (.html + pictures) > save in the new folder.\n" +
    "4. Right-click the selection AGAIN > Export Viewpoints...  (.xml) > save in the same folder.\n" +
    "    Steps 3 and 4 are two separate menu entries, one below the other: run both.\n" +
    "5. Click Next and pick that folder. Viewpoint_Navigator.html (web report) is saved there too.",
    "1. Create a new empty folder, e.g. ...\\Revizto 2026-11-23\\\n" +
    "2. Revizto: Issue Tracker > Export issues > Save as Excel (.xlsx) into the new folder.\n" +
    "    Only the Excel is needed.\n" +
    "3. Click Next and pick that .xlsx. Snapshots download to a sub-folder with the Excel name;\n" +
    "    the navigator page <name>_Navigator.html (web report) is saved next to the Excel."
  };


  // small light-bulb picture for the "How to use" heading
  static SD.Bitmap LampIcon() {
    var b = new SD.Bitmap(20, 20);
    using (var g = SD.Graphics.FromImage(b)) {
      g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
      using (var glow = new SD.SolidBrush(SD.Color.FromArgb(70, 255, 200, 40))) g.FillEllipse(glow, 1, 0, 18, 16);
      using (var bulb = new SD.SolidBrush(SD.Color.FromArgb(255, 196, 36))) g.FillEllipse(bulb, 4, 2, 12, 11);
      using (var pen = new SD.Pen(SD.Color.FromArgb(200, 140, 0), 1f)) g.DrawEllipse(pen, 4, 2, 12, 11);
      using (var neck = new SD.SolidBrush(SD.Color.FromArgb(255, 196, 36))) g.FillRectangle(neck, 7.5f, 11, 5, 3);
      using (var basev = new SD.SolidBrush(SD.Color.FromArgb(120, 125, 135))) { g.FillRectangle(basev, 7, 14, 6, 2); g.FillRectangle(basev, 7.5f, 16.5f, 5, 1.5f); }
      using (var pen = new SD.Pen(SD.Color.White, 1f)) g.DrawArc(pen, 6, 4, 5, 5, 180, 90);
    }
    return b;
  }

  public SourceDialog(string preselect) {
    Text = "Viewpoint Navigator - what are you opening?";
    FormBorderStyle = WF.FormBorderStyle.FixedDialog; MinimizeBox = false; MaximizeBox = false;
    StartPosition = WF.FormStartPosition.CenterParent;
    ClientSize = new SD.Size(540, 378);
    Font = new SD.Font("Segoe UI", 9.5f);
    var lbl = new WF.Label { Left = 16, Top = 14, Width = 490, Height = 22, Text = "Choose the kind of data (point at one to see what to export):" };
    var lamp = new WF.PictureBox { Left = 16, Top = 146, Width = 20, Height = 20, Image = LampIcon() };
    var how = new WF.Label { Left = 38, Top = 147, Width = 300, Height = 20, Text = "How to use:", Font = new SD.Font("Segoe UI Semibold", 9.5f), ForeColor = SD.Color.FromArgb(40, 50, 60) };
    var guide = new WF.Label { Left = 16, Top = 170, Width = 508, Height = 146, BorderStyle = WF.BorderStyle.FixedSingle, BackColor = SD.Color.FromArgb(246, 248, 251), Padding = new WF.Padding(8, 6, 8, 6), Font = new SD.Font("Segoe UI", 8.75f), ForeColor = SD.Color.FromArgb(40, 50, 60) };
    var tip = new WF.ToolTip { AutoPopDelay = 30000, InitialDelay = 300 };
    var rb = new WF.RadioButton[3];
    int sel = Math.Max(0, Array.IndexOf(Keys, preselect ?? ""));
    for (int i = 0; i < 3; i++) {
      int k = i;
      rb[i] = new WF.RadioButton { Left = 24, Top = 44 + 30 * i, Width = 480, Height = 26, Text = Titles[i], Checked = i == sel };
      tip.SetToolTip(rb[i], Guides[i]);
      rb[i].MouseEnter += (s, e) => guide.Text = Guides[k];
      rb[i].MouseLeave += (s, e) => { for (int j = 0; j < 3; j++) if (rb[j].Checked) guide.Text = Guides[j]; };
      rb[i].CheckedChanged += (s, e) => { if (rb[k].Checked) guide.Text = Guides[k]; };
    }
    guide.Text = Guides[sel];
    var cancel = new WF.Button { Left = 336, Top = 332, Width = 90, Height = 32, Text = "Cancel", DialogResult = WF.DialogResult.Cancel };
    var ok = new WF.Button { Left = 434, Top = 332, Width = 90, Height = 32, Text = "Next", DialogResult = WF.DialogResult.OK, FlatStyle = WF.FlatStyle.Flat, BackColor = SD.Color.FromArgb(0, 120, 212), ForeColor = SD.Color.White, UseVisualStyleBackColor = false, Font = new SD.Font("Segoe UI Semibold", 9.5f) };
    ok.FlatAppearance.BorderColor = SD.Color.FromArgb(0, 95, 184);
    AcceptButton = ok; CancelButton = cancel;
    Controls.AddRange(new WF.Control[] { lbl, rb[0], rb[1], rb[2], lamp, how, guide, cancel, ok });
    Shown += (s, e) => ok.Focus();
    ok.Click += (s, e) => { for (int j = 0; j < 3; j++) if (rb[j].Checked) Choice = Keys[j]; };
  }
}

// small JSON reader (objects, arrays, strings, numbers, true/false/null) - works in every Revit version
public static class MiniJson {
  public static object Parse(string s) { int i = 0; return Val(s, ref i); }
  static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
  static object Val(string s, ref int i) {
    Ws(s, ref i);
    if (i >= s.Length) return null;
    char ch = s[i];
    if (ch == '{') {
      var d = new Dictionary<string, object>(); i++;
      while (true) { Ws(s, ref i); if (i < s.Length && s[i] == '}') { i++; return d; }
        string k = Str(s, ref i); Ws(s, ref i); i++; d[k] = Val(s, ref i); Ws(s, ref i);
        if (i < s.Length && s[i] == ',') i++; else { Ws(s, ref i); if (i < s.Length && s[i] == '}') i++; return d; } }
    }
    if (ch == '[') {
      var l = new List<object>(); i++;
      while (true) { Ws(s, ref i); if (i < s.Length && s[i] == ']') { i++; return l; }
        l.Add(Val(s, ref i)); Ws(s, ref i);
        if (i < s.Length && s[i] == ',') i++; else { Ws(s, ref i); if (i < s.Length && s[i] == ']') i++; return l; } }
    }
    if (ch == '"') return Str(s, ref i);
    if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
    if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
    if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
    int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
    double r; double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out r); return r;
  }
  static string Str(string s, ref int i) {
    var sb = new StringBuilder(); i++;
    while (i < s.Length && s[i] != '"') {
      if (s[i] == '\\' && i + 1 < s.Length) {
        i++; char e = s[i];
        if (e == 'n') sb.Append('\n'); else if (e == 't') sb.Append('\t'); else if (e == 'r') sb.Append('\r'); else if (e == 'b') sb.Append('\b'); else if (e == 'f') sb.Append('\f');
        else if (e == 'u' && i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
        else sb.Append(e);
      } else sb.Append(s[i]);
      i++;
    }
    i++; return sb.ToString();
  }
}

// Reads a Revizto "Export to Excel" workbook (sheet "Raw Data") into the navigator's camera list.
public static class ReviztoData {
  const double FT = 0.3048;
  const string NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
  const string NSR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
  static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

  class Sheet { public List<Dictionary<string, string>> Rows = new List<Dictionary<string, string>>(); public Dictionary<string, string> Links = new Dictionary<string, string>(); public List<string> Headers = new List<string>(); }

  static XmlDocument ReadXml(System.IO.Compression.ZipArchive z, string path) {
    var e = z.GetEntry(path); if (e == null) return null;
    var d = new XmlDocument(); using (var s = e.Open()) d.Load(s); return d;
  }
  static string ColOf(string cellRef) { int i = 0; while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++; return cellRef.Substring(0, i); }
  static int RowOf(string cellRef) { int i = 0; while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++; int r; int.TryParse(cellRef.Substring(i), out r); return r; }

  static Sheet ReadSheet(string xlsx, string wantedName) {
    using (var fs = new System.IO.FileStream(xlsx, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
    using (var z = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read)) {
      var wb = ReadXml(z, "xl/workbook.xml");
      var rels = ReadXml(z, "xl/_rels/workbook.xml.rels");
      string rid = null;
      foreach (XmlElement s in wb.GetElementsByTagName("sheet", NS))
        if (string.Equals(s.GetAttribute("name"), wantedName, StringComparison.OrdinalIgnoreCase)) rid = s.GetAttribute("id", NSR);
      if (rid == null) return null;
      string target = null;
      foreach (XmlElement r in rels.DocumentElement.ChildNodes.OfType<XmlElement>()) if (r.GetAttribute("Id") == rid) target = r.GetAttribute("Target");
      if (target == null) return null;
      target = target.TrimStart('/'); if (!target.StartsWith("xl/")) target = "xl/" + target;

      var shared = new List<string>();
      var ss = ReadXml(z, "xl/sharedStrings.xml");
      if (ss != null) foreach (XmlElement si in ss.GetElementsByTagName("si", NS)) shared.Add(string.Concat(si.GetElementsByTagName("t", NS).Cast<XmlElement>().Select(t => t.InnerText)));

      var sh = ReadXml(z, target);
      var res = new Sheet();
      var cellsByRow = new SortedDictionary<int, Dictionary<string, string>>();
      foreach (XmlElement c in sh.GetElementsByTagName("c", NS)) {
        string rf = c.GetAttribute("r"), t = c.GetAttribute("t"), val = null;
        var v = c.GetElementsByTagName("v", NS).Cast<XmlElement>().FirstOrDefault();
        if (t == "s" && v != null) { int k; if (int.TryParse(v.InnerText, out k) && k >= 0 && k < shared.Count) val = shared[k]; }
        else if (t == "inlineStr") val = string.Concat(c.GetElementsByTagName("t", NS).Cast<XmlElement>().Select(x => x.InnerText));
        else if (v != null) val = v.InnerText;
        var f = c.GetElementsByTagName("f", NS).Cast<XmlElement>().FirstOrDefault();
        if (f != null) { var m = System.Text.RegularExpressions.Regex.Match(f.InnerText, "HYPERLINK\\(\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase); if (m.Success) res.Links[rf] = m.Groups[1].Value; }
        int row = RowOf(rf);
        if (!cellsByRow.ContainsKey(row)) cellsByRow[row] = new Dictionary<string, string>();
        cellsByRow[row][ColOf(rf)] = val;
      }
      // hyperlinks stored as relationships
      string relPath = target.Substring(0, target.LastIndexOf('/')) + "/_rels/" + target.Substring(target.LastIndexOf('/') + 1) + ".rels";
      var srels = ReadXml(z, relPath);
      var relMap = new Dictionary<string, string>();
      if (srels != null) foreach (XmlElement r in srels.DocumentElement.ChildNodes.OfType<XmlElement>()) relMap[r.GetAttribute("Id")] = r.GetAttribute("Target");
      foreach (XmlElement h in sh.GetElementsByTagName("hyperlink", NS)) {
        string id = h.GetAttribute("id", NSR), rf = h.GetAttribute("ref");
        if (relMap.ContainsKey(id) && !string.IsNullOrEmpty(rf)) res.Links[rf.Split(':')[0]] = relMap[id];
      }
      if (cellsByRow.Count == 0) return res;
      int headerRow = cellsByRow.Keys.First();
      var colToHeader = cellsByRow[headerRow].Where(kv => !string.IsNullOrEmpty(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.Trim());
      res.Headers = colToHeader.Values.ToList();
      foreach (var kv in cellsByRow) {
        if (kv.Key == headerRow) continue;
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in kv.Value) if (colToHeader.ContainsKey(cell.Key)) d[colToHeader[cell.Key]] = cell.Value;
        foreach (var hc in colToHeader) { string link; if (res.Links.TryGetValue(hc.Key + kv.Key, out link)) d[hc.Value + "#link"] = link; }
        res.Rows.Add(d);
      }
      return res;
    }
  }

  public static bool IsReviztoWorkbook(string xlsx) {
    try { var s = ReadSheet(xlsx, "Raw Data"); return s != null && s.Headers.Contains("Camera") && s.Headers.Contains("GUID"); } catch (Exception) { return false; }
  }

  static string G(Dictionary<string, string> r, string k) { string v; return r.TryGetValue(k, out v) ? (v ?? "").Trim() : ""; }
  static double[] Triple(string s) {
    var m = System.Text.RegularExpressions.Regex.Matches(s ?? "", "-?\\d+(\\.\\d+)?([eE][-+]?\\d+)?");
    if (m.Count < 3) return null;
    return new double[] { double.Parse(m[0].Value, Inv), double.Parse(m[1].Value, Inv), double.Parse(m[2].Value, Inv) };
  }
  static double[] JsonVec(string json, string key) {
    var m = System.Text.RegularExpressions.Regex.Match(json ?? "", "\"" + key + "\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(-?[\\d.eE+-]+)\\s*,\\s*\"y\"\\s*:\\s*(-?[\\d.eE+-]+)\\s*,\\s*\"z\"\\s*:\\s*(-?[\\d.eE+-]+)");
    if (!m.Success) return null;
    return new double[] { double.Parse(m.Groups[1].Value, Inv), double.Parse(m.Groups[2].Value, Inv), double.Parse(m.Groups[3].Value, Inv) };
  }
  static double JsonNum(string json, string key, double def) {
    var m = System.Text.RegularExpressions.Regex.Match(json ?? "", "\"" + key + "\"\\s*:\\s*(-?[\\d.eE+-]+)");
    double r; return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, Inv, out r) ? r : def;
  }
  // Revizto camera axes: x = -East, y = Up, z = -North
  static double[] ToShared(double[] v) { return v == null ? null : new double[] { -v[0], -v[2], v[1] }; }

  public static string StatusTag(string s) {
    s = (s ?? "").ToLowerInvariant();
    if (s.Contains("solved") || s.Contains("closed") || s.Contains("resolved") || s.Contains("done")) return "closed";
    return "open";
  }

  public static List<VpCam> Load(string xlsx, Document doc, out string projectName) {
    var sheet = ReadSheet(xlsx, "Raw Data");
    if (sheet == null) throw new Exception("This workbook has no 'Raw Data' sheet. Export the issues from Revizto with Export to Excel.");
    projectName = System.IO.Path.GetFileNameWithoutExtension(xlsx);
    Transform toInt = NavData.SharedToInternal(doc);
    var list = new List<VpCam>();
    int idx = 0;
    foreach (var r in sheet.Rows) {
      string guid = G(r, "GUID"), id = G(r, "ID");
      if (string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(id)) continue;
      idx++;
      var c = new VpCam();
      c.Index = idx; c.Guid = string.IsNullOrEmpty(guid) ? "revizto-" + id : guid; c.Id = id;
      string title = G(r, "Title");
      c.Name = (string.IsNullOrEmpty(title) ? "Issue" : title) + (string.IsNullOrEmpty(id) ? "" : " (" + id + ")");
      c.Img = "revizto_" + (string.IsNullOrEmpty(id) ? idx.ToString() : id) + ".jpg";
      string url; c.Url = r.TryGetValue("Snapshot#link", out url) ? url : null;
      c.Model = G(r, "Anchor model name");
      string status = G(r, "Status");
      c.Status = status;   // Revizto status, shown in the Revizto status sub-filter
      c.St0 = StatusTag(status);   // report status for new issues: Solved / Closed -> Closed, Open / In progress -> Open
      c.Zone = G(r, "Zone");
      c.Room = System.Text.RegularExpressions.Regex.Replace(G(r, "Room") != "" ? G(r, "Room") : G(r, "Space"), "\\s+", " ").Trim();
      c.Stamp = G(r, "Stamp Abbreviation");
      var parts = new List<string>();
      if (status != "") parts.Add("Revizto status: " + status);
      if (G(r, "Priority") != "") parts.Add(G(r, "Priority"));
      if (G(r, "Assignee") != "") parts.Add("Assignee " + G(r, "Assignee"));
      if (G(r, "Stamp Abbreviation") != "") parts.Add("Stamp " + G(r, "Stamp Abbreviation"));
      if (G(r, "Level") != "") parts.Add("Level " + G(r, "Level"));
      if (G(r, "Grid Location") != "") parts.Add("Grid " + G(r, "Grid Location"));
      if (c.Model != "") parts.Add("Model " + c.Model); else parts.Add("No anchor model");
      if (c.Zone != "") parts.Add(c.Zone);
      if (c.Room != "") parts.Add("Room " + c.Room);
      c.Note = string.Join(" · ", parts);
      foreach (var s in G(r, "Selection").Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)) { int eid; if (int.TryParse(s.Trim(), out eid)) c.Select.Add(eid); }

      string cam = G(r, "Camera");
      var eye = Triple(G(r, "Camera Position (m)"));
      var pt = Triple(G(r, "Coordinates (m)"));
      var fwd = ToShared(JsonVec(cam, "forward"));
      var up = ToShared(JsonVec(cam, "up"));
      if (eye == null || fwd == null) continue; // no 3D camera (e.g. a sheet issue)
      c.RawPos = eye; c.RawDir = fwd;
      c.Eye = toInt.OfPoint(new XYZ(eye[0] / FT, eye[1] / FT, eye[2] / FT));
      c.Fwd = toInt.OfVector(new XYZ(fwd[0], fwd[1], fwd[2])).Normalize();
      var upv = up != null ? toInt.OfVector(new XYZ(up[0], up[1], up[2])) : XYZ.BasisZ;
      // make Up exactly perpendicular to Forward
      var right = c.Fwd.CrossProduct(upv);
      if (right.GetLength() < 1e-6) right = c.Fwd.CrossProduct(XYZ.BasisX);
      c.Up = right.Normalize().CrossProduct(c.Fwd).Normalize();
      if (pt != null) {
        c.HasPoint = true;
        c.Point = toInt.OfPoint(new XYZ(pt[0] / FT, pt[1] / FT, pt[2] / FT));
        c.FocalM = Math.Max(0.1, Math.Sqrt(Math.Pow(pt[0] - eye[0], 2) + Math.Pow(pt[1] - eye[1], 2) + Math.Pow(pt[2] - eye[2], 2)));
      } else c.FocalM = 2.0;
      c.Target = c.HasPoint ? c.Point : c.Eye + c.Fwd.Multiply(c.FocalM / FT);
      c.FovH = JsonNum(cam, "fov", 60.0) * Math.PI / 180.0;   // Revizto fov = vertical, degrees
      c.Aspect = 16.0 / 9.0;                                    // replaced by the snapshot's real shape once downloaded
      list.Add(c);
    }
    return list;
  }

  // Downloads one snapshot to <dir>\<Img> (JPEG). Returns true if the file exists afterwards.
  public static bool Download(VpCam c, string dir) {
    string dest = NavData.BeforePath(dir, c);
    if (System.IO.File.Exists(dest) && new System.IO.FileInfo(dest).Length > 0) return true;
    if (string.IsNullOrEmpty(c.Url)) return false;
    try {
      System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
      byte[] bytes;
      using (var wc = new System.Net.WebClient()) bytes = wc.DownloadData(c.Url);
      if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) { System.IO.File.WriteAllBytes(dest, bytes); return true; }
      using (var ms = new System.IO.MemoryStream(bytes)) using (var img = System.Drawing.Image.FromStream(ms)) img.Save(dest, System.Drawing.Imaging.ImageFormat.Jpeg);
      return true;
    } catch (Exception) { return false; }
  }

  // Revit file name without extension, local-copy suffix (_username) and "detached" markers, lower case
  public static string ModelKey(string name) {
    string s = System.IO.Path.GetFileName((name ?? "").Trim());
    s = System.Text.RegularExpressions.Regex.Replace(s, "\\.(rvt|nwc|nwd|ifc)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    s = System.Text.RegularExpressions.Regex.Replace(s, "_(detached|backup)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return s.ToLowerInvariant();
  }
  public static bool ModelMatches(string anchor, string docTitle) {
    string a = ModelKey(anchor), d = ModelKey(docTitle);
    if (a == "" || d == "") return false;
    return a == d || d.StartsWith(a + "_") || a.StartsWith(d + "_");
  }
}

// Reads a Navisworks Clash Detective HTML (tabular) report: one item per clash (group header rows are skipped).
public static class ClashData {
  const double FT = 0.3048;
  static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
  static readonly Regex RxRow = new Regex("<tr class=\"(clashGroupRow|childRow|childRowLast|contentRow)\">(.*?)</tr>", RegexOptions.Singleline | RegexOptions.Compiled);
  static readonly Regex RxCell = new Regex("<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.Compiled);
  static readonly Regex RxTag = new Regex("<[^>]+>", RegexOptions.Compiled);
  static readonly Regex RxWs = new Regex("\\s+", RegexOptions.Compiled);
  static readonly Regex RxSrc = new Regex("src\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  static readonly Regex RxPt = new Regex("x\\s*:\\s*(-?[\\d.]+)\\s*,\\s*y\\s*:\\s*(-?[\\d.]+)\\s*,\\s*z\\s*:\\s*(-?[\\d.]+)", RegexOptions.Compiled);
  static readonly Regex RxEid = new Regex("Element ID\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

  static string Text(string html) { return RxWs.Replace(System.Net.WebUtility.HtmlDecode(RxTag.Replace(html ?? "", " ")), " ").Trim(); }

  public static bool IsClashReport(string html) {
    try { using (var r = new System.IO.StreamReader(html)) { var b = new char[20000]; int n = r.Read(b, 0, b.Length); var s = new string(b, 0, n); return s.Contains("Clash Report") || s.Contains("testSummaryTable"); } }
    catch (Exception) { return false; }
  }

  // model file from a Navisworks selection path "File > X.nwd > MODEL.nwc > Category > ..."
  static string ModelOf(string path) {
    var segs = path.Split('>').Select(s => s.Trim()).Where(s => s != "").ToList();
    var m = segs.Skip(1).FirstOrDefault(s => Regex.IsMatch(s, "\\.(nwc|rvt|ifc|dwg|nwd)$", RegexOptions.IgnoreCase) && !s.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase));
    return m ?? (segs.Count > 1 ? segs[1] : "");
  }

  public static string StatusTag(string s) {
    s = (s ?? "").ToLowerInvariant();
    return (s.Contains("resolved") || s.Contains("approved")) ? "closed" : "open";
  }

  public static List<VpCam> Load(string htmlPath, Document doc, out string title, out string picDir, out bool hasStatus) {
    string s = System.IO.File.ReadAllText(htmlPath);
    string baseDir = System.IO.Path.GetDirectoryName(htmlPath);
    title = System.IO.Path.GetFileNameWithoutExtension(htmlPath);
    picDir = baseDir; hasStatus = false;
    Transform toInt = NavData.SharedToInternal(doc);
    var list = new List<VpCam>();
    var starts = new List<int>();
    int at = 0;
    while ((at = s.IndexOf("class=\"testSummaryTable\"", at, StringComparison.Ordinal)) >= 0) { starts.Add(at); at += 10; }
    bool picDirSet = false;
    int idx = 0;
    for (int t = 0; t < starts.Count; t++) {
      string part = s.Substring(starts[t], (t + 1 < starts.Count ? starts[t + 1] : s.Length) - starts[t]);
      // summary: header row (test name, Tolerance, Clashes, New, Active, Reviewed, Approved, Resolved, ...) + value row
      int sumEnd = part.IndexOf("</table>", StringComparison.Ordinal);
      string summary = sumEnd > 0 ? part.Substring(0, sumEnd) : "";
      var sumRows = Regex.Matches(summary, "<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline).Cast<Match>().Select(m => RxCell.Matches(m.Groups[1].Value).Cast<Match>().Select(c => Text(c.Groups[1].Value)).ToList()).ToList();
      string test = sumRows.Count > 0 && sumRows[0].Count > 0 ? sumRows[0][0] : "Test " + (t + 1);
      int newActive = 0;
      if (sumRows.Count > 1) {
        var hdr = sumRows[0]; var val = sumRows[1];
        for (int k = 1; k < hdr.Count; k++) {
          if (hdr[k] == "New" || hdr[k] == "Active") { int n; if (k - 1 < val.Count && int.TryParse(val[k - 1], out n)) newActive += n; }
        }
      }
      int main = part.IndexOf("class=\"mainTable\"", StringComparison.Ordinal);
      if (main < 0) continue;
      string table = part.Substring(main);
      // column names from the header row with generalHeader / item headers, in order
      var names = Regex.Matches(table.Substring(0, Math.Min(table.Length, 6000)), "<td[^>]*class=\"(generalHeader|item1Header|item2Header)\"[^>]*>(.*?)</td>", RegexOptions.Singleline)
        .Cast<Match>().Select(m => Text(m.Groups[2].Value)).Where(n => n != "").ToList();
      int cGroup = names.IndexOf("Clash Group"), cName = names.IndexOf("Clash Name"), cGrid = names.IndexOf("Grid Location"),
          cDesc = names.IndexOf("Description"), cDate = names.IndexOf("Date Found"), cPt = names.IndexOf("Clash Point"), cStatus = names.IndexOf("Status");
      int cId1 = names.IndexOf("Item ID"), cId2 = cId1 >= 0 ? names.IndexOf("Item ID", cId1 + 1) : -1;
      if (cStatus >= 0) hasStatus = true;

      foreach (Match rm in RxRow.Matches(table)) {
        if (rm.Groups[1].Value == "clashGroupRow") continue;
        var raw = RxCell.Matches(rm.Groups[2].Value).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        Func<int, string> T = i => (i >= 0 && i < raw.Count) ? Text(raw[i]) : "";
        var pm = RxPt.Match(T(cPt));
        if (!pm.Success) continue;
        idx++;
        var c = new VpCam();
        c.Index = idx; c.Test = test; c.IsClash = true;
        c.Name = T(cName); if (c.Name == "") c.Name = "Clash " + idx;
        c.Guid = test + "|" + c.Name;
        var src = raw.Count > 0 ? RxSrc.Match(raw[0]) : Match.Empty;
        string rel = src.Success ? src.Groups[1].Value.Trim() : "";
        c.Img = rel != "" ? System.IO.Path.GetFileName(rel.Replace('/', '\\')) : "clash_" + idx + ".jpg";
        if (!picDirSet && rel != "") { picDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, System.IO.Path.GetDirectoryName(rel.Replace('/', '\\')) ?? "")); picDirSet = true; }
        string status = T(cStatus);
        c.St0 = "";   // report status starts Open; the Navisworks status is kept separately in Status
        c.Status = status;
        c.DefaultTest = cStatus >= 0 ? (status == "New" || status == "Active") : newActive > 0;
        // both items: Element ID + model
        var parts = new List<string> { "Test " + test };
        if (T(cGroup) != "") parts.Add("Group " + T(cGroup));
        if (status != "") parts.Add("Clash status: " + status);
        if (T(cGrid) != "") parts.Add("Grid " + T(cGrid));
        if (T(cDesc) != "") parts.Add(T(cDesc));
        foreach (int ci in new[] { cId1, cId2 }) {
          if (ci < 0) continue;
          var em = RxEid.Match(T(ci));
          string model = ModelOf(T(ci + 1));
          if (model != "" && !c.Models.Contains(model)) c.Models.Add(model);
          if (em.Success) { int id; if (int.TryParse(em.Groups[1].Value, out id)) { c.Items.Add(new KeyValuePair<string, int>(model, id)); parts.Add(model + " #" + id); } }
        }
        c.Model = string.Join(" / ", c.Models);
        c.Note = string.Join(" · ", parts);
        double x = double.Parse(pm.Groups[1].Value, Inv), y = double.Parse(pm.Groups[2].Value, Inv), z = double.Parse(pm.Groups[3].Value, Inv);
        c.RawPos = new double[] { x, y, z };
        c.HasPoint = true;
        c.Point = toInt.OfPoint(new XYZ(x / FT, y / FT, z / FT));
        // the real camera is set when navigating (depends on the elements found in the open model)
        c.Target = c.Point; c.Eye = c.Point + new XYZ(5, -5, 4.5); c.Fwd = (c.Point - c.Eye).Normalize();
        c.Up = c.Fwd.CrossProduct(XYZ.BasisZ).CrossProduct(c.Fwd).Normalize();
        c.FovH = 50.0 * Math.PI / 180.0; c.Aspect = 1.0; c.FocalM = 3.0;
        list.Add(c);
      }
    }
    return list;
  }
}
