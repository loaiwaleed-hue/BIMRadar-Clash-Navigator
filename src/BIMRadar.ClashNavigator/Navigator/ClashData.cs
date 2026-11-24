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
    try { using (var r = new System.IO.StreamReader(html)) { var b = new char[20000]; int n = r.Read(b, 0, b.Length); var s = new string(b, 0, n); return s.Contains("Clash Report") || s.Contains("testSummaryTable") || s.Contains("class=\"mainTable\""); } }   // #91
    catch (Exception) { return false; }
  }

  // model file from a Navisworks selection path "File > X.nwd > MODEL.nwc > Category > ..."
  // #90: the LAST model-file segment (innermost file: 'File > HOST.nwc > LINK.rvt > ...' gives LINK.rvt)
  internal static string ModelOf(string path) {
    var segs = path.Split('>').Select(s => s.Trim()).Where(s => s != "").ToList();
    var m = segs.Skip(1).LastOrDefault(s => Regex.IsMatch(s, "\\.(nwc|rvt|ifc|dwg|nwd)$", RegexOptions.IgnoreCase) && !s.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase));
    return m ?? (segs.Count > 1 ? segs[1] : "");
  }

  // #23: whole-word, case-insensitive match of a clash name inside a viewpoint name ("Clash1" matches "Test A - Clash1", not "Clash10")
  internal static bool NameMatches(string clash, string vp) {
    clash = (clash ?? "").Trim(); vp = (vp ?? "").Trim();
    if (clash == "" || vp == "") return false;
    if (string.Equals(clash, vp, StringComparison.OrdinalIgnoreCase)) return true;
    return Regex.IsMatch(vp, "(?<![\\p{L}\\p{N}])" + Regex.Escape(clash) + "(?![\\p{L}\\p{N}])", RegexOptions.IgnoreCase);
  }

  // #23: when a Navisworks viewpoints XML (newest one) sits in the same folder as the clash HTML, give matching clashes its exact camera.
  // Returns the number of clashes that got a camera. Ranking: viewpoint folder equals the clash's Test (+2), exact name (+1).
  public static int ApplyNavCams(List<VpCam> clashes, string htmlPath, Document doc) {
    try {
      string dir = System.IO.Path.GetDirectoryName(htmlPath);
      var xml = new System.IO.DirectoryInfo(dir).GetFiles("*.xml").OrderByDescending(f => f.LastWriteTime).FirstOrDefault(f => NavData.IsViewpointXml(f.FullName));
      if (xml == null) return 0;
      string nwd, folder;
      var vps = NavData.Load(xml.FullName, doc, out nwd, out folder);
      if (vps.Count == 0) return 0;
      int n = 0;
      foreach (var c in clashes) {
        VpCam best = null; int bestScore = -1;
        foreach (var vp in vps) {
          if (!NameMatches(c.Name, vp.Name)) continue;
          int score = (string.Equals(vp.VpFolder, c.Test, StringComparison.OrdinalIgnoreCase) ? 2 : 0) + (string.Equals(vp.Name.Trim(), c.Name.Trim(), StringComparison.OrdinalIgnoreCase) ? 1 : 0);
          if (score > bestScore) { best = vp; bestScore = score; }
        }
        if (best == null || best.Eye == null || best.Fwd == null || best.Up == null) continue;
        c.Eye = best.Eye; c.Fwd = best.Fwd; c.Up = best.Up;
        if (best.FovH > 0.01 && best.FovH < 3.0) c.FovH = best.FovH;
        c.HasNavCam = true; n++;
      }
      return n;
    } catch (Exception) { return 0; }
  }

  public static string StatusTag(string s) {
    s = (s ?? "").ToLowerInvariant();
    return (s.Contains("resolved") || s.Contains("approved")) ? "closed" : "open";
  }

  // #94: what the report lacks, in Navisworks' own option names, so the user knows exactly what to tick
  public static readonly List<string> Missing = new List<string>();
  public static readonly List<string> Warnings = new List<string>();
  public static List<VpCam> Load(string htmlPath, Document doc, out string title, out string picDir, out bool hasStatus) {
    Missing.Clear(); Warnings.Clear();
    string s = System.IO.File.ReadAllText(htmlPath);
    string baseDir = System.IO.Path.GetDirectoryName(htmlPath);
    title = System.IO.Path.GetFileNameWithoutExtension(htmlPath);
    picDir = baseDir; hasStatus = false;
    Transform toInt = NavData.SharedToInternal(doc);
    var list = new List<VpCam>();
    var starts = new List<int>();
    int at = 0;
    while ((at = s.IndexOf("class=\"testSummaryTable\"", at, StringComparison.Ordinal)) >= 0) { starts.Add(at); at += 10; }
    // #91: report written without "Summary" has no testSummaryTable: read each mainTable as its own test
    bool noSummary = starts.Count == 0;
    if (noSummary) { at = 0; while ((at = s.IndexOf("class=\"mainTable\"", at, StringComparison.Ordinal)) >= 0) { starts.Add(at); at += 10; } }
    bool picDirSet = false;
    int idx = 0;
    for (int t = 0; t < starts.Count; t++) {
      string part = s.Substring(starts[t], (t + 1 < starts.Count ? starts[t + 1] : s.Length) - starts[t]);
      // summary: header row (test name, Tolerance, Clashes, New, Active, Reviewed, Approved, Resolved, ...) + value row
      int sumEnd = part.IndexOf("</table>", StringComparison.Ordinal);
      string summary = sumEnd > 0 ? part.Substring(0, sumEnd) : "";
      var sumRows = Regex.Matches(summary, "<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline).Cast<Match>().Select(m => RxCell.Matches(m.Groups[1].Value).Cast<Match>().Select(c => Text(c.Groups[1].Value)).ToList()).ToList();
      string test = !noSummary && sumRows.Count > 0 && sumRows[0].Count > 0 ? sumRows[0][0] : "Test " + (t + 1);   // #91
      int newActive = 0;
      if (!noSummary && sumRows.Count > 1) {   // #91
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
      // #89: the Path column is found by name. Reports can have extra item columns (e.g. "Layer")
      // between Item ID and Path; the old code assumed Path = Item ID + 1.
      int cPath1 = names.IndexOf("Path"), cPath2 = cPath1 >= 0 ? names.IndexOf("Path", cPath1 + 1) : -1;
      // #94: required columns, found by header name in any order with any extra columns
      if (cPt < 0 && !Missing.Contains("Clash Point")) Missing.Add("Clash Point");
      if (cId1 < 0 && !Missing.Contains("Item ID")) Missing.Add("Item ID");
      if (cPath1 < 0 && !Warnings.Contains("Item Path")) Warnings.Add("Item Path");
      if (names.IndexOf("Image") < 0 && !Warnings.Contains("Image")) Warnings.Add("Image");
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
        for (int item = 0; item < 2; item++) {
          int ci = item == 0 ? cId1 : cId2;
          if (ci < 0) continue;
          int cp = item == 0 ? cPath1 : cPath2;
          if (cp < 0) cp = ci + 1;   // #89: fallback = old behaviour
          var em = RxEid.Match(T(ci));
          string model = ModelOf(T(cp));
          if (model != "" && !c.Models.Contains(model)) c.Models.Add(model);
          if (em.Success) { int id; if (int.TryParse(em.Groups[1].Value, out id)) { c.Items.Add(new KeyValuePair<string, int>(model, id)); c.ItemPaths.Add(T(cp)); /* #90 */ parts.Add(model + " #" + id); } }
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
}
