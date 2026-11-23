using System.Runtime.InteropServices;

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
    }
}
