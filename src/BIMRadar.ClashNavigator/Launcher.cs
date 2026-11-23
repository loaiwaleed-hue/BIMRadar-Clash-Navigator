using System;
using Autodesk.Revit.UI;
using BIMRadar.ClashNavigator.Navigator;

namespace BIMRadar.ClashNavigator
{
    /// <summary>Static entry points called by both the pyRevit buttons and the standalone add-in.</summary>
    public static class Launcher
    {
        private const string DialogTitle = "BIMRadar · Clash Navigator";

        public static void Hello(UIApplication uiapp)
        {
            string text = "Clash Navigator loaded on Revit " + uiapp.Application.VersionNumber
                + " (" + Compat.RuntimeDescription + ")";
            TaskDialog.Show("BIMRadar", text);
        }

        /// <summary>Opens the Clash Navigator window (modeless). Shows a dialog only on errors or when the window did not open.</summary>
        public static void Open(UIApplication uiapp)
        {
            string result;
            try
            {
                result = NavigatorEntry.Open(uiapp);
            }
            catch (Exception ex)
            {
                TaskDialog.Show(DialogTitle, ex.Message);
                return;
            }

            bool windowOpen = NavForm.Instance != null && !NavForm.Instance.IsDisposed;
            if (!windowOpen && !string.IsNullOrEmpty(result) && result != "Cancelled.")
            {
                TaskDialog.Show(DialogTitle, result);
            }
        }
    }
}
