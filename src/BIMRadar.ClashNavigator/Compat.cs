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
    }
}
