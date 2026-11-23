using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace BIMRadar.ClashNavigator.Navigator
{
// #72 / #73: read-only questions to the open document ("what is at this location?"). No transactions, nothing is written.
// Revit API used (all exist in Revit 2022-2027): FilteredElementCollector, BoundingBoxIntersectsFilter, Outline,
// Element.Category / Element.ViewSpecific, Category.CategoryType, RevitLinkInstance.GetLinkDocument / GetTotalTransform, Transform.Inverse / OfPoint.
public static class LocationCheck {
  const double FT = 0.3048;
  public const int Cap = 1000;   // counting stops here (speed)

  public class ModelCount {
    public string Name = ""; public int Count; public bool IsLink;
  }

  // axis-aligned box (lo, hi) around the 8 corners of the box (min, max) after transform t
  public static void Aabb(Transform t, XYZ min, XYZ max, out XYZ lo, out XYZ hi) {
    double lx = double.MaxValue, ly = double.MaxValue, lz = double.MaxValue, hx = double.MinValue, hy = double.MinValue, hz = double.MinValue;
    foreach (double x in new[] { min.X, max.X }) foreach (double y in new[] { min.Y, max.Y }) foreach (double z in new[] { min.Z, max.Z }) {
      var q = t == null ? new XYZ(x, y, z) : t.OfPoint(new XYZ(x, y, z));
      lx = Math.Min(lx, q.X); ly = Math.Min(ly, q.Y); lz = Math.Min(lz, q.Z);
      hx = Math.Max(hx, q.X); hy = Math.Max(hy, q.Y); hz = Math.Max(hz, q.Z);
    }
    lo = new XYZ(lx, ly, lz); hi = new XYZ(hx, hy, hz);
  }

  // model-space bounds of a section box (a plane-cut box carries its own Transform)
  public static void WorldBounds(BoundingBoxXYZ bb, out XYZ lo, out XYZ hi) {
    Aabb(bb.Transform, bb.Min, bb.Max, out lo, out hi);
  }

  // model elements (not types, not view-specific, not link instances) whose bounding box meets the box; stops at Cap
  public static int CountModelElementsInBox(Document doc, XYZ min, XYZ max) {
    int n = 0;
    var filter = new BoundingBoxIntersectsFilter(new Outline(min, max));
    using (var col = new FilteredElementCollector(doc)) {
      foreach (Element e in col.WhereElementIsNotElementType().WherePasses(filter)) {
        try {
          if (e is RevitLinkInstance) continue;
          var cat = e.Category;
          if (cat == null || cat.CategoryType != CategoryType.Model) continue;
          if (e.ViewSpecific) continue;
          n++;
          if (n >= Cap) break;
        } catch (Exception) { }
      }
    }
    return n;
  }

  // host (first) and every loaded link: element count in the box (box given in host coordinates)
  public static List<ModelCount> ModelsNear(Document doc, XYZ min, XYZ max) {
    var res = new List<ModelCount>();
    int host = 0;
    try { host = CountModelElementsInBox(doc, min, max); } catch (Exception) { }
    res.Add(new ModelCount { Name = doc.Title, Count = host, IsLink = false });
    List<RevitLinkInstance> links;
    try { links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList(); } catch (Exception) { return res; }
    foreach (var li in links) {
      try {
        var ld = li.GetLinkDocument();
        if (ld == null) continue;
        XYZ lo, hi;
        Aabb(li.GetTotalTransform().Inverse, min, max, out lo, out hi);   // host box -> link coordinates
        res.Add(new ModelCount { Name = ld.Title, Count = CountModelElementsInBox(ld, lo, hi), IsLink = true });
      } catch (Exception) { }
    }
    return res;
  }

  // metres between the Revizto point and the centre of the nearest found element box; null when no element was found
  public static double? ElementOffset(Document doc, VpCam c, List<NavHandler.Hit> hits) {
    if (!c.HasPoint || hits == null) return null;
    double? best = null;
    foreach (var h in hits) {
      BoundingBoxXYZ b = null;
      try { b = NavHandler.HitBox(doc, h); } catch (Exception) { }
      if (b == null) continue;
      double d = (((b.Min + b.Max) * 0.5) - c.Point).GetLength() * FT;
      if (best == null || d < best.Value) best = d;
    }
    return best;
  }

  static string Short(string s) {
    s = System.IO.Path.GetFileNameWithoutExtension(s ?? "");
    return s.Length <= 30 ? s : s.Substring(0, 14) + "…" + s.Substring(s.Length - 14);
  }

  static string Num(int n) { return n >= Cap ? Cap + "+" : n.ToString(CultureInfo.InvariantCulture); }

  // "At location: this model 12 · link …G3… 40"
  public static string Summary(List<ModelCount> near) {
    var parts = near.Where(m => m.Count > 0).Select(m => (m.IsLink ? "link " + Short(m.Name) : "this model") + " " + Num(m.Count)).ToList();
    return parts.Count == 0 ? "At location: no model elements" : "At location: " + string.Join(" · ", parts);
  }

  // hint, in priority order (a: element far from the point, b: elements belong to a link, c: nothing anywhere, d: none)
  public static string PickHint(bool empty, double? offsetM, double boxM, List<ModelCount> near) {
    if (offsetM != null && offsetM.Value > Math.Max(boxM, 2.0))
      return "Element found " + offsetM.Value.ToString("0.0", CultureInfo.InvariantCulture) + " m from the Revizto point: shared coordinates may differ. Use 'Go to element'.";
    if (empty) {
      var lk = near.FirstOrDefault(m => m.IsLink && m.Count > 0);
      if (lk != null) return "Nothing from this model here. Elements here belong to link '" + lk.Name + "'. Check the zone / anchor in Revizto (e.g. G2 vs G3).";
      if (near.All(m => m.Count == 0)) return "Nothing from this model or its links at this location: probably not from this model, or the coordinates are wrong.";
    }
    return "";
  }
}
}
