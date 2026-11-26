using System.IO;
using System.Linq;
using BIMRadar.ClashNavigator;
using Xunit;

// #94: synthetic Navisworks (Tabular) reports in fixtures/, no client data.
public class ClashReportParserTests {
  static ClashReportResult Load(string name) {
    return ClashReportParser.Parse(File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "fixtures", name)));
  }

  [Fact]
  public void DefaultContents() {
    var r = Load("default.html");
    Assert.Empty(r.Missing);
    Assert.Empty(r.Warnings);
    Assert.True(r.HasStatus);
    Assert.Equal(2, r.Clashes.Count);
    var c = r.Clashes[0];
    Assert.Equal(1, c.Index);
    Assert.Equal("Test 1", c.Test);
    Assert.Equal("Clash1", c.Name);
    Assert.Equal("New", c.Status);
    Assert.Equal("A-1 : LEVEL 01", c.Grid);
    Assert.Equal("Hard", c.Description);
    Assert.Equal("", c.Group);
    Assert.True(c.DefaultTest);
    Assert.Equal("default_files\\cd000001.jpg", c.ImgRelPath);
    Assert.Equal(101.0, c.X); Assert.Equal(201.0, c.Y); Assert.Equal(7.0, c.Z);
    Assert.Equal(new[] { "PROJ-A-AR.nwc", "PROJ-B-ST.nwc" }, c.Models.ToArray());
    Assert.Equal(2, c.Items.Count);
    Assert.Equal(123, c.Items[0].Id); Assert.Equal("PROJ-A-AR.nwc", c.Items[0].Model);
    Assert.Equal(456, c.Items[1].Id); Assert.Equal("PROJ-B-ST.nwc", c.Items[1].Model);
    Assert.Equal("File > File > PROJ-A-AR.nwc > GROUND FLOOR > Walls > Basic Wall > W1", c.Items[0].Path);
    Assert.Equal("Clash2", r.Clashes[1].Name);
    Assert.Equal(124, r.Clashes[1].Items[0].Id);
  }

  [Fact]
  public void ExtraColumns_LayerQuickPropertiesAssignedTo() {
    var r = Load("extra_columns.html");
    Assert.Empty(r.Missing);
    Assert.Empty(r.Warnings);
    Assert.Equal(2, r.Clashes.Count);
    var c = r.Clashes[0];
    Assert.Equal("Clash1", c.Name);
    Assert.Equal("New", c.Status);
    Assert.Equal(101.0, c.X);
    Assert.Equal(123, c.Items[0].Id);
    Assert.Equal(456, c.Items[1].Id);
    Assert.Equal("PROJ-A-AR.nwc", c.Items[0].Model);
    Assert.Equal("PROJ-B-ST.nwc", c.Items[1].Model);
    Assert.EndsWith("> W1", c.Items[0].Path);
  }

  [Fact]
  public void NoSummary_EachMainTableIsATest() {
    var r = Load("no_summary.html");
    Assert.Empty(r.Missing);
    Assert.Equal(2, r.Clashes.Count);
    Assert.Equal("Test 1", r.Clashes[0].Test);
    Assert.True(r.Clashes[0].DefaultTest);   // from the Status column
  }

  [Fact]
  public void NoImage_WarnsAndHasNoPicture() {
    var r = Load("no_image.html");
    Assert.Contains("Image", r.Warnings);
    Assert.Empty(r.Missing);
    Assert.Single(r.Clashes);
    Assert.Equal("", r.Clashes[0].ImgRelPath);
    Assert.Equal("Clash1", r.Clashes[0].Name);
    Assert.Equal(123, r.Clashes[0].Items[0].Id);
  }

  [Fact]
  public void NoPath_WarnsAndModelIsEmpty() {
    var r = Load("no_path.html");
    Assert.Contains("Item Path", r.Warnings);
    Assert.Empty(r.Missing);
    Assert.Single(r.Clashes);
    Assert.Empty(r.Clashes[0].Models);
    Assert.Equal(2, r.Clashes[0].Items.Count);
    Assert.Equal("", r.Clashes[0].Items[0].Model);
    Assert.Equal(123, r.Clashes[0].Items[0].Id);
  }

  [Fact]
  public void NoItemId_IsMissingAndYieldsNoClashItems() {
    var r = Load("no_item_id.html");
    Assert.Contains("Item ID", r.Missing);
    Assert.Empty(r.Clashes.SelectMany(c => c.Items));
  }

  [Fact]
  public void TwoTests_InOneReport() {
    var r = Load("two_tests.html");
    Assert.Equal(3, r.Clashes.Count);
    Assert.Equal(new[] { "Test 1", "Test 1", "Test 2" }, r.Clashes.Select(c => c.Test).ToArray());
    Assert.Equal(new[] { 1, 2, 3 }, r.Clashes.Select(c => c.Index).ToArray());
    Assert.Equal("Clash1", r.Clashes[2].Name);
  }

  [Fact]
  public void CombinedPath_UsesInnermostModel() {
    var r = Load("combined_path.html");
    Assert.Single(r.Clashes);
    Assert.Equal("LINK.rvt", r.Clashes[0].Items[0].Model);
    Assert.Equal("PROJ-B-ST.nwc", r.Clashes[0].Items[1].Model);
    Assert.Equal("LINK.rvt", ClashReportParser.ModelOf("File > HOST.nwc > LINK.rvt > L1 > Walls > W1"));
    Assert.Equal("PROJ-A-AR.nwc", ClashReportParser.ModelOf("File > File > PROJ-A-AR.nwc > L1"));
  }

  [Fact]
  public void ClashGroupRow_IsSkipped() {
    var r = Load("group_row.html");
    Assert.Single(r.Clashes);
    Assert.Equal("Clash1", r.Clashes[0].Name);
    Assert.Equal(101.0, r.Clashes[0].X);
  }

  [Fact]
  public void IsClashReport_ChecksContent() {
    Assert.True(ClashReportParser.IsClashReport("<title>Clash Report</title>"));
    Assert.True(ClashReportParser.IsClashReport("<table class=\"mainTable\">"));
    Assert.False(ClashReportParser.IsClashReport("<html>viewpoints</html>"));
  }
}
