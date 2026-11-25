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
  public bool ChangingSource;   // #92: set by the form's Change source button
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
    public string CatName = "";   // #81: category of the found element (parent category for a subcategory)
    public int Item = 1;          // #83: item order in the clash (1 = first item of the clash data, 2 = second): decides the colour
    public bool Found { get { return Eid != null; } }
  }

  static List<RevitLinkInstance> Links(Document doc) {
    return new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().Where(l => { try { return l.GetLinkDocument() != null; } catch (Exception) { return false; } }).ToList();
  }

  // #90: Revit category names of the host document, collected once per document
  HashSet<string> catNames; string catNamesDoc;
  string PathCategory(Document doc, string path) {
    if (catNames == null || catNamesDoc != doc.Title) {
      catNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); catNamesDoc = doc.Title;
      foreach (Category cat in doc.Settings.Categories) { try { catNames.Add(cat.Name); } catch (Exception) { } }
    }
    foreach (var seg in (path ?? "").Split('>').Select(s => s.Trim()).Where(s => s != "")) if (catNames.Contains(seg)) return seg;
    return null;
  }

  // find each element by its Element ID: in the open model when the item's model is this file, else in the loaded link of that model
  List<Hit> FindHits(Document doc, VpCam c) {
    var res = new List<Hit>();
    var src = c.IsClash ? c.Items : c.Select.Select(i => new KeyValuePair<string, int>(doc.Title, i)).ToList();
    List<RevitLinkInstance> links = null;
    int itemNo = 0;
    foreach (var it in src) {
      itemNo++;
      var h = new Hit { Model = it.Key, Id = it.Value, Item = itemNo };   // #83: Items keeps the clash data order
      var id = NavData.Eid(it.Value);
      if (string.IsNullOrEmpty(it.Key) || ReviztoData.ModelMatches(it.Key, doc.Title) || !c.IsClash) {   // #94: no Item Path in the report -> look in the open model
        if (doc.GetElement(id) != null) { h.Eid = id; h.Where = "found in this model"; } else h.Where = "not found in this model";
      } else {
        if (links == null) links = Links(doc);
        foreach (var li in links.Where(l => ReviztoData.ModelMatches(it.Key, l.GetLinkDocument().Title))) {
          if (li.GetLinkDocument().GetElement(id) != null) { h.Eid = id; h.Link = li; h.Where = "found in link " + li.GetLinkDocument().Title; break; }
        }
        if (!h.Found) h.Where = links.Any(l => ReviztoData.ModelMatches(it.Key, l.GetLinkDocument().Title)) ? "not found in the link" : "model not open / not linked";
      }
      if (h.Found) { var hc = CatOf(doc, h); if (hc != null) h.CatName = hc.Name; }   // #81
      // #90: the path's category segment must agree with the found element's category, else it is a different element with the same ID
      if (h.Found && c.IsClash && h.CatName != "" && itemNo - 1 < c.ItemPaths.Count) {
        string pathCat = PathCategory(doc, c.ItemPaths[itemNo - 1]);
        if (pathCat != null && !string.Equals(pathCat, h.CatName, StringComparison.OrdinalIgnoreCase)) {
          h.Eid = null; h.Link = null;
          h.Where = "ID matches a different element (" + h.CatName + ", report says " + pathCat + "): check the model";
        }
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
  // #81: categories are matched by BuiltInCategory (the integer id of a built-in category is the same in the link and the host
  // document), falling back to the category name (project-defined categories have different ids per document).
  static void ApplyCategories(Document doc, View v, HashSet<long> keepIds, HashSet<string> keepNames) {
    long links = (long)(int)BuiltInCategory.OST_RvtLinks, box = (long)(int)BuiltInCategory.OST_SectionBox;
    bool all = keepIds == null;
    foreach (Category cat in doc.Settings.Categories) {
      try {
        if (cat.CategoryType != CategoryType.Model) continue;
        long id = Compat.IdValue(cat.Id);
        bool keepIt = all || id == links || id == box || keepIds.Contains(id) || (keepNames != null && keepNames.Contains(cat.Name));
        bool hide = !keepIt;
        if (!v.CanCategoryBeHidden(cat.Id) || v.GetCategoryHidden(cat.Id) == hide) continue;
        v.SetCategoryHidden(cat.Id, hide);
      } catch (Exception) { }
    }
  }

  // top-level category of the hit's element (in its own document: the link document for a linked hit)
  static Category CatOf(Document doc, Hit h) {
    var d = h.Link != null ? h.Link.GetLinkDocument() : doc;
    var e = d == null ? null : d.GetElement(h.Eid);
    var c = e == null ? null : e.Category;
    if (c != null && c.Parent != null) c = c.Parent;
    return c;
  }

  public string LinkNote = "";   // #81: what the link display step did in the last ApplyIsolation (shown in the window)

  // isolate the found elements of the item in view v when on (#82: clash too, Isolate button). Host elements by temporary isolate; linked ones as above.
  // on == false: only the temporary isolate is switched off (and categories shown again); section box and colours are not touched here.
  void ApplyIsolation(Document doc, View3D v, List<Hit> hits, bool on) {
    if (v.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate)) v.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
    var found = hits.Where(x => x.Found).ToList();
    LinkNote = "";   // #83: the link-display step of #81 is removed; Colorize may add a note
    var iso = found.Select(x => x.Link == null ? x.Eid : x.Link.Id).Distinct().ToList();
    if (on && iso.Count > 0) v.IsolateElementsTemporary(iso);
    HashSet<long> keepIds = null; HashSet<string> keepNames = null;
    if (on && found.Any(x => x.Link != null)) {
      keepIds = new HashSet<long>(); keepNames = new HashSet<string>();
      foreach (var x in found) {
        var cat = CatOf(doc, x);
        if (cat == null) { keepIds = null; keepNames = null; break; }   // a category we cannot read: hide nothing rather than hide the element
        keepIds.Add(Compat.IdValue(cat.Id)); keepNames.Add(cat.Name);   // BuiltInCategory id first (equal in link and host), name as fallback
      }
    }
    ApplyCategories(doc, v, keepIds, keepNames);
  }

  // #68 / #77: VP / work views open shaded at Fine detail (skipped when a view template controls them; a refusal never breaks navigation)
  static void ShadedFine(View v) {
    try { if (v.ViewTemplateId != ElementId.InvalidElementId) return; } catch (Exception) { return; }
    try { v.DisplayStyle = DisplayStyle.Shading; } catch (Exception) { }
    try { v.DetailLevel = ViewDetailLevel.Fine; } catch (Exception) { }   // #77
  }

  // #77: every annotation category hidden except the section box (stays visible). Skipped when a view template controls the view.
  // View.CanCategoryBeHidden(ElementId) / SetCategoryHidden / GetCategoryHidden exist in Revit 2022-2027. Call inside a transaction.
  static void HideAnnotationsExceptSectionBox(View v) {
    try { if (v.ViewTemplateId != ElementId.InvalidElementId) return; } catch (Exception) { return; }
    Document doc = v.Document;
    var sb = new ElementId(BuiltInCategory.OST_SectionBox);
    foreach (Category cat in doc.Settings.Categories) {
      try {
        if (cat.CategoryType != CategoryType.Annotation || cat.Id.Equals(sb)) continue;
        if (v.CanCategoryBeHidden(cat.Id) && !v.GetCategoryHidden(cat.Id)) v.SetCategoryHidden(cat.Id, true);
      } catch (Exception) { }
    }
    try { if (v.GetCategoryHidden(sb)) v.SetCategoryHidden(sb, false); } catch (Exception) { }
  }

  // #80 / #83: Navisworks-style clash colours by ITEM ORDER (item 1 red 255,0,0; item 2 green 0,170,0), not by model.
  // Host hit: per-element override in its item colour. Linked hit: category override (View.SetCategoryOverrides) of its category in the host view,
  // in its item colour (in an isolated clash view only the clash categories are visible). Per-element host overrides win over category overrides.
  // OverrideGraphicSettings.SetSurfaceForegroundPatternId / -Color, SetCutForegroundPatternId / -Color, SetProjectionLineColor,
  // SetCutLineColor, View.SetElementOverrides and View.SetCategoryOverrides(ElementId, OverrideGraphicSettings) all exist in Revit 2022-2027
  // (SetCategoryOverrides since 2014, the foreground pattern API since 2019.1).
  public bool ColorsOn = true;
  readonly Dictionary<ElementId, List<ElementId>> colored = new Dictionary<ElementId, List<ElementId>>();      // view id -> element ids overridden by the tool (#83)
  readonly Dictionary<ElementId, List<ElementId>> coloredCats = new Dictionary<ElementId, List<ElementId>>();  // view id -> category ids overridden by the tool (#83)

  static ElementId SolidFillId(Document doc) {
    try {
      var fp = FillPatternElement.GetFillPatternElementByName(doc, FillPatternTarget.Drafting, "<Solid fill>");
      if (fp != null) return fp.Id;
    } catch (Exception) { }
    try {
      foreach (FillPatternElement e in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))) {
        var p = e.GetFillPattern();
        if (p != null && p.IsSolidFill && p.Target == FillPatternTarget.Drafting) return e.Id;
      }
    } catch (Exception) { }
    return ElementId.InvalidElementId;
  }

  // #83: clears the element and category overrides the tool set in this view (default OverrideGraphicSettings)
  void ClearColors(View v) {
    List<ElementId> ids;
    if (colored.TryGetValue(v.Id, out ids)) {
      foreach (var id in ids) { try { v.SetElementOverrides(id, new OverrideGraphicSettings()); } catch (Exception) { } }
      colored.Remove(v.Id);
    }
    if (coloredCats.TryGetValue(v.Id, out ids)) {
      foreach (var id in ids) { try { v.SetCategoryOverrides(id, new OverrideGraphicSettings()); } catch (Exception) { } }
      coloredCats.Remove(v.Id);
    }
  }

  // #96 (Clear Overrides ribbon button): resets the category overrides this tool set in the view, forgets the tracking of this view and returns
  // true when the view was tracked. Must be called inside the caller's open transaction. The element overrides are reset by the caller's own scan.
  public bool ResetTrackedCategories(View v) {
    bool tracked = colored.ContainsKey(v.Id) || coloredCats.ContainsKey(v.Id);
    List<ElementId> ids;
    if (coloredCats.TryGetValue(v.Id, out ids)) {
      foreach (var id in ids) { try { v.SetCategoryOverrides(id, new OverrideGraphicSettings()); } catch (Exception) { } }
      coloredCats.Remove(v.Id);
    }
    colored.Remove(v.Id);
    return tracked;
  }

  // surface + cut foreground = solid pattern in col, projection + cut lines in col
  static OverrideGraphicSettings Ogs(ElementId solid, Color col) {
    var o = new OverrideGraphicSettings();
    o.SetProjectionLineColor(col);
    o.SetCutLineColor(col);
    if (solid != null && solid != ElementId.InvalidElementId) {
      o.SetSurfaceForegroundPatternId(solid); o.SetSurfaceForegroundPatternColor(col);
      o.SetCutForegroundPatternId(solid); o.SetCutForegroundPatternColor(col);
    }
    return o;
  }

  static bool PaintEl(View v, ElementId id, ElementId solid, Color col) {
    try { v.SetElementOverrides(id, Ogs(solid, col)); return true; } catch (Exception) { return false; }
  }

  static bool PaintCat(View v, ElementId catId, ElementId solid, Color col) {
    try { v.SetCategoryOverrides(catId, Ogs(solid, col)); return true; } catch (Exception) { return false; }
  }

  static Color ItemColor(int item) { return item == 2 ? new Color(0, 170, 0) : new Color(255, 0, 0); }   // #83
  static string ItemColorName(int item) { return item == 2 ? "green" : "red"; }

  // #83: the host document's category for a category found in a link: same BuiltInCategory id first, then the name (project-defined categories)
  static ElementId HostCatId(Document doc, Category linkCat) {
    long id = Compat.IdValue(linkCat.Id);
    ElementId byName = null;
    foreach (Category cat in doc.Settings.Categories) {
      try {
        if (Compat.IdValue(cat.Id) == id && id < 0) return cat.Id;
        if (byName == null && cat.Name == linkCat.Name) byName = cat.Id;
      } catch (Exception) { }
    }
    return byName;
  }

  // clears what the previous clash set in this view, then (clash + ColorsOn) colours the current hits by item order. Never throws.
  void Colorize(Document doc, View v, bool clash, List<Hit> hits) {
    try {
      ClearColors(v);
      if (!clash || !ColorsOn) return;
      var solid = SolidFillId(doc);
      var done = new List<ElementId>();
      var doneCats = new List<ElementId>();
      colored[v.Id] = done; coloredCats[v.Id] = doneCats;   // tracked before painting, so a failure half way is still cleared next time
      foreach (var h in hits.Where(x => x.Found && x.Link == null).OrderBy(x => x.Item))
        if (!done.Contains(h.Eid) && PaintEl(v, h.Eid, solid, ItemColor(h.Item))) done.Add(h.Eid);
      var catItem = new Dictionary<long, int>();
      var notes = new List<string>();
      foreach (var h in hits.Where(x => x.Found && x.Link != null).OrderBy(x => x.Item)) {   // item 1 first: item 1 wins a shared category
        var lc = CatOf(doc, h);
        var hid = lc == null ? null : HostCatId(doc, lc);
        if (hid == null) continue;
        long key = Compat.IdValue(hid);
        int first;
        if (catItem.TryGetValue(key, out first)) {
          if (first != h.Item) notes.Add("Both clash elements are not from this model (both in linked models, category " + lc.Name + "): both shown in item " + first + "'s colour (" + ItemColorName(first) + ")");   // owner wording 2026-11-24
          continue;
        }
        catItem[key] = h.Item;
        if (PaintCat(v, hid, solid, ItemColor(h.Item))) doneCats.Add(hid);
      }
      if (notes.Count > 0) LinkNote = (string.IsNullOrEmpty(LinkNote) ? "" : LinkNote + "; ") + string.Join("; ", notes.Distinct().ToArray());
    } catch (Exception) { }
  }

  public Dictionary<int, bool> OtherIdx = new Dictionary<int, bool>();   // #79: VpCam.Index -> anchored to a model that is not the open one (IsOtherModel)

  // orthographic copy of the VP view to work in: same look direction, same section box (or one around the item), found elements isolated if Isolate is on
  string DuplicateOrtho(UIDocument uidoc, View3D tv, VpCam c) {
    var doc = uidoc.Document;
    var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
    string baseName = /* #78 */ "WIP-Clash Viewer-" + tv.Name, nm = baseName;
    for (int i = 2; names.Contains(nm); i++) nm = baseName + " (" + i + ")";
    View3D nv;
    using (var tx = new Transaction(doc, "Clash Viewer - orthographic work view")) {
      tx.Start();
      var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
      nv = View3D.CreateIsometric(doc, vft.Id);
      try { nv.Name = nm; } catch (Exception) { }
      // work view: no view template, every annotation category off, only the section box shown
      try { if (nv.ViewTemplateId != ElementId.InvalidElementId) nv.ViewTemplateId = ElementId.InvalidElementId; } catch (Exception) { }
      HideAnnotationsExceptSectionBox(nv);   // #77: shared with the VP view (was inline here)
      var o = tv.GetOrientation();
      nv.SetOrientation(new ViewOrientation3D(o.EyePosition, o.UpDirection, o.ForwardDirection));
      ShadedFine(nv);   // #68 / #77
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
      ApplyIsolation(doc, nv, Hits, Isolated && !Source.StartsWith("Revizto"));   // #69 / #82: clash too, H.Isolated (default true for clash)
      Colorize(doc, nv, c.IsClash, Hits);   // #80 / #81: after ApplyIsolation (link display + categories)
      tx.Commit();
    }
    uidoc.ActiveView = nv;
    try { var uiv = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == nv.Id); if (uiv != null) uiv.ZoomToFit(); } catch (Exception) { }
    if (c.IsClash) { try { uidoc.Selection.SetElementIds(new List<ElementId>()); } catch (Exception) { } }   // #84: clash elements are not left selected (blue)
    else {
      var sel = Hits.Where(x => x.Found && x.Link == null).Select(x => x.Eid).ToList();
      if (sel.Count > 0) { try { uidoc.Selection.SetElementIds(sel); } catch (Exception) { } }
    }
    return nv.Name;
  }

  // #84: union of the found hits' bounding boxes (host, and linked through the link transform: HitBox); null when none
  internal static BoundingBoxXYZ HitsBox(Document doc, List<Hit> hits) {
    var boxes = hits.Select(x => HitBox(doc, x)).Where(b => b != null).ToList();
    if (boxes.Count == 0) return null;
    var r = new BoundingBoxXYZ();
    r.Min = new XYZ(boxes.Min(b => b.Min.X), boxes.Min(b => b.Min.Y), boxes.Min(b => b.Min.Z));
    r.Max = new XYZ(boxes.Max(b => b.Max.X), boxes.Max(b => b.Max.Y), boxes.Max(b => b.Max.Z));
    return r;
  }

  // #84: horizontal fraction (0..0.6) of the view's width the floating window covers, and on which side (left = true). 0 = no overlap / unknown.
  // Runs on the Revit thread, which is also the thread of the modeless form (as in AICon): reading Bounds directly is fine.
  double FormCover(int vLeft, int vTop, int vRight, int vBottom, out bool left) {
    left = true;
    try {
      if (Form == null || Form.IsDisposed || !Form.Visible || Form.WindowState == WF.FormWindowState.Minimized) return 0;
      var fr = Form.Bounds;   // screen pixels (top-level form)
      int vw = vRight - vLeft;
      if (vw <= 0) return 0;
      double ox = Math.Min(vRight, fr.Right) - Math.Max(vLeft, fr.Left);
      double oy = Math.Min(vBottom, fr.Bottom) - Math.Max(vTop, fr.Top);
      if (ox <= 0 || oy <= 0) return 0;
      left = (fr.Left + fr.Right) / 2.0 < (vLeft + vRight) / 2.0;
      return Math.Min(ox / vw, 0.6);
    } catch (Exception) { return 0; }
  }

  // #84: zoom the VP view to the clash elements (box grown 15% + 0.3 m), widened toward the side the window covers so they centre in the visible part.
  // UIView.ZoomAndCenterRectangle(XYZ, XYZ), UIView.GetWindowRectangle() and View.RightDirection / UpDirection exist in Revit 2022-2027. Never throws.
  void ZoomToHits(Document doc, UIDocument uidoc, View3D v, List<Hit> hits) {
    try {
      var bb = HitsBox(doc, hits);
      if (bb == null) return;
      var uiv = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == v.Id);
      if (uiv == null) return;
      XYZ ctr = (bb.Min + bb.Max) * 0.5;
      double m = 0.3 / FT;
      XYZ half = new XYZ((bb.Max.X - bb.Min.X) / 2.0 * 1.15 + m, (bb.Max.Y - bb.Min.Y) / 2.0 * 1.15 + m, (bb.Max.Z - bb.Min.Z) / 2.0 * 1.15 + m);
      XYZ R = v.RightDirection, U = v.UpDirection;
      double rmin = double.MaxValue, rmax = double.MinValue, umin = double.MaxValue, umax = double.MinValue;
      foreach (double sx in new[] { -1.0, 1.0 }) foreach (double sy in new[] { -1.0, 1.0 }) foreach (double sz in new[] { -1.0, 1.0 }) {
        XYZ q = new XYZ(ctr.X + sx * half.X, ctr.Y + sy * half.Y, ctr.Z + sz * half.Z);
        double r = q.DotProduct(R), u = q.DotProduct(U);
        rmin = Math.Min(rmin, r); rmax = Math.Max(rmax, r); umin = Math.Min(umin, u); umax = Math.Max(umax, u);
      }
      var wr = uiv.GetWindowRectangle();   // Autodesk.Revit.DB.Rectangle (not System.Drawing)
      bool left; double f = FormCover(wr.Left, wr.Top, wr.Right, wr.Bottom, out left);
      if (f > 0.01) {   // widen on the covered side: width * f / (1 - f), so the box fills the visible (1 - f) of the view width
        double add = (rmax - rmin) * f / (1.0 - f);
        if (left) rmin -= add; else rmax += add;   // screen left = -RightDirection
      }
      XYZ baseP = ctr - R * ctr.DotProduct(R) - U * ctr.DotProduct(U);   // keeps the box centre's depth
      uiv.ZoomAndCenterRectangle(baseP + R * rmin + U * umin, baseP + R * rmax + U * umax);
    } catch (Exception) { }
  }

  // #85: perspective camera fit for a clash. Last UIView rectangle (screen px) of a clash navigation: used for the overlap when no UIView is open yet.
  bool lastRectOk; int lastVL, lastVT, lastVR, lastVB;
  const double PerspHalfFov = 25.0 * Math.PI / 180.0;   // Revit's default perspective is about 50 degrees
  // #87: separate horizontal / vertical half angles; learned from UIView.GetZoomCorners (exists in Revit 2022-2027, since 2011) and cached for the next clashes
  double fovHx = PerspHalfFov, fovHy = PerspHalfFov; bool fovLearned;
  XYZ fitEye, fitDir, fitUp, fitRight;   // #87: last fitted camera (eye after the overlap shift is NOT needed: the frustum is relative to the eye)

  // #85: new eye so the host item fills the view: target = clash point (else box centre), keep the current direction (c.Fwd / c.Up from ClashSetup),
  // d = max(1.5 m, r / tan(25 deg) * 1.1), eye = target - dir * d; then target and eye shift sideways by w * f / 2 (w = 2 d tan 25 deg)
  // toward the side the form covers, so the clash shows in the free part. SetOrientation / View3D.IsPerspective / UIView.GetWindowRectangle exist in Revit 2022-2027.
  void FitPerspective(Document doc, UIDocument uidoc, View3D v, VpCam c, List<Hit> hits, BoundingBoxXYZ clashBox) {
    try {
      // #86: frame ALL found clash elements (host + linked) completely, not only the host item
      BoundingBoxXYZ bb = HitsBox(doc, hits);
      if (bb == null) bb = clashBox;
      if (bb == null) return;
      XYZ center = (bb.Min + bb.Max) * 0.5;
      XYZ target = c.HasPoint && c.Point != null ? c.Point : center;
      XYZ dir = c.Fwd.Normalize();
      XYZ up = c.Up - dir * c.Up.DotProduct(dir);   // perpendicular to dir, as ClashSetup does
      if (up.GetLength() < 1e-6) return;
      up = up.Normalize();
      XYZ right = dir.CrossProduct(up).Normalize();
      // #87: half angles. Learned values win; otherwise hx = 25 deg and hy from the crop box aspect (|Y extent| / |X extent|, invalid = 0.6)
      double hx = fovHx, hy = fovHy;
      if (!fovLearned) {
        double aspect = 0.6;
        try {
          var cb = v.CropBox;
          double ex = Math.Abs(cb.Max.X - cb.Min.X), ey = Math.Abs(cb.Max.Y - cb.Min.Y);
          if (ex > 1e-9 && ey > 1e-9) { double q = ey / ex; if (q > 0.1 && q < 10) aspect = q; }
        } catch (Exception) { }
        hy = Math.Atan(Math.Tan(PerspHalfFov) * aspect);
      }
      double tx = Math.Tan(hx), ty = Math.Tan(hy);
      if (tx < 1e-6 || ty < 1e-6) return;
      // #87: per corner: lateral offsets (a, b), depth z (positive = far side): eye distance = max(|a| / tan hx, |b| / tan hy) - z
      double dReq = 0;
      foreach (var cx in new[] { bb.Min.X, bb.Max.X }) foreach (var cy in new[] { bb.Min.Y, bb.Max.Y }) foreach (var cz in new[] { bb.Min.Z, bb.Max.Z }) {
        XYZ o = new XYZ(cx, cy, cz) - target;
        double a = o.DotProduct(right), b = o.DotProduct(up), z = o.DotProduct(dir);
        dReq = Math.Max(dReq, Math.Max(Math.Abs(a) / tx, Math.Abs(b) / ty) - z);
      }
      double d = Math.Max(2.0 / FT, dReq * 1.3);   // #86: margin 1.3, min 2 m
      int vl = 0, vt = 0, vr = 0, vb = 0; bool have = false;
      try {
        var uiv = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == v.Id);
        if (uiv != null) { var wr = uiv.GetWindowRectangle(); vl = wr.Left; vt = wr.Top; vr = wr.Right; vb = wr.Bottom; have = true; }
      } catch (Exception) { }
      if (!have && lastRectOk) { vl = lastVL; vt = lastVT; vr = lastVR; vb = lastVB; have = true; }
      if (have) {
        bool left; double f = FormCover(vl, vt, vr, vb, out left);
        if (f > 0.01) {
          double w = 2.0 * d * tx;   // #87: real hx
          double s = w * f / 2.0;
          target = target + right * (left ? -s : s);   // camera moves toward the covered side: the clash appears in the free part
        }
      }
      XYZ eye = target - dir * d;
      v.SetOrientation(new ViewOrientation3D(eye, up, dir));
      fitEye = eye; fitDir = dir; fitUp = up; fitRight = right;
    } catch (Exception) { }
  }

  // #87: after the commit: read the real frustum from the active perspective UIView (GetZoomCorners: 2 corners, model space) and, when the half angles
  // differ from the assumed ones by more than 2 degrees, cache them and re-run the fit in a small transaction. UIView.GetZoomCorners exists in Revit 2022-2027.
  void RefitFromFrustum(Document doc, UIDocument uidoc, View3D v, VpCam c, List<Hit> hits, BoundingBoxXYZ clashBox) {
    try {
      if (fitEye == null || fitDir == null) return;
      var uiv = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == v.Id);
      if (uiv == null) return;
      var zc = uiv.GetZoomCorners();
      if (zc == null || zc.Count < 2) return;
      double mx = 0, my = 0, mdepth = double.MaxValue;
      foreach (XYZ q in zc) {
        XYZ o = q - fitEye;
        double depth = o.DotProduct(fitDir);
        if (depth <= 1e-9) return;   // guard
        mdepth = Math.Min(mdepth, depth);
        mx = Math.Max(mx, Math.Abs(o.DotProduct(fitRight)) / depth);
        my = Math.Max(my, Math.Abs(o.DotProduct(fitUp)) / depth);
      }
      double rhx = Math.Atan(mx), rhy = Math.Atan(my);
      if (rhx < 1e-3 || rhy < 1e-3) return;
      double ahx = fovHx, ahy = fovHy;
      if (!fovLearned) {   // assumed values of the first pass: recompute the crop-box guess the same way
        ahx = PerspHalfFov;
        double aspect = 0.6;
        try { var cb = v.CropBox; double ex = Math.Abs(cb.Max.X - cb.Min.X), ey = Math.Abs(cb.Max.Y - cb.Min.Y); if (ex > 1e-9 && ey > 1e-9) { double q = ey / ex; if (q > 0.1 && q < 10) aspect = q; } } catch (Exception) { }
        ahy = Math.Atan(Math.Tan(PerspHalfFov) * aspect);
      }
      const double tol = 2.0 * Math.PI / 180.0;
      if (Math.Abs(rhx - ahx) <= tol && Math.Abs(rhy - ahy) <= tol) { fovHx = ahx; fovHy = ahy; fovLearned = true; return; }
      fovHx = rhx; fovHy = rhy; fovLearned = true;
      using (var t2 = new Transaction(doc, "Clash Viewer fit")) {
        t2.Start();
        FitPerspective(doc, uidoc, v, c, hits, clashBox);   // SetOrientation only
        t2.Commit();
      }
    } catch (Exception) { }
  }

  // #85: remember the VP view's rectangle for the next navigation
  void RememberRect(UIDocument uidoc, View3D v) {
    try {
      var uiv = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == v.Id);
      if (uiv == null) return;
      var wr = uiv.GetWindowRectangle();
      lastVL = wr.Left; lastVT = wr.Top; lastVR = wr.Right; lastVB = wr.Bottom; lastRectOk = true;
    } catch (Exception) { }
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
      if (c.Models.Count == 0) return false;   // #94: no Item Path in the report -> treat as this model
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
    if (!c.HasNavCam) {   // #23: a Navisworks viewpoint camera is kept exactly
      c.Eye = center + dir * (diag * 1.15);
      c.Fwd = (center - c.Eye).Normalize();
      c.Up = c.Fwd.CrossProduct(XYZ.BasisZ).CrossProduct(c.Fwd).Normalize();
    }
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

      if (a == "ChangeSource") {   // #92: reopen through the same path as the ribbon button, dialog forced
        Launcher.Open(app, true);
        return;
      }
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
      if (a == "ToggleColors") {   // #80: clear / re-apply the clash colours in the current VP (or work) view
        ColorsOn = !ColorsOn;
        View3D tgtView = tv;
        var av3 = av as View3D;
        if (av3 != null && !av3.IsTemplate && (av3.Name.StartsWith("VP-") || av3.Name.StartsWith("WIP-Clash Viewer-"))) tgtView = av3;
        if (tgtView != null) {
          try {
            using (var txc = new Transaction(doc, "Clash Viewer - clash colours")) {
              txc.Start();
              Colorize(doc, tgtView, true, Hits);
              txc.Commit();
            }
          } catch (Exception) { }
        }
        if (Form != null && !Form.IsDisposed) { Form.SyncColors(); Form.ShowMessage(ColorsOn ? "Clash colours applied (item 1 red, item 2 green)." : "Clash colours cleared in this view."); }
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
      // #82: clash: toggles Isolated (default true); Refresh re-runs the view: unisolated = temporary isolate off, section box and colours kept
      else if (a == "Isolate") { Isolated = !Isolated; a = "Refresh"; }
      if (a == "Duplicate") {
        if (tv == null || cur < 1 || cur > count) { if (Form != null && !Form.IsDisposed) Form.ShowError("Open a VP view first (click Next or Previous), then duplicate it."); return; }
        string made = DuplicateOrtho(uidoc, tv, Cams[cur - 1]);
        if (Form != null && !Form.IsDisposed) Form.ShowMessage("Created \"" + made + "\": orthographic, no crop, annotations off (section box kept), same section box" + (Cams[cur - 1].IsClash && Isolated ? ", clashing elements isolated" : "") + ". Next / Previous go back to the VP view.");
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
      bool other = IsOtherModel(c, doc);
      OtherIdx[c.Index] = other;   // #79
      if (other && !SearchHit.Contains(c.Index)) {
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

      using (var tx = new Transaction(doc, "Clash Viewer")) {
        tx.Start();
        if (tv == null) {
          var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
          tv = View3D.CreatePerspective(doc, vft.Id);
        }
        if (tv.Name != nm) { try { tv.Name = nm; } catch (Exception) { tv.Name = nm + " (" + tv.Id.ToString() + ")"; } }
        tv.SetOrientation(new ViewOrientation3D(eyeP, upP, fwdP));
        ShadedFine(tv);   // #68 / #77
        HideAnnotationsExceptSectionBox(tv);   // #77

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
        if (c.IsClash && !c.HasNavCam && gotoBox == null && tv.IsPerspective) FitPerspective(doc, uidoc, tv, c, hits, clashBox);   // #85: after the section box is set
        // #82: clash and VP: only the found elements when Isolated (clash: on at load, Isolate / Unisolate button); never for Revizto
        ApplyIsolation(doc, tv, hits, Isolated && !Source.StartsWith("Revizto"));   // #69 / #82: clash too, H.Isolated (default true for clash)
        Colorize(doc, tv, c.IsClash, hits);   // #80 / #81: after ApplyIsolation / ApplyCategories; after isolation + section box; failures are swallowed
        tx.Commit();
      }

      if (uidoc.ActiveView == null || uidoc.ActiveView.Id != tv.Id) uidoc.ActiveView = tv;
      try { var uiv = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == tv.Id); if (uiv != null && !(c.IsClash && tv.IsPerspective)) uiv.ZoomToFit(); } catch (Exception) { }   // #85: not for the perspective clash view (it would undo the fit)
      if (c.IsClash && uidoc.ActiveView != null && uidoc.ActiveView.Id == tv.Id) {
        if (tv.IsPerspective && !c.HasNavCam) RefitFromFrustum(doc, uidoc, tv, c, hits, clashBox);   // #87: real frustum, second fit; #23: not for an exact Navisworks camera
        if (tv.IsPerspective) RememberRect(uidoc, tv);   // #85: perspective is fitted in the transaction (FitPerspective); ZoomAndCenterRectangle only for orthographic
        else ZoomToHits(doc, uidoc, tv, hits);
      }
      // #84: after the transaction, view active
      // close the tabs of earlier work views (they stay in the project): every extra open 3D view with links makes Revit slower
      try {
        foreach (var u in uidoc.GetOpenUIViews()) {
          var vv = doc.GetElement(u.ViewId) as View;
          if (vv != null && vv.Id != tv.Id && vv.Name.StartsWith("WIP-Clash Viewer-VP-")) u.Close();
        }
      } catch (Exception) { }
      // #84: a clash is never left selected (blue); only the ID buttons (action "Pick", returns above) select on purpose
      if (c.IsClash) { try { uidoc.Selection.SetElementIds(new List<ElementId>()); } catch (Exception) { } }
      else {
        var sel = hits.Where(x => x.Found && x.Link == null).Select(x => x.Eid).Distinct().ToList();
        if (sel.Count > 0) { try { uidoc.Selection.SetElementIds(sel); } catch (Exception) { } }
      }
      Cur = target;
      if (!c.IsClash && Source.StartsWith("Revizto") && a != "GotoElement") ComputeLoc(doc, c, hits);   // #72 / #73: read-only, after the commit
      if (Form != null && !Form.IsDisposed) { Form.RefreshLoc(target); if (hits.Count > 0) Form.ShowSelection(hits.Count(x => x.Found), hits.Count, c.IsClash, LinkNote); /* #81 */ Form.ShowTime(sw.ElapsedMilliseconds); }
    } catch (Exception ex) {
      if (Form != null && !Form.IsDisposed) Form.ShowError(ex.Message);
    }
  }

  // #97: exports the VP view at high resolution, auto-crops to the visible content (+12% margin) and saves it at the source picture's shape.
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
      o.PixelSize = 3000; // #97: high-res export, then auto-crop to the clash content
      o.ImageResolution = ImageResolution.DPI_150;
      o.HLRandWFViewsFileType = ImageFileType.JPEGLossless;
      o.ShadowViewsFileType = ImageFileType.JPEGLossless;
      doc.ExportImage(o);
      var f = new System.IO.DirectoryInfo(tmp).GetFiles().OrderByDescending(x => x.Length).FirstOrDefault();
      if (f == null) throw new Exception("Revit did not produce an image.");
      string dest = NavData.AfterPath(PicDir, c);
      string tmpOut = System.IO.Path.Combine(tmp, "out.jpg");
      using (var srcImg = NavData.LoadImageNoLock(f.FullName))
      using (var src = new SD.Bitmap(srcImg.Width, srcImg.Height, SD.Imaging.PixelFormat.Format32bppArgb)) // #97
      using (var bmp = new SD.Bitmap(outW, outH))
      using (var g = SD.Graphics.FromImage(bmp)) {
        g.InterpolationMode = SD.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.Clear(SD.Color.White);
        using (var gs = SD.Graphics.FromImage(src)) { gs.Clear(SD.Color.White); gs.DrawImage(srcImg, 0, 0, srcImg.Width, srcImg.Height); } // #97
        var content = SnapshotCrop.ContentRect(src); // #97: auto-crop to content, keep the full image if nothing found
        var destRect = new SD.Rectangle(0, 0, outW, outH);
        if (!content.IsEmpty) g.DrawImage(src, destRect, SnapshotCrop.FitCrop(content, src.Size, (double)outW / outH), SD.GraphicsUnit.Pixel); // #97
        else g.DrawImage(src, destRect);
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
