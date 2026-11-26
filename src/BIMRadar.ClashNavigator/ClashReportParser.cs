using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BIMRadar.ClashNavigator
{
// #94: pure parser for a Navisworks Clash Detective HTML (Tabular) report. NO Revit references:
// ClashData converts the result to VpCam (transform, camera, picture folder).

public class ParsedClashItem {
  public string Model, Path;   // Path = full Navisworks selection path of the item
  public int Id;               // Revit Element ID
}

public class ParsedClash {
  public int Index;
  public string Test, Name, Status, Group, Grid, Description;
  public string ImgRelPath = "";   // picture path as written in the report ("" = none)
  public double X, Y, Z;           // raw clash point, shared metres
  public bool DefaultTest;         // New / Active
  public List<string> Models = new List<string>();          // models of both items (also items without an Element ID)
  public List<ParsedClashItem> Items = new List<ParsedClashItem>();   // items with a readable Element ID
}

public class ClashReportResult {
  public List<ParsedClash> Clashes = new List<ParsedClash>();
  // what the report lacks, in Navisworks' own option names
  public List<string> Missing = new List<string>();
  public List<string> Warnings = new List<string>();
  public bool HasStatus;
}

public class ClashReportParser {
  static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
  static readonly Regex RxRow = new Regex("<tr class=\"(clashGroupRow|childRow|childRowLast|contentRow)\">(.*?)</tr>", RegexOptions.Singleline | RegexOptions.Compiled);
  static readonly Regex RxCell = new Regex("<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.Compiled);
  static readonly Regex RxTag = new Regex("<[^>]+>", RegexOptions.Compiled);
  static readonly Regex RxWs = new Regex("\\s+", RegexOptions.Compiled);
  static readonly Regex RxSrc = new Regex("src\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  static readonly Regex RxPt = new Regex("x\\s*:\\s*(-?[\\d.]+)\\s*,\\s*y\\s*:\\s*(-?[\\d.]+)\\s*,\\s*z\\s*:\\s*(-?[\\d.]+)", RegexOptions.Compiled);
  static readonly Regex RxEid = new Regex("Element ID\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

  static string Text(string html) { return RxWs.Replace(System.Net.WebUtility.HtmlDecode(RxTag.Replace(html ?? "", " ")), " ").Trim(); }

  // content check on the first characters of a file
  public static bool IsClashReport(string head) {
    head = head ?? "";
    return head.Contains("Clash Report") || head.Contains("testSummaryTable") || head.Contains("class=\"mainTable\"");   // #91
  }

  // model file from a Navisworks selection path "File > X.nwd > MODEL.nwc > Category > ..."
  // #90: the LAST model-file segment (innermost file: 'File > HOST.nwc > LINK.rvt > ...' gives LINK.rvt)
  public static string ModelOf(string path) {
    var segs = path.Split('>').Select(s => s.Trim()).Where(s => s != "").ToList();
    var m = segs.Skip(1).LastOrDefault(s => Regex.IsMatch(s, "\\.(nwc|rvt|ifc|dwg|nwd)$", RegexOptions.IgnoreCase) && !s.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase));
    return m ?? (segs.Count > 1 ? segs[1] : "");
  }

  public static ClashReportResult Parse(string s) {
    var res = new ClashReportResult();
    var Missing = res.Missing; var Warnings = res.Warnings;
    s = s ?? "";
    var starts = new List<int>();
    int at = 0;
    while ((at = s.IndexOf("class=\"testSummaryTable\"", at, StringComparison.Ordinal)) >= 0) { starts.Add(at); at += 10; }
    // #91: report written without "Summary" has no testSummaryTable: read each mainTable as its own test
    bool noSummary = starts.Count == 0;
    if (noSummary) { at = 0; while ((at = s.IndexOf("class=\"mainTable\"", at, StringComparison.Ordinal)) >= 0) { starts.Add(at); at += 10; } }
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
      if (cStatus >= 0) res.HasStatus = true;

      foreach (Match rm in RxRow.Matches(table)) {
        if (rm.Groups[1].Value == "clashGroupRow") continue;
        var raw = RxCell.Matches(rm.Groups[2].Value).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        Func<int, string> T = i => (i >= 0 && i < raw.Count) ? Text(raw[i]) : "";
        var pm = RxPt.Match(T(cPt));
        if (!pm.Success) continue;
        idx++;
        var c = new ParsedClash();
        c.Index = idx; c.Test = test;
        c.Name = T(cName); if (c.Name == "") c.Name = "Clash " + idx;
        var src = raw.Count > 0 ? RxSrc.Match(raw[0]) : Match.Empty;
        c.ImgRelPath = src.Success ? src.Groups[1].Value.Trim() : "";
        string status = T(cStatus);
        c.Status = status;
        c.Group = T(cGroup); c.Grid = T(cGrid); c.Description = T(cDesc);
        c.DefaultTest = cStatus >= 0 ? (status == "New" || status == "Active") : newActive > 0;
        for (int item = 0; item < 2; item++) {
          int ci = item == 0 ? cId1 : cId2;
          if (ci < 0) continue;
          int cp = item == 0 ? cPath1 : cPath2;
          if (cp < 0) cp = ci + 1;   // #89: fallback = old behaviour
          var em = RxEid.Match(T(ci));
          string model = ModelOf(T(cp));
          if (model != "" && !c.Models.Contains(model)) c.Models.Add(model);
          if (em.Success) { int id; if (int.TryParse(em.Groups[1].Value, out id)) c.Items.Add(new ParsedClashItem { Model = model, Id = id, Path = T(cp) }); }
        }
        c.X = double.Parse(pm.Groups[1].Value, Inv); c.Y = double.Parse(pm.Groups[2].Value, Inv); c.Z = double.Parse(pm.Groups[3].Value, Inv);
        res.Clashes.Add(c);
      }
    }
    return res;
  }
}
}
