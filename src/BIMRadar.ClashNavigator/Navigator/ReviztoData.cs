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
}
