using System;
using System.Globalization;
using System.IO;

namespace BIMRadar.ClashNavigator {
  /// <summary>
  /// One-time copy of the old AICon settings (%AppData%\AICon\vpnav) into the new settings
  /// folder. Pure System.IO, no Revit references, so it is unit-testable. Best-effort:
  /// IO errors are swallowed. The old folder is never modified.
  /// </summary>
  public static class SettingsMigration {
    public const string MarkerFile = "migrated_from_aicon.txt";

    public static string EnsureMigrated(string newDir, string oldDir) {
      try {
        Directory.CreateDirectory(newDir);
        if (Directory.GetFiles(newDir).Length == 0 && !string.IsNullOrEmpty(oldDir) && Directory.Exists(oldDir)) {
          foreach (var src in Directory.GetFiles(oldDir, "*.txt", SearchOption.TopDirectoryOnly)) {
            try {
              var dst = Path.Combine(newDir, Path.GetFileName(src));
              if (!File.Exists(dst)) File.Copy(src, dst, false);
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
          }
          File.WriteAllText(Path.Combine(newDir, MarkerFile),
            DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Environment.NewLine + oldDir + Environment.NewLine);
        }
      } catch (IOException) { } catch (UnauthorizedAccessException) { }
      return newDir;
    }
  }
}
