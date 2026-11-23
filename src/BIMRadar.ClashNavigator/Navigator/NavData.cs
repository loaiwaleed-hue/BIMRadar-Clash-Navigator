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
public static class NavData {
  const double FT = 0.3048;
  static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
  // T4: moves to %AppData%\BIMRadar\ClashNavigator
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
  public static ElementId Eid(long v) { return Compat.Eid(v); }

  public static SD.Image LoadImageNoLock(string path) {
    if (!System.IO.File.Exists(path)) return null;
    try { using (var ms = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(path))) using (var img = SD.Image.FromStream(ms)) return new SD.Bitmap(img); }
    catch (Exception) { return null; }
  }
}
}
