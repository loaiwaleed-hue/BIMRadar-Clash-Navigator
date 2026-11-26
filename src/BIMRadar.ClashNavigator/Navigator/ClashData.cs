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
  public static bool IsClashReport(string html) {
    try { using (var r = new System.IO.StreamReader(html)) { var b = new char[20000]; int n = r.Read(b, 0, b.Length); var s = new string(b, 0, n); return ClashReportParser.IsClashReport(s); } }   // #91
    catch (Exception) { return false; }
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
    var res = ClashReportParser.Parse(s);   // #94: pure HTML parsing
    Missing.AddRange(res.Missing); Warnings.AddRange(res.Warnings);
    hasStatus = res.HasStatus;
    bool picDirSet = false;
    foreach (var pc in res.Clashes) {
      var c = new VpCam();
      c.Index = pc.Index; c.Test = pc.Test; c.IsClash = true;
      c.Name = pc.Name;
      c.Guid = pc.Test + "|" + c.Name;
      string rel = pc.ImgRelPath;
      c.Img = rel != "" ? System.IO.Path.GetFileName(rel.Replace('/', '\\')) : "clash_" + pc.Index + ".jpg";
      if (!picDirSet && rel != "") { picDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, System.IO.Path.GetDirectoryName(rel.Replace('/', '\\')) ?? "")); picDirSet = true; }
      c.St0 = "";   // report status starts Open; the Navisworks status is kept separately in Status
      c.Status = pc.Status;
      c.DefaultTest = pc.DefaultTest;
      // both items: Element ID + model
      var parts = new List<string> { "Test " + pc.Test };
      if (pc.Group != "") parts.Add("Group " + pc.Group);
      if (pc.Status != "") parts.Add("Clash status: " + pc.Status);
      if (pc.Grid != "") parts.Add("Grid " + pc.Grid);
      if (pc.Description != "") parts.Add(pc.Description);
      c.Models.AddRange(pc.Models);
      foreach (var it in pc.Items) { c.Items.Add(new KeyValuePair<string, int>(it.Model, it.Id)); c.ItemPaths.Add(it.Path); /* #90 */ parts.Add(it.Model + " #" + it.Id); }
      c.Model = string.Join(" / ", c.Models);
      c.Note = string.Join(" · ", parts);
      c.RawPos = new double[] { pc.X, pc.Y, pc.Z };
      c.HasPoint = true;
      c.Point = toInt.OfPoint(new XYZ(pc.X / FT, pc.Y / FT, pc.Z / FT));
      // the real camera is set when navigating (depends on the elements found in the open model)
      c.Target = c.Point; c.Eye = c.Point + new XYZ(5, -5, 4.5); c.Fwd = (c.Point - c.Eye).Normalize();
      c.Up = c.Fwd.CrossProduct(XYZ.BasisZ).CrossProduct(c.Fwd).Normalize();
      c.FovH = 50.0 * Math.PI / 180.0; c.Aspect = 1.0; c.FocalM = 3.0;
      list.Add(c);
    }
    return list;
  }
}
}
