using System;
using Autodesk.Revit.UI;

namespace BIMRadar.ClashNavigator.Standalone
{
    public class App : IExternalApplication
    {
        private const string TabName = "BIMRadar";
        private const string PanelName = "Review";

        public Result OnStartup(UIControlledApplication application)
        {
            // The tab may already exist (created by another BIMRadar tool): ignore that error.
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Exception)
            {
            }

            RibbonPanel panel = null;
            foreach (RibbonPanel p in application.GetRibbonPanels(TabName))
            {
                if (p.Name == PanelName)
                {
                    panel = p;
                    break;
                }
            }
            if (panel == null)
            {
                panel = application.CreateRibbonPanel(TabName, PanelName);
            }

            string assemblyPath = typeof(App).Assembly.Location;
            var data = new PushButtonData(
                "BIMRadar_ClashNavigator_Hello",
                "Hello",
                assemblyPath,
                "BIMRadar.ClashNavigator.Standalone.HelloCommand");
            data.ToolTip = "Shows which Revit and .NET runtime the BIMRadar Clash Navigator loaded on.";
            panel.AddItem(data);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
