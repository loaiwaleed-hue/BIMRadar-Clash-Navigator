using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.Revit.UI;

namespace BIMRadar.ClashNavigator.Standalone
{
    public class App : IExternalApplication
    {
        private const string TabName = "BIMRadar";
        private const string PanelName = "Review";
        private const string ExtensionName = "BIMRadar-ClashNavigator.extension";

        public Result OnStartup(UIControlledApplication application)
        {
            // E3: never show two Review panels. If the pyRevit extension is registered, stay hidden.
            try
            {
                string reason = PyRevitRegistration();
                if (reason != null)
                {
                    WriteLog("pyRevit extension registered (" + reason + "); standalone add-in did not create its ribbon panel.");
                    return Result.Succeeded;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                CreateRibbon(application);
            }
            catch (Exception ex)
            {
                WriteLog("Ribbon creation failed: " + ex.Message);
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void CreateRibbon(UIControlledApplication application)
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

            string asm = typeof(App).Assembly.Location;
            string ns = "BIMRadar.ClashNavigator.Standalone.";

            var openData = new PushButtonData("BIMRadar_ClashNavigator_Open", "Clash\nViewer", asm, ns + "OpenCommand");
            openData.ToolTip = "Step through Navisworks clash tests, saved viewpoints or Revizto issues in Revit, with a web report and Excel export.";
            SetImages(openData, "ClashViewer");

            panel.AddItem(openData);
        }

        private static void SetImages(PushButtonData data, string baseName)
        {
            try
            {
                var large = LoadImage(baseName + "32.png");
                var small = LoadImage(baseName + "16.png");
                if (large != null) data.LargeImage = large;
                if (small != null) data.Image = small;
            }
            catch (Exception)
            {
                // A missing icon must never break startup.
            }
        }

        private static System.Windows.Media.ImageSource LoadImage(string fileName)
        {
            try
            {
                Assembly a = typeof(App).Assembly;
                string name = a.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
                if (name == null) return null;
                using (Stream s = a.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    var img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit();
                    img.StreamSource = s;
                    img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Returns a short reason when the pyRevit extension is registered, otherwise null.</summary>
        private static string PyRevitRegistration()
        {
            string ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "pyRevit_config.ini");
            if (!File.Exists(ini)) return null;
            string text = File.ReadAllText(ini);
            if (text.IndexOf(ExtensionName, StringComparison.OrdinalIgnoreCase) >= 0) return "listed in pyRevit_config.ini";

            foreach (string line in text.Split('\n'))
            {
                string l = line.Trim();
                if (!l.StartsWith("userextensions", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = l.IndexOf('=');
                if (eq < 0) continue;
                string value = l.Substring(eq + 1).Trim();
                // Format: ["C:\\path one", "D:\\path two"] (JSON-style list; backslashes may be escaped).
                foreach (Match m in Regex.Matches(value, "\"([^\"]*)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\").Trim();
                    if (p.Length == 0) continue;
                    try
                    {
                        if (Directory.Exists(Path.Combine(p, ExtensionName))) return "found in " + p;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            return null;
        }

        private static void WriteLog(string text)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIMRadar", "ClashNavigator");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "startup.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }
    }
}
