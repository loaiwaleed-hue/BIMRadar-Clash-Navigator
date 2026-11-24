using System;
using Autodesk.Revit.UI;
using BIMRadar.ClashNavigator.Navigator;

namespace BIMRadar.ClashNavigator
{
    /// <summary>Static entry points called by both the pyRevit buttons and the standalone add-in.</summary>
    public static class Launcher
    {
        private const string DialogTitle = "BIMRadar · Clash Viewer";

        public static void Hello(UIApplication uiapp)
        {
            string text = "Clash Viewer loaded on Revit " + uiapp.Application.VersionNumber
                + " (" + Compat.RuntimeDescription + ")";
            TaskDialog.Show("BIMRadar", text);
        }

        /// <summary>Opens the Clash Viewer window (modeless). Shows a dialog only on errors or when the window did not open.</summary>
        public static void Open(UIApplication uiapp)
        {
            Open(uiapp, false);
        }

        /// <summary>#92: forceDialog = true always shows the source dialog (Change source button); false reopens the remembered source of this document.</summary>
        public static void Open(UIApplication uiapp, bool forceDialog)
        {
            string result;
            try
            {
                result = NavigatorEntry.Open(uiapp, forceDialog);
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

        // No ribbon button uses this since the 2026-11-24 owner decision (Next / Previous live in the window); kept for possible later use.
        /// <summary>Steps the open Clash Viewer to the next item (same path as its Next button).</summary>
        public static void Next(UIApplication uiapp)
        {
            Step(true);
        }

        /// <summary>Steps the open Clash Viewer to the previous item (same path as its Previous button).</summary>
        public static void Previous(UIApplication uiapp)
        {
            Step(false);
        }

        private static void Step(bool forward)
        {
            NavForm form = NavForm.Instance;
            if (form == null || form.IsDisposed)
            {
                TaskDialog.Show(DialogTitle, "Open Clash Viewer first.");
                return;
            }

            try
            {
                if (forward) form.GoNext(); else form.GoPrevious();
            }
            catch (Exception ex)
            {
                TaskDialog.Show(DialogTitle, ex.Message);
            }
        }
    }
}
