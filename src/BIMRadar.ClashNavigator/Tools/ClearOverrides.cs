using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMRadar.ClashNavigator.Navigator;

namespace BIMRadar.ClashNavigator.Tools
{
    /// <summary>
    /// #96: port of the AICon routine clear-view-overrides (reference-source/clear-view-overrides), ACTIVE view only, no deep scan.
    /// View.GetElementOverrides, View.SetElementOverrides, View.SetCategoryOverrides and OverrideGraphicSettings
    /// ProjectionLineColor / CutLineColor / Surface* / Cut* pattern colours (Color.IsValid), Halftone and Transparency exist in Revit 2022-2027.
    /// </summary>
    public static class ClearOverrides
    {
        private static bool Plottable(View v)
        {
            return v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan ||
                   v.ViewType == ViewType.EngineeringPlan || v.ViewType == ViewType.AreaPlan ||
                   v.ViewType == ViewType.Section || v.ViewType == ViewType.Elevation ||
                   v.ViewType == ViewType.ThreeD;
        }

        private static bool IsOverridden(OverrideGraphicSettings o)
        {
            return o.ProjectionLineColor.IsValid || o.CutLineColor.IsValid ||
                   o.SurfaceForegroundPatternColor.IsValid || o.SurfaceBackgroundPatternColor.IsValid ||
                   o.CutForegroundPatternColor.IsValid || o.CutBackgroundPatternColor.IsValid ||
                   o.Halftone || o.Transparency > 0;
        }

        public static string Run(UIApplication uiapp)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return "Open a project first.";
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;
            if (view == null || view.IsTemplate || !Plottable(view))
                return "Clear Overrides works in plan, section, elevation and 3D views. Open one of these views first.";

            var blank = new OverrideGraphicSettings();
            int cleared = 0;
            bool tracked = false;
            NavHandler handler = null;
            try
            {
                NavForm form = NavForm.Instance;
                if (form != null && !form.IsDisposed) handler = form.Handler;
            }
            catch (Exception)
            {
            }

            using (var tx = new Transaction(doc, "Clear Overrides"))
            {
                tx.Start();
                try
                {
                    System.Collections.Generic.ICollection<ElementId> ids;
                    try
                    {
                        ids = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElementIds();
                    }
                    catch (Exception)
                    {
                        ids = new System.Collections.Generic.List<ElementId>();
                    }
                    foreach (ElementId id in ids)
                    {
                        try
                        {
                            OverrideGraphicSettings o = view.GetElementOverrides(id);
                            if (o != null && IsOverridden(o))
                            {
                                view.SetElementOverrides(id, blank);
                                cleared++;
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                    if (handler != null)
                    {
                        try { tracked = handler.ResetTrackedCategories(view); } catch (Exception) { }
                    }
                }
                catch (Exception)
                {
                    tx.RollBack();
                    throw;
                }
                if (cleared > 0 || tracked) tx.Commit(); else tx.RollBack();
            }

            if (tracked && handler != null)
            {
                handler.ColorsOn = false;
                try
                {
                    NavForm form = NavForm.Instance;
                    if (form != null && !form.IsDisposed) form.SyncColors();
                }
                catch (Exception)
                {
                }
            }

            if (cleared == 0 && !tracked) return "No overrides in " + view.Name + ".";
            return "Cleared " + cleared + " element override(s) in " + view.Name + ".";
        }
    }
}
