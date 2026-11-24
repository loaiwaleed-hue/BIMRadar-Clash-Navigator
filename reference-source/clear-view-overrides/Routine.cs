var wantedViews = new List<string>();
var rawViews = input.TextList("viewNames");
if (rawViews != null)
    foreach (var s in rawViews)
        if (!string.IsNullOrWhiteSpace(s)) wantedViews.Add(s.Trim());

bool deep = input.Bool("deepScan", false);

Func<View, bool> plottable = v =>
    v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan ||
    v.ViewType == ViewType.EngineeringPlan || v.ViewType == ViewType.AreaPlan ||
    v.ViewType == ViewType.Section || v.ViewType == ViewType.Elevation ||
    v.ViewType == ViewType.ThreeD;

var views = new List<View>();
if (wantedViews.Count == 0) {
    var av = uidoc.ActiveView;
    if (av != null && !av.IsTemplate && plottable(av)) views.Add(av);
} else {
    views = new FilteredElementCollector(doc).OfClass(typeof(View)).OfType<View>()
        .Where(v => !v.IsTemplate && plottable(v) &&
                    wantedViews.Any(n => v.Name.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                                         v.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
        .ToList();
}
if (views.Count == 0) return "No target view matched. Tick at least one view, or open the view you want and leave the list empty.";

var blank = new OverrideGraphicSettings();

Func<OverrideGraphicSettings, bool> isOverridden = o =>
    o.ProjectionLineColor.IsValid || o.CutLineColor.IsValid ||
    o.SurfaceForegroundPatternColor.IsValid || o.SurfaceBackgroundPatternColor.IsValid ||
    o.CutForegroundPatternColor.IsValid || o.CutBackgroundPatternColor.IsValid ||
    o.Halftone || o.Transparency > 0;

ICollection<ElementId> deepIds = null;
if (deep)
    deepIds = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds();

var perView = new List<string>();
int total = 0;

foreach (var v in views) {
    ICollection<ElementId> ids = deepIds;
    if (ids == null) {
        try { ids = new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType().ToElementIds(); }
        catch { perView.Add(v.Name + " : skipped"); continue; }
    }
    int n = 0;
    foreach (var id in ids) {
        try {
            var o = v.GetElementOverrides(id);
            if (o != null && isOverridden(o)) { v.SetElementOverrides(id, blank); n++; }
        } catch { }
    }
    perView.Add(v.Name + " : " + n);
    total += n;
}

return new Dictionary<string, object> {
    { "clearedOverrides", total },
    { "viewsProcessed", views.Count },
    { "mode", deep ? "deep scan" : "visible elements" },
    { "perView", perView.Take(60).ToList() }
};
