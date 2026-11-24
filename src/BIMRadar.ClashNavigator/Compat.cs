using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.Revit.DB;

namespace BIMRadar.ClashNavigator
{
    /// <summary>
    /// The ONE place for Revit-version and runtime differences.
    /// Check every Revit API member against 2022-2027 before adding it here.
    /// </summary>
    internal static class Compat
    {
        /// <summary>Runtime description, e.g. ".NET Framework 4.8.9300.0" or ".NET 8.0.11".</summary>
        public static string RuntimeDescription
        {
            get { return RuntimeInformation.FrameworkDescription; }
        }

        // ElementId from a number in every Revit version (2022/2023: int constructor, 2024+: long constructor)
        public static ElementId Eid(long v)
        {
            var ctor = typeof(ElementId).GetConstructor(new[] { typeof(long) });
            if (ctor != null) return (ElementId)ctor.Invoke(new object[] { v });
            return (ElementId)typeof(ElementId).GetConstructor(new[] { typeof(int) }).Invoke(new object[] { (int)v });
        }

        // #81: integer value of an ElementId in every Revit version (2024+: Value (long); 2022/2023: the int property). Reflection, no direct member call.
        public static long IdValue(ElementId id)
        {
            if (id == null) return long.MinValue;
            var p = typeof(ElementId).GetProperty("Value");
            if (p == null) p = typeof(ElementId).GetProperty("IntegerValue");
            if (p == null) return long.MinValue;
            return Convert.ToInt64(p.GetValue(id, null));
        }

        // #81: set a RevitLinkInstance to "By host view" in view v. View.GetLinkOverrides / SetLinkOverrides and RevitLinkGraphicsSettings
        // (property LinkVisibilityType, enum value ByHostView) are looked up by reflection: nothing breaks at compile time if a version lacks them.
        // Returns null when nothing had to change, else a short note (what it was / why it was left). Call inside a transaction. Never throws.
        public static string LinkToHostView(View v, ElementId linkId)
        {
            try
            {
                var get = typeof(View).GetMethod("GetLinkOverrides", new[] { typeof(ElementId) });
                if (get == null) return "link display settings not available in this Revit version";
                var set = typeof(View).GetMethod("SetLinkOverrides", new[] { typeof(ElementId), get.ReturnType });
                if (set == null) return "link display settings not available in this Revit version";
                object gs = get.Invoke(v, new object[] { linkId });
                if (gs == null) return null;
                var prop = gs.GetType().GetProperty("LinkVisibilityType");
                if (prop == null) return "link display type could not be read";
                string cur = Convert.ToString(prop.GetValue(gs, null));
                if (cur == "ByHostView") return null;
                if (!prop.CanWrite) return "link display is " + cur + " (left as is)";
                prop.SetValue(gs, Enum.Parse(prop.PropertyType, "ByHostView"), null);
                set.Invoke(v, new object[] { linkId, gs });
                return "link display was " + cur + ", set to By host view in this view";
            }
            catch (Exception ex)
            {
                var m = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return "link display not changed (" + m + ")";
            }
        }
    }
}
