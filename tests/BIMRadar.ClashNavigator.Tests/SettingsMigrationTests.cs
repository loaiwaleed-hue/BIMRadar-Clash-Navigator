using System;
using System.IO;
using System.Linq;
using BIMRadar.ClashNavigator;
using Xunit;

public class SettingsMigrationTests : IDisposable {
  readonly string root = Path.Combine(Path.GetTempPath(), "bimradar_t4_" + Guid.NewGuid().ToString("N"));
  string NewDir { get { return Path.Combine(root, "new"); } }
  string OldDir { get { return Path.Combine(root, "old"); } }

  public void Dispose() {
    try { Directory.Delete(root, true); } catch (IOException) { }
  }

  static string[] Names(string dir) {
    return Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
  }

  void MakeOld() {
    Directory.CreateDirectory(OldDir);
    File.WriteAllText(Path.Combine(OldDir, "last_source.txt"), "Revizto");
    File.WriteAllText(Path.Combine(OldDir, "model_search_clash.txt"), "ARC");
  }

  [Fact]
  public void EmptyTarget_WithSource_CopiesAndWritesMarker() {
    MakeOld();
    var r = SettingsMigration.EnsureMigrated(NewDir, OldDir);
    Assert.Equal(NewDir, r);
    Assert.Equal(new[] { "last_source.txt", "migrated_from_aicon.txt", "model_search_clash.txt" }, Names(NewDir));
    Assert.Equal("Revizto", File.ReadAllText(Path.Combine(NewDir, "last_source.txt")));
    var marker = File.ReadAllText(Path.Combine(NewDir, "migrated_from_aicon.txt"));
    Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), marker);
    Assert.Contains(OldDir, marker);
  }

  [Fact]
  public void TargetWithFiles_CopiesNothing() {
    MakeOld();
    Directory.CreateDirectory(NewDir);
    File.WriteAllText(Path.Combine(NewDir, "existing.txt"), "x");
    SettingsMigration.EnsureMigrated(NewDir, OldDir);
    Assert.Equal(new[] { "existing.txt" }, Names(NewDir));
  }

  [Fact]
  public void NoSource_JustCreatesDir() {
    var r = SettingsMigration.EnsureMigrated(NewDir, OldDir);
    Assert.Equal(NewDir, r);
    Assert.True(Directory.Exists(NewDir));
    Assert.Empty(Directory.GetFiles(NewDir));
  }

  [Fact]
  public void PartialSource_OnlyTxtCopied() {
    MakeOld();
    File.WriteAllText(Path.Combine(OldDir, "notes.json"), "{}");
    Directory.CreateDirectory(Path.Combine(OldDir, "sub"));
    File.WriteAllText(Path.Combine(OldDir, "sub", "deep.txt"), "d");
    SettingsMigration.EnsureMigrated(NewDir, OldDir);
    Assert.DoesNotContain("notes.json", Names(NewDir));
    Assert.Equal(new[] { "last_source.txt", "migrated_from_aicon.txt", "model_search_clash.txt" }, Names(NewDir));
    Assert.False(Directory.Exists(Path.Combine(NewDir, "sub")));
  }

  [Fact]
  public void Source_IsUnchanged() {
    MakeOld();
    File.WriteAllText(Path.Combine(OldDir, "notes.json"), "{}");
    var before = Names(OldDir);
    var content = File.ReadAllText(Path.Combine(OldDir, "last_source.txt"));
    SettingsMigration.EnsureMigrated(NewDir, OldDir);
    Assert.Equal(before, Names(OldDir));
    Assert.Equal(content, File.ReadAllText(Path.Combine(OldDir, "last_source.txt")));
  }
}
