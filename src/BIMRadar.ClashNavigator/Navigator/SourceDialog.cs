using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace BIMRadar.ClashNavigator.Navigator
{
// "Which kind of data?" dialog shown when the routine starts
public class SourceDialog : WF.Form {
  public string Choice = null;
  static readonly string[] Keys = { "clash", "viewpoints", "revizto" };
  static readonly string[] Titles = { "Navisworks clash test  (clash report, HTML)", "Navisworks viewpoints  (viewpoints XML + viewpoints report)", "Revizto issues  (Excel export)" };
  static readonly string[] Guides = {
    "1. Create a new empty folder, e.g. ...\\Clash 2026-11-23\\\n" +
    "2. Navisworks: Clash Detective > Report tab > Contents: tick Image, Clash Point, Status,\n" +
    "    Item ID (and Grid Location). Report Format: HTML (Tabular).\n" +
    "3. Write Report > save it into the new folder. Only the HTML is needed (no XML).\n" +
    "4. Click Next and pick that .html. Its pictures folder (..._files) and the\n" +
    "    navigator page (web report) are kept in the same folder.",  // owner 2026-11-24: HTML only, no viewpoint export hint
    "1. Create a new empty folder, e.g. ...\\VP 2026-11-23\\\n" +
    "2. Navisworks: Saved Viewpoints > select the viewpoints you need\n" +
    "    (delete the ones you don't need, then select all).\n" +
    "3. Right-click the selection > Export Viewpoints Report...  (.html + pictures) > save in the new folder.\n" +
    "4. Right-click the selection AGAIN > Export Viewpoints...  (.xml) > save in the same folder.\n" +
    "    Steps 3 and 4 are two separate menu entries, one below the other: run both.\n" +
    "5. Click Next and pick that folder. Viewpoint_Navigator.html (web report) is saved there too.",
    "1. Create a new empty folder, e.g. ...\\Revizto 2026-11-23\\\n" +
    "2. Revizto: Issue Tracker > Export issues > Save as Excel (.xlsx) into the new folder.\n" +
    "    Only the Excel is needed.\n" +
    "3. Click Next and pick that .xlsx. Snapshots download to a sub-folder with the Excel name;\n" +
    "    the navigator page <name>_Navigator.html (web report) is saved next to the Excel."
  };


  // small light-bulb picture for the "How to use" heading
  static SD.Bitmap LampIcon() {
    var b = new SD.Bitmap(20, 20);
    using (var g = SD.Graphics.FromImage(b)) {
      g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
      using (var glow = new SD.SolidBrush(SD.Color.FromArgb(70, 255, 200, 40))) g.FillEllipse(glow, 1, 0, 18, 16);
      using (var bulb = new SD.SolidBrush(SD.Color.FromArgb(255, 196, 36))) g.FillEllipse(bulb, 4, 2, 12, 11);
      using (var pen = new SD.Pen(SD.Color.FromArgb(200, 140, 0), 1f)) g.DrawEllipse(pen, 4, 2, 12, 11);
      using (var neck = new SD.SolidBrush(SD.Color.FromArgb(255, 196, 36))) g.FillRectangle(neck, 7.5f, 11, 5, 3);
      using (var basev = new SD.SolidBrush(SD.Color.FromArgb(120, 125, 135))) { g.FillRectangle(basev, 7, 14, 6, 2); g.FillRectangle(basev, 7.5f, 16.5f, 5, 1.5f); }
      using (var pen = new SD.Pen(SD.Color.White, 1f)) g.DrawArc(pen, 6, 4, 5, 5, 180, 90);
    }
    return b;
  }

  public SourceDialog(string preselect) {
    Text = "BIMRadar Clash Viewer - what are you opening?";
    FormBorderStyle = WF.FormBorderStyle.FixedDialog; MinimizeBox = false; MaximizeBox = false;
    StartPosition = WF.FormStartPosition.CenterParent;
    ClientSize = new SD.Size(540, 378);
    Font = new SD.Font("Segoe UI", 9.5f);
    var lbl = new WF.Label { Left = 16, Top = 14, Width = 490, Height = 22, Text = "Choose the kind of data (point at one to see what to export):" };
    var lamp = new WF.PictureBox { Left = 16, Top = 146, Width = 20, Height = 20, Image = LampIcon() };
    var how = new WF.Label { Left = 38, Top = 147, Width = 300, Height = 20, Text = "How to use:", Font = new SD.Font("Segoe UI Semibold", 9.5f), ForeColor = SD.Color.FromArgb(40, 50, 60) };
    var guide = new WF.Label { Left = 16, Top = 170, Width = 508, Height = 146, BorderStyle = WF.BorderStyle.FixedSingle, BackColor = SD.Color.FromArgb(246, 248, 251), Padding = new WF.Padding(8, 6, 8, 6), Font = new SD.Font("Segoe UI", 8.75f), ForeColor = SD.Color.FromArgb(40, 50, 60) };
    var tip = new WF.ToolTip { AutoPopDelay = 30000, InitialDelay = 300 };
    var rb = new WF.RadioButton[3];
    int sel = Math.Max(0, Array.IndexOf(Keys, preselect ?? ""));
    for (int i = 0; i < 3; i++) {
      int k = i;
      rb[i] = new WF.RadioButton { Left = 24, Top = 44 + 30 * i, Width = 480, Height = 26, Text = Titles[i], Checked = i == sel };
      tip.SetToolTip(rb[i], Guides[i]);
      rb[i].MouseEnter += (s, e) => guide.Text = Guides[k];
      rb[i].MouseLeave += (s, e) => { for (int j = 0; j < 3; j++) if (rb[j].Checked) guide.Text = Guides[j]; };
      rb[i].CheckedChanged += (s, e) => { if (rb[k].Checked) guide.Text = Guides[k]; };
    }
    guide.Text = Guides[sel];
    var cancel = new WF.Button { Left = 336, Top = 332, Width = 90, Height = 32, Text = "Cancel", DialogResult = WF.DialogResult.Cancel };
    var ok = new WF.Button { Left = 434, Top = 332, Width = 90, Height = 32, Text = "Next", DialogResult = WF.DialogResult.OK, FlatStyle = WF.FlatStyle.Flat, BackColor = SD.Color.FromArgb(0, 120, 212), ForeColor = SD.Color.White, UseVisualStyleBackColor = false, Font = new SD.Font("Segoe UI Semibold", 9.5f) };
    ok.FlatAppearance.BorderColor = SD.Color.FromArgb(0, 95, 184);
    AcceptButton = ok; CancelButton = cancel;
    Controls.AddRange(new WF.Control[] { lbl, rb[0], rb[1], rb[2], lamp, how, guide, cancel, ok });
    Shown += (s, e) => ok.Focus();
    ok.Click += (s, e) => { for (int j = 0; j < 3; j++) if (rb[j].Checked) Choice = Keys[j]; };
  }
}
}
