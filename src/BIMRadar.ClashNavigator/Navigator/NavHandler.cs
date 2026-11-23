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

  // #72 / #73: what is at a Revizto issue's location (session cache, nothing written to Revizto data)
  public class LocResult { public bool Empty; public string Summary = ""; public string Hint = ""; public double? OffsetM; }
  public Dictionary<int, LocResult> Loc = new Dictionary<int, LocResult>();   // VpCam.Index -> result
  public HashSet<int> EmptyIdx = new HashSet<int>();                          // VpCam.Index of issues with nothing at their location
  public double OffsetLimit { get { return Math.Max(BoxM, 2.0); } }           // #73: more than this = "shared coordinates may differ"

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

  internal static BoundingBoxXYZ HitBox(Document doc, Hit h) {
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

  // #68: VP / work views open shaded at medium detail (skipped when a view template controls them; a refusal never breaks navigation)
  static void ShadedMedium(View v) {
    try { if (v.ViewTemplateId != ElementId.InvalidElementId) return; } catch (Exception) { return; }
    try { v.DisplayStyle = DisplayStyle.Shading; } catch (Exception) { }
    try { v.DetailLevel = ViewDetailLevel.Medium; } catch (Exception) { }
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
      ShadedMedium(nv);   // #68
      nv.CropBoxActive = false; nv.CropBoxVisible = false;
      if (c.IsClash) {   // #74: clash work view: same clash box when Section box is on, none when off (isolation stays)
        if (UseBox) { XYZ l0, h0; nv.SetSectionBox(ClashBox(doc, c, Hits, out l0, out h0)); nv.IsSectionBoxActive = true; }
        else nv.IsSectionBoxActive = false;
      }
      else if (tv.IsSectionBoxActive) { nv.SetSectionBox(tv.GetSectionBox()); nv.IsSectionBoxActive = true; }
      else {
        XYZ p = c.HasPoint ? c.Point : c.Target;
        double h = Math.Max(BoxM, 1.0) / FT / 2.0;
        var bb = new BoundingBoxXYZ(); bb.Min = new XYZ(p.X - h, p.Y - h, p.Z - h); bb.Max = new XYZ(p.X + h, p.Y + h, p.Z + h);
        nv.SetSectionBox(bb); nv.IsSectionBoxActive = true;
      }
      ApplyIsolation(doc, nv, Hits, (Isolated && !Source.StartsWith("Revizto")) || c.IsClash);   // #69
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

  // #74: section box of a clash: the clashing elements (host, and linked ones through the link transform) grown by Box size / 2
  // on each side (min 0.5 m); no element found: Box size centred on the clash point. Same box for the VP view and the work view.
  BoundingBoxXYZ ClashBox(Document doc, VpCam c, List<Hit> hits, out XYZ lo, out XYZ hi) {
    var boxes = hits.Select(x => HitBox(doc, x)).Where(b => b != null).ToList();
    double grow = Math.Max(BoxM / 2.0, 0.5) / FT;
    XYZ p = c.Point;
    if (boxes.Count == 0) {
      double h = Math.Max(BoxM, 1.0) / FT / 2.0;
      lo = new XYZ(p.X - h, p.Y - h, p.Z - h); hi = new XYZ(p.X + h, p.Y + h, p.Z + h);
    } else {
      lo = new XYZ(boxes.Min(b => b.Min.X) - grow, boxes.Min(b => b.Min.Y) - grow, boxes.Min(b => b.Min.Z) - grow);
      hi = new XYZ(boxes.Max(b => b.Max.X) + grow, boxes.Max(b => b.Max.Y) + grow, boxes.Max(b => b.Max.Z) + grow);
    }
    var r = new BoundingBoxXYZ(); r.Min = lo; r.Max = hi; return r;
  }

  // clash: elements of the open model, section box around them (#74: grown by Box size / 2, not capped), camera looking at it
  List<ElementId> ClashSetup(Document doc, VpCam c, List<Hit> hits, out BoundingBoxXYZ box) {
    var ids = hits.Where(x => x.Found && x.Link == null).Select(x => x.Eid).Distinct().ToList();   // host elements to select
    XYZ lo, hi;
    box = ClashBox(doc, c, hits, out lo, out hi);   // #74
    XYZ center = (lo + hi) * 0.5;
    double diag = Math.Max((hi - lo).GetLength(), 1.0 / FT);
    XYZ dir = new XYZ(1, -1, 0.9).Normalize();                    // from the south-east, above
    c.Eye = center + dir * (diag * 1.15);
    c.Fwd = (center - c.Eye).Normalize();
    c.Up = c.Fwd.CrossProduct(XYZ.BasisZ).CrossProduct(c.Fwd).Normalize();
    c.Target = center;
    return ids;
  }

  // #72: section box of a Revizto / viewpoint item (plane cut, or around the issue point); null = no box. Numbers unchanged from the inline code in Execute.
  BoundingBoxXYZ ReviztoBox(VpCam c) {
    if (c.HasPlane) {
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
      return bb;
    }
    if (c.HasPoint) {
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
      return bb;
    }
    return null;
  }

  // #72 / #73: what is at the location of a Revizto issue. Read-only (call outside a transaction). Stores the result in Loc / EmptyIdx; null when it cannot be told.
  LocResult ComputeLoc(Document doc, VpCam c, List<Hit> hits) {
    try {
      var box = ReviztoBox(c);
      if (box == null) return null;
      XYZ lo, hi;
      LocationCheck.WorldBounds(box, out lo, out hi);
      var inView = LocationCheck.ModelsNear(doc, lo, hi);
      bool empty = inView.Sum(m => m.Count) == 0;
      var near = inView;
      double? off = null;
      if (c.HasPoint) {
        double h = BoxM / FT / 2.0;
        near = LocationCheck.ModelsNear(doc, new XYZ(c.Point.X - h, c.Point.Y - h, c.Point.Z - h), new XYZ(c.Point.X + h, c.Point.Y + h, c.Point.Z + h));
        off = LocationCheck.ElementOffset(doc, c, hits);
      }
      var r = new LocResult { Empty = empty, OffsetM = off, Summary = LocationCheck.Summary(near), Hint = LocationCheck.PickHint(empty, off, BoxM, near) };
      Loc[c.Index] = r;
      if (empty) EmptyIdx.Add(c.Index); else EmptyIdx.Remove(c.Index);
      return r;
    } catch (Exception) { return null; }
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
      if (a == "CheckAll") {   // #72: every navigable Revizto item, the view does not change
        int n = 0, k = 0;
        foreach (var cc in Cams) {
          if (cc.IsClash || (!cc.HasPoint && !cc.HasPlane) || NotHere(cc, doc)) continue;
          var r = ComputeLoc(doc, cc, FindHits(doc, cc));
          if (r == null) continue;
          n++; if (r.Empty) k++;
        }
        if (Form != null && !Form.IsDisposed) { Form.RefreshLoc(0); Form.ShowMessage("Checked " + n + " issues: " + k + " empty."); }
        return;
      }
      if (a == "Isolate" && Source.StartsWith("Revizto")) { Isolated = false; a = "Refresh"; }   // #69: no isolation for Revizto issues
      else if (a == "Isolate") { Isolated = !Isolated; a = "Refresh"; }
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
      else if (a == "Refresh" || a == "GotoElement") tpos = pos < 0 ? 0 : pos;
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
      // #73: Go to element: section box + camera around the first found element instead of the Revizto point
      BoundingBoxXYZ gotoBox = null;
      XYZ eyeP = c.Eye, fwdP = c.Fwd, upP = c.Up;
      if (a == "GotoElement") {
        var gh = hits.FirstOrDefault(x => x.Found && HitBox(doc, x) != null);
        if (gh == null) { if (Form != null && !Form.IsDisposed) Form.ShowMessage("No element of this issue was found in the model."); return; }
        XYZ glo, ghi;
        gotoBox = ClashBox(doc, c, new List<Hit> { gh }, out glo, out ghi);
        XYZ gc = (glo + ghi) * 0.5;
        double gd = Math.Max((ghi - glo).GetLength(), 1.0 / FT);
        eyeP = gc + new XYZ(1, -1, 0.9).Normalize() * (gd * 1.15);
        fwdP = (gc - eyeP).Normalize();
        upP = fwdP.CrossProduct(XYZ.BasisZ).CrossProduct(fwdP).Normalize();
      }

      using (var tx = new Transaction(doc, "Viewpoint Navigator")) {
        tx.Start();
        if (tv == null) {
          var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
          tv = View3D.CreatePerspective(doc, vft.Id);
        }
        if (tv.Name != nm) { try { tv.Name = nm; } catch (Exception) { tv.Name = nm + " (" + tv.Id.ToString() + ")"; } }
        tv.SetOrientation(new ViewOrientation3D(eyeP, upP, fwdP));
        ShadedMedium(tv);   // #68

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

        if (gotoBox != null) {
          tv.SetSectionBox(gotoBox);
          tv.IsSectionBoxActive = true;
        } else if (UseBox && clashBox != null) {
          tv.SetSectionBox(clashBox);
          tv.IsSectionBoxActive = true;
        } else if (UseBox) {
          var rb = ReviztoBox(c);   // #72: box computed by a reusable method
          if (rb != null) { tv.SetSectionBox(rb); tv.IsSectionBoxActive = true; }
          else tv.IsSectionBoxActive = false;
        } else {
          tv.IsSectionBoxActive = false;
        }
        // clash: always only the clashing elements; Revizto: when "Isolate in 3D view" is on
        ApplyIsolation(doc, tv, hits, (Isolated && !Source.StartsWith("Revizto")) || c.IsClash);   // #69
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
      if (!c.IsClash && Source.StartsWith("Revizto") && a != "GotoElement") ComputeLoc(doc, c, hits);   // #72 / #73: read-only, after the commit
      if (Form != null && !Form.IsDisposed) { Form.RefreshLoc(target); if (hits.Count > 0) Form.ShowSelection(hits.Count(x => x.Found), hits.Count); Form.ShowTime(sw.ElapsedMilliseconds); }
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
}
