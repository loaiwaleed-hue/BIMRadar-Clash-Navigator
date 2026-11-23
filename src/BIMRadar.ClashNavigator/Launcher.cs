using Autodesk.Revit.UI;

namespace BIMRadar.ClashNavigator
{
    /// <summary>Static entry points called by both the pyRevit buttons and the standalone add-in.</summary>
    public static class Launcher
    {
        public static void Hello(UIApplication uiapp)
        {
            string text = "Clash Navigator loaded on Revit " + uiapp.Application.VersionNumber
                + " (" + Compat.RuntimeDescription + ")";
            TaskDialog.Show("BIMRadar", text);
        }
    }
}
