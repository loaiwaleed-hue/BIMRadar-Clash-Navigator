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
public class NavForm : WF.Form {
  public static NavForm Instance;
  const double FT = 0.3048;
  readonly NavHandler H;
  readonly ExternalEvent Ev;
  WF.Label lblName, lblCount, lblInfo, lblPic;
  WF.Label lblWhy1, lblWhy2;   // #70 / #71: anchor + search-match lines under the snapshot
  WF.Label lblWhy3, lblHint;   // #72 / #73: what is at the location (red when empty / grey summary) + yellow hint
  WF.CheckBox chkHide;         // #72: Hide empty (Revizto)
  WF.Button btnCheck, btnGoto; // #72: Check locations; #73: Go to element
  int keepN = 0;               // item that stays in the list while Hide empty is on (the one just opened)
  WF.Button btnPrev, btnNext, btnSnap, btnOpen, btnWeb;
  WF.PictureBox pic;
  WF.RadioButton rbBefore, rbAfter;
  string lastSaved;
  WF.ComboBox cmb, cmbModel, cmbTest;
  List<string> testKeys = new List<string>();
  bool isClash;
  WF.CheckBox chk;
  WF.NumericUpDown num;
  bool syncing;
  int curN;
  List<string> modelKeys = new List<string>(); // "" = all
  string appliedKey = null;                    // model filter that H.Order currently reflects
  string sourceWord;

  WF.Button btnChange;   // #92
  WF.Button btnIso, btnDup, btnColor;   // #80: btnColor = Clear / Create override (clash source only)
  WF.ComboBox cmbSt;
  static readonly string[] StKeys = { "open", "closed", "clarify", "others" };
  WF.Label lblDone;
  WF.TextBox txtComment, txtSub;
  WF.Button btnPost;
  bool editingComment = true;
  WF.ComboBox cmbSub, cmbSubSt;
  WF.Button[] btnPick = new WF.Button[2];
  WF.Label[] lblItem = new WF.Label[2];
  WF.Timer subTimer = new WF.Timer { Interval = 350 };
  WF.TextBox txtMS;                                    // model-name search (building code)
  WF.Timer msTimer = new WF.Timer { Interval = 700 };
  string modelPat = "";                                // applied model-name search
  bool anyModel, isRevizto, srcStatus;
  WF.FlowLayoutPanel tagPanel;                         // search results as tags (models / zone / room / not located)
  Dictionary<int, string> hitGroup = new Dictionary<int, string>();   // VpCam.Index -> search group
  List<string> tagKeys = new List<string>();
  Dictionary<string, bool> tagOn = new Dictionary<string, bool>();
  List<string> fullItems = new List<string>(), fullKeys = new List<string>();   // model list without a search
  List<string> searchKeys = new List<string>();        // model list entries while a search is active ("*" = all results, "?" = custom)
  int fullIndex = 0;
  WF.ComboBox cmbStamp;
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
  int pendingN = 0;   // item whose comment is waiting to be saved

  public NavForm(NavHandler h, ExternalEvent ev, string defaultModel) {
    H = h; Ev = ev;
    isClash = H.Source.StartsWith("Navisworks clash");
    isRevizto = H.Source.StartsWith("Revizto");
    srcStatus = isClash || isRevizto;
    bool isVp = H.Source.StartsWith("Navisworks viewpoints");
    sourceWord = H.Source.StartsWith("Revizto") ? "issues" : isClash ? "clashes" : "viewpoints";
    Text = "BIMRadar Clash Viewer - " + H.Source;
    FormBorderStyle = WF.FormBorderStyle.FixedSingle;
    MinimizeBox = true; MaximizeBox = false;
    ShowInTaskbar = true; // so a minimized window can be restored from the taskbar
    StartPosition = WF.FormStartPosition.Manual;
    var wa = WF.Screen.PrimaryScreen.WorkingArea;
    int clientH = Math.Min(isClash ? 1082 : isRevizto ? 1080 : 1002, wa.Height - 50);
    ClientSize = new SD.Size(380, clientH);
    Location = new SD.Point(wa.Right - Width - 24, Math.Max(wa.Top, Math.Min(wa.Top + 30, wa.Bottom - Height)));
    Font = new SD.Font("Segoe UI", 9.5f);
    KeyPreview = true;
    var blue = SD.Color.FromArgb(0, 120, 212);

    lblName = new WF.Label { Left = 12, Top = 10, Width = 282, Height = 26, Font = new SD.Font("Segoe UI Semibold", 12f), AutoEllipsis = true, Text = "Loading..." };
    btnChange = new WF.Button { Left = 298, Top = 10, Width = 70, Height = 26, Text = "\u25C2 Back", Font = new SD.Font("Segoe UI", 8.5f) };   // #92: top right, same row as the title (title narrowed to 252 px), Previous / Next stay below
    lblCount = new WF.Label { Left = 12, Top = 38, Width = 356, Height = 20, ForeColor = SD.Color.DimGray, AutoEllipsis = true, Text = "" };
    lblInfo = new WF.Label { Left = 12, Top = 60, Width = 356, Height = 34, ForeColor = SD.Color.DimGray, Text = "" };
    btnPrev = new WF.Button { Left = 12, Top = 102, Width = 176, Height = 46, Text = "<  Previous", Font = new SD.Font("Segoe UI Semibold", 11f) };
    btnNext = new WF.Button { Left = 192, Top = 102, Width = 176, Height = 46, Text = "Next  >", Font = new SD.Font("Segoe UI Semibold", 11f), FlatStyle = WF.FlatStyle.Flat, BackColor = blue, ForeColor = SD.Color.White, UseVisualStyleBackColor = false };
    btnNext.FlatAppearance.BorderColor = SD.Color.FromArgb(0, 95, 184);
    int y = 160;
    var lblModel = new WF.Label { Left = 12, Top = y + 3, Width = 48, Height = 20, Text = "Model" };
    cmbModel = new WF.ComboBox { Left = 62, Top = y, Width = 306, DropDownStyle = WF.ComboBoxStyle.DropDownList, DropDownWidth = 520 };
    y += 34;
    var lblMS = new WF.Label { Left = 12, Top = y + 3, Width = 50, Height = 20, Text = "Search" };
    txtMS = new WF.TextBox { Left = 62, Top = y, Width = 306 };
    txtMS.HandleCreated += (s, e) => { try { SendMessage(txtMS.Handle, 0x1501, (IntPtr)1, "Building / Volume code / Sequential number"); } catch (Exception) { } };
    y += 30;
    tagPanel = new WF.FlowLayoutPanel { Left = 12, Top = y, Width = 356, Height = 58, AutoScroll = true, WrapContents = true, BackColor = SD.Color.FromArgb(248, 248, 248), BorderStyle = WF.BorderStyle.None };
    y += 62;
    var lblTest = new WF.Label { Left = 12, Top = y + 3, Width = 48, Height = 20, Text = "Test", Visible = isClash };
    cmbTest = new WF.ComboBox { Left = 62, Top = y, Width = 306, DropDownStyle = WF.ComboBoxStyle.DropDownList, DropDownWidth = 420, Visible = isClash };
    if (isClash) y += 34;
    var lblSub = new WF.Label { Left = 12, Top = y + 3, Width = 48, Height = 20, Text = "Filter" };
    cmbSub = new WF.ComboBox { Left = 62, Top = y, Width = 112, DropDownStyle = WF.ComboBoxStyle.DropDownList };
    cmbSub.Items.AddRange(new object[] { "No filter", isClash ? "Clash name" : "Name", isClash ? "Clash status" : isRevizto ? "Revizto status" : "Status" });
    if (isRevizto) cmbSub.Items.Add("Stamp");
    cmbStamp = new WF.ComboBox { Left = 180, Top = y, Width = 188, DropDownStyle = WF.ComboBoxStyle.DropDown, Visible = false };
    foreach (var g in H.Cams.Where(c => c.Stamp != "").GroupBy(c => c.Stamp, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key)) cmbStamp.Items.Add(g.Key + "   (" + g.Count() + ")");
    txtSub = new WF.TextBox { Left = 180, Top = y, Width = 188, Visible = false };
    cmbSubSt = new WF.ComboBox { Left = 180, Top = y, Width = 188, DropDownStyle = WF.ComboBoxStyle.DropDownList, Visible = false };
    if (srcStatus) {
      // status from the source (Navisworks clash status / Revizto status), not the report status below
      var order = new List<string> { "New", "Open", "Active", "In progress", "Reviewed", "Approved", "Solved", "Resolved", "Closed" };
      foreach (var s0 in H.Cams.Select(c => c.Status ?? "").Where(s0 => s0 != "").Distinct().OrderBy(s0 => order.IndexOf(s0) < 0 ? 99 : order.IndexOf(s0))) cmbSubSt.Items.Add(s0);
      if (cmbSubSt.Items.Count == 0) cmbSubSt.Items.Add("(no status in the report)");
    } else {
      cmbSubSt.DrawMode = WF.DrawMode.OwnerDrawFixed; cmbSubSt.ItemHeight = 17;
      cmbSubSt.DrawItem += DrawStatusItem;
      cmbSubSt.Items.AddRange(new object[] { "Open", "Closed", "Need clarification", "Action by others" });
    }
    y += 34;
    // #72: Hide empty + Check locations (Revizto only)
    chkHide = new WF.CheckBox { Left = 12, Top = y, Width = 108, Height = 24, Text = "Hide empty", Visible = isRevizto };
    btnCheck = new WF.Button { Left = 124, Top = y - 1, Width = 244, Height = 26, Text = "Check locations", Visible = isRevizto };
    if (isRevizto) y += 30;
    cmb = new WF.ComboBox { Left = 12, Top = y, Width = 356, DropDownStyle = WF.ComboBoxStyle.DropDownList, DropDownWidth = 520 };
    y += 36;
    for (int i = 0; i < 2; i++) {
      int k = i;
      btnPick[i] = new WF.Button { Left = 12, Top = y, Width = 30, Height = 26, Text = "", Image = IdIcon(), ImageAlign = SD.ContentAlignment.MiddleCenter, Visible = isClash, Enabled = false };
      lblItem[i] = new WF.Label { Left = 48, Top = y + 4, Width = 320, Height = 20, AutoEllipsis = true, Visible = isClash, Text = "Item " + (i + 1) };
      btnPick[i].Click += (s, e) => { H.PickIndex = k; H.Action = "Pick"; Ev.Raise(); };
      if (isClash) y += 30;
    }
    chk = new WF.CheckBox { Left = 12, Top = y, Width = 150, Height = 24, Text = "Section box", Checked = true };
    var lblSize = new WF.Label { Left = 170, Top = y + 3, Width = 110, Height = 20, Text = "Box size (m):" };
    num = new WF.NumericUpDown { Left = 280, Top = y - 1, Width = 88, DecimalPlaces = 1, Increment = 0.5m, Minimum = 0.5m, Maximum = 50m, Value = (decimal)Math.Min(50.0, Math.Max(0.5, H.BoxM)) };
    y += 32;
    bool noIso = !isClash;   // #93: Isolate / Unisolate button for the clash source ONLY (hidden for Revizto and viewpoints; Revizto never isolates, #69)
    // #85: clash: row A = [Unisolate | Clear override], row B = [Duplicate 3D view | Save snapshot]; Revizto / VP keep the old layout
    btnIso = new WF.Button { Left = 12, Top = y, Width = 176, Height = 34, Text = isClash ? "Unisolate" : "Isolate in 3D view", Visible = !noIso, Enabled = false };   // #82
    btnColor = new WF.Button { Left = 192, Top = y, Width = 176, Height = 34, Text = "Clear override", Visible = isClash };   // #80 / #85: clash only, right of Unisolate
    if (isClash) {
      y += 40;
      btnDup = new WF.Button { Left = 12, Top = y, Width = 176, Height = 34, Text = "Duplicate 3D view" };
      btnSnap = new WF.Button { Left = 192, Top = y, Width = 176, Height = 34, Text = "Save snapshot", Font = new SD.Font("Segoe UI Semibold", 9.5f) };
    } else {
      btnSnap = new WF.Button { Left = noIso ? 12 : 192, Top = y, Width = noIso ? 356 : 176, Height = 34, Text = "Save snapshot", Font = new SD.Font("Segoe UI Semibold", 9.5f) };
      y += 40;
      btnDup = new WF.Button { Left = 12, Top = y, Width = 356, Height = 32, Text = "Duplicate 3D view" };
    }
    btnGoto = new WF.Button { Left = 248, Top = y, Width = 120, Height = 32, Text = "Go to element", Visible = false };   // #73: shown only when an element is far from the point
    y += 38;
    var lblAct = new WF.Label { Left = 12, Top = y, Width = 84, Height = 16, ForeColor = SD.Color.DimGray, Font = new SD.Font("Segoe UI", 8.5f), Text = "Action status" };
    y += 16;
    cmbSt = new WF.ComboBox { Left = 12, Top = y + 4, Width = 124, DropDownWidth = 160, DropDownStyle = WF.ComboBoxStyle.DropDownList, Font = new SD.Font("Segoe UI Semibold", 9.5f), DrawMode = WF.DrawMode.OwnerDrawFixed, ItemHeight = 18 };
    cmbSt.DrawItem += DrawStatusItem;
    cmbSt.Items.AddRange(new object[] { "Open", "Closed", "Need clarification", "Action by others" });
    lblDone = new WF.Label { Left = 96, Top = y - 16, Width = 272, Height = 16, ForeColor = SD.Color.Gray, Font = new SD.Font("Segoe UI", 8f), TextAlign = SD.ContentAlignment.TopRight, AutoEllipsis = true, Text = "" };
    btnWeb = new WF.Button { Left = 214, Top = y, Width = 86, Height = 32, Text = "Web report" };
    btnOpen = new WF.Button { Left = 304, Top = y, Width = 64, Height = 32, Text = "Folder" };
    y += 38;
    var lblCm = new WF.Label { Left = 12, Top = y, Width = 356, Height = 18, ForeColor = SD.Color.DimGray, Font = new SD.Font("Segoe UI", 8.5f), Text = "Comment  (Enter = post, Shift+Enter = new line)" };
    y += 18;
    txtComment = new WF.TextBox { Left = 12, Top = y, Width = 288, Height = 44, Multiline = true, ScrollBars = WF.ScrollBars.Vertical };
    btnPost = new WF.Button { Left = 304, Top = y, Width = 64, Height = 44, Text = "Post", Font = new SD.Font("Segoe UI Semibold", 9.5f) };
    y += 50;
    rbBefore = new WF.RadioButton { Left = 12, Top = y, Width = 150, Height = 22, Text = H.Source.StartsWith("Revizto") ? "Revizto snapshot" : isClash ? "Clash picture" : "Navisworks", Checked = true };
    rbAfter = new WF.RadioButton { Left = 166, Top = y, Width = 150, Height = 22, Text = "Solved snapshot" };
    y += 26;
    int below = isRevizto ? 122 : 74;   // #70/#71: 34 px for the why lines; #72/#73: Revizto +48 for the location line and the hint
    int picH = Math.Max(140, clientH - y - below);
    pic = new WF.PictureBox { Left = 12, Top = y, Width = 356, Height = picH, SizeMode = WF.PictureBoxSizeMode.Zoom, BorderStyle = WF.BorderStyle.FixedSingle, BackColor = SD.Color.FromArgb(30, 30, 30), Cursor = WF.Cursors.Hand };
    lblPic = new WF.Label { Left = 12, Top = y + picH + 4, Width = 356, Height = 32, ForeColor = SD.Color.Gray, Font = new SD.Font("Segoe UI", 8.5f), Text = "" };
    // #70 / #71: two small lines under the file-name line
    lblWhy1 = new WF.Label { Left = 12, Top = y + picH + 38, Width = 356, Height = 16, Font = new SD.Font("Segoe UI", 8.5f), AutoEllipsis = true, ForeColor = SD.Color.Gray, Text = "" };
    lblWhy2 = new WF.Label { Left = 12, Top = y + picH + 54, Width = 356, Height = 16, Font = new SD.Font("Segoe UI", 8.5f), AutoEllipsis = true, ForeColor = SD.Color.Gray, Text = "" };
    lblWhy3 = new WF.Label { Left = 12, Top = y + picH + 70, Width = 356, Height = 16, Font = new SD.Font("Segoe UI", 8.5f), AutoEllipsis = true, ForeColor = SD.Color.Gray, Text = "" };
    lblHint = new WF.Label { Left = 12, Top = y + picH + 88, Width = 356, Height = 32, Font = new SD.Font("Segoe UI", 8.5f), ForeColor = SD.Color.FromArgb(90, 60, 0), BackColor = SD.Color.FromArgb(255, 243, 191), Visible = false, Text = "" };
    if (y + picH + below > clientH) ClientSize = new SD.Size(380, y + picH + below);

    var tip = new WF.ToolTip();
    if (isClash) tip.SetToolTip(btnSnap, "Solved snapshot in 2 steps: click, set up the Revit view as the solved picture should look, then click Capture view: the whole active view is saved to <picture>_after.jpg in:\n" + (H.PicDir ?? "") + "\nShortcut: Ctrl+S");
    else tip.SetToolTip(btnSnap, "Exports the VP view's frame (same field of view as the source picture) to <picture>_after.jpg in:\n" + (H.PicDir ?? "") + "\nShortcut: Ctrl+S");
    tip.SetToolTip(btnWeb, "Open the web navigator for this project");
    tip.SetToolTip(btnOpen, H.PicDir ?? "");
    tip.SetToolTip(pic, "Click to open the picture full size");
    tip.SetToolTip(cmbModel, "One model, or all models. Use Search below to show several models by part of their name.");
    tip.SetToolTip(txtMS, "Building / volume code or sequential number, as in the ISO 19650 file name (e.g. D0, 33, 03306): matches a whole field of the name.\n1st the anchor model; issues without an anchor model by Zone, then Room. 'Other anchor' = anchored to another model but zone / room match.\nIssues without any location show as 'Not located' (off until you click it).\n* and ? are wildcards, ; separates several codes (D0; D1). Results show as tags below: click a tag to hide / show it.\nShown items open in 3D even when anchored to another model. Remembered for next time.");
    if (isClash) tip.SetToolTip(cmbSub, "Clash status = the Navisworks status from the clash report (New / Active / Reviewed / Approved / Resolved). The coloured Status below is your report status.");
    if (isRevizto) tip.SetToolTip(cmbSub, "Revizto status = the status in the Revizto Excel (Open / In progress / Solved / Closed). Stamp = stamp abbreviation; several with ; (AMP; SEC = either). The coloured Status below is your report status.");
    tip.SetToolTip(cmbStamp, "Stamp abbreviation. Several separated by ; show any of them (AMP; SEC).");
    tip.SetToolTip(btnIso, isClash ? "Clash: Unisolate shows the whole model inside the section box (colours kept). Isolate shows only the clashing elements again." : "Temporarily isolate the found elements in the VP view. Click again to restore the original clash view.");
    tip.SetToolTip(btnPick[0], "Select by ID: select this element in Revit (found by its Element ID)");
    tip.SetToolTip(btnPick[1], "Select by ID: select this element in Revit (found by its Element ID)");
    tip.SetToolTip(btnDup, "Orthographic copy of this view (WIP-Clash Viewer-VP-...): same direction and section box, all annotations off. Fix the issue there; the VP view stays for the snapshot.");
    tip.SetToolTip(cmbSt, "Action status (your own): Open / Closed / Need clarification / Action by others (another contractor has to act). The Revizto / clash status from the source is shown in the info line above and in Filter. Shared with the web navigator (navigator_status.json in the picture folder). New items start as Open.");
    Controls.AddRange(new WF.Control[] { btnChange, lblName, lblCount, lblInfo, btnPrev, btnNext, lblModel, cmbModel, lblMS, txtMS, tagPanel, lblTest, cmbTest, lblSub, cmbSub, txtSub, cmbSubSt, cmbStamp, lblAct, cmb,
      btnPick[0], lblItem[0], btnPick[1], lblItem[1], chk, lblSize, num, btnIso, btnSnap, btnDup, btnColor, cmbSt, lblDone, btnWeb, btnOpen, lblCm, txtComment, btnPost, rbBefore, rbAfter, pic, lblPic, lblWhy1, lblWhy2, lblWhy3, lblHint, chkHide, btnCheck, btnGoto });

    // model list: "All" first, then every model with its count; default = the model that matches the open file
    var groups = H.Cams.SelectMany(c => c.Models.Count > 0 ? c.Models : new List<string> { c.Model ?? "" }).GroupBy(m => m, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key == "" ? "~" : g.Key, StringComparer.OrdinalIgnoreCase).ToList();
    anyModel = groups.Any(g => g.Key != "");
    cmbModel.Items.Add("All models (" + H.Cams.Count + " " + sourceWord + ")"); modelKeys.Add("");
    foreach (var g in groups) { cmbModel.Items.Add((g.Key == "" ? "(no anchored model)" : g.Key) + "   (" + g.Count() + ")"); modelKeys.Add(g.Key == "" ? "\u0001none" : g.Key); }
    int def = 0;
    if (!string.IsNullOrEmpty(defaultModel)) { int k = modelKeys.IndexOf(defaultModel); if (k > 0) def = k; }
    cmbModel.SelectedIndex = def;
    fullItems = cmbModel.Items.Cast<object>().Select(o => o.ToString()).ToList(); fullKeys = modelKeys.ToList(); fullIndex = def;
    anyModel = anyModel || H.Cams.Any(c => c.Zone != "" || c.Room != "");
    cmbModel.Enabled = anyModel;
    txtMS.Enabled = anyModel;
    string savedPat = ReadPref();
    if (savedPat != "" && BuildSearch(savedPat)) { modelPat = savedPat; txtMS.Text = savedPat; }
    else ShowTagsHint();
    cmbSub.SelectedIndex = 0; cmbSubSt.SelectedIndex = 0;
    if (isClash) cmbTest.SelectedIndexChanged += (s, e) => { if (!syncing) ApplyFilter(true); };
    ApplyFilter(false);

    btnNext.Click += (s, e) => Go("Next", 0);
    btnPrev.Click += (s, e) => Go("Prev", 0);
    btnSnap.Click += (s, e) => SnapClick();   // #100
    btnIso.Click += (s, e) => Go("Isolate", 0);
    btnChange.Click += (s, e) => { FlushComment(); H.ChangingSource = true; H.Action = "ChangeSource"; Ev.Raise(); };   // #92: the Revit API call (reopen with the dialog) runs in the ExternalEvent handler
    tip.SetToolTip(btnChange, "Back to the source dialog: choose another folder or report, or another source (clash report / viewpoints / Revizto).");
    btnDup.Click += (s, e) => Go("Duplicate", 0);
    btnGoto.Click += (s, e) => Go("GotoElement", 0);   // #73
    btnColor.Click += (s, e) => Go("ToggleColors", 0);   // #80
    tip.SetToolTip(btnColor, "Clash colours in the current view: the element in this model red (solid fill), the linked element green (its link). Click to clear them; the button then re-applies them.");
    btnCheck.Click += (s, e) => { ShowMessage("Checking locations..."); Go("CheckAll", 0); };   // #72
    chkHide.CheckedChanged += (s, e) => { if (!syncing) SubFilterChanged(); };   // #72
    tip.SetToolTip(chkHide, "Hide issues that have nothing from this model at their location (found by visiting them or by Check locations). Issues not checked yet stay in the list.");
    tip.SetToolTip(btnCheck, "Count model elements (this model and its links) at every issue's location, without changing the view.");
    tip.SetToolTip(btnGoto, "Section box and camera around the element found by its Element ID, instead of the Revizto point.");
    btnOpen.Click += (s, e) => OpenFolder();
    btnWeb.Click += (s, e) => OpenWeb();
    rbBefore.CheckedChanged += (s, e) => ShowPicture();
    rbAfter.CheckedChanged += (s, e) => ShowPicture();
    pic.Click += (s, e) => { if (snapArmed) return; var p = CurrentPicturePath(); if (p != null && System.IO.File.Exists(p)) Start(p, null); };
    // any way the model changes (click, keyboard, autocomplete) re-applies the filter and jumps to that model's first item
    cmbModel.SelectedIndexChanged += (s, e) => {
      if (syncing || cmbModel.SelectedIndex < 0) return;
      if (modelPat != "") {
        string k = searchKeys[cmbModel.SelectedIndex];
        if (k == "?") return;
        foreach (var t in tagKeys.ToList()) tagOn[t] = k == "*" ? t != "U" : t == k;
        RefreshTags();
      } else fullIndex = cmbModel.SelectedIndex;
      if (CurrentKey() != appliedKey) ApplyFilter(true);
    };
    txtMS.TextChanged += (s, e) => { msTimer.Stop(); msTimer.Start(); };
    msTimer.Tick += (s, e) => { msTimer.Stop(); ApplyModelSearch(); };
    txtMS.KeyDown += (s, e) => { if (e.KeyCode == WF.Keys.Enter) { msTimer.Stop(); ApplyModelSearch(); e.Handled = true; e.SuppressKeyPress = true; } };
    cmb.SelectionChangeCommitted += (s, e) => { if (!syncing && cmb.SelectedIndex >= 0) Go("Goto", cmb.SelectedIndex + 1); };
    chk.CheckedChanged += (s, e) => { if (!syncing) Go("Refresh", 0); };
    num.ValueChanged += (s, e) => { if (!syncing) Go("Refresh", 0); };
    // sub-filter: name text or Done status
    cmbSub.SelectedIndexChanged += (s, e) => { txtSub.Visible = cmbSub.SelectedIndex == 1; cmbSubSt.Visible = cmbSub.SelectedIndex == 2; cmbStamp.Visible = cmbSub.SelectedIndex == 3; if (!syncing) SubFilterChanged(); };
    cmbStamp.SelectedIndexChanged += (s, e) => { if (!syncing) SubFilterChanged(); };
    cmbStamp.TextUpdate += (s, e) => { subTimer.Stop(); subTimer.Start(); };
    cmbSubSt.SelectedIndexChanged += (s, e) => { if (!syncing) SubFilterChanged(); };
    txtSub.TextChanged += (s, e) => { subTimer.Stop(); subTimer.Start(); };
    subTimer.Tick += (s, e) => { subTimer.Stop(); SubFilterChanged(); };
    // Done + comment, saved to navigator_status.json and the web navigator
    cmbSt.SelectedIndexChanged += (s, e) => {
      if (syncing || curN < 1 || cmbSt.SelectedIndex < 0) return;
      string st = StKeys[cmbSt.SelectedIndex];
      NavData.SetState(H.Cams[curN - 1], st, null);
      ShowDoneInfo(H.Cams[curN - 1]);
      if (st == "closed" && isClash) { if (!snapArmed) SnapClick(); }   // #100: clash: prepare the view, then Capture view
      else if (st == "closed" && WF.MessageBox.Show(this, "Marked as Closed.\n\nSave the solved snapshot now (same camera as the VP view)?", "BIMRadar Clash Viewer", WF.MessageBoxButtons.YesNo, WF.MessageBoxIcon.Question) == WF.DialogResult.Yes)
        Go("Snapshot", 0);
    };
    // comment: type, then Post (or Enter); Edit re-opens a posted comment. Text not posted is still kept when moving on.
    txtComment.TextChanged += (s, e) => { if (syncing || curN < 1 || !editingComment) return; pendingN = curN; };
    txtComment.KeyDown += (s, e) => { if (e.KeyCode == WF.Keys.Enter && !e.Shift && editingComment) { e.Handled = true; e.SuppressKeyPress = true; PostComment(); } };
    btnPost.Click += (s, e) => {
      if (editingComment) PostComment();
      else { SetCommentMode(true); txtComment.Focus(); txtComment.SelectionStart = txtComment.TextLength; }
    };
    tip.SetToolTip(btnPost, "Post the comment (saved to the web navigator and its report). After posting, Edit opens it again.");
    Activated += (s, e) => { FlushComment(); if (NavData.ReloadStates() && curN > 0) Sync(curN); };
    FormClosed += (s, e) => {
      FlushComment(); Instance = null; if (pic.Image != null) pic.Image.Dispose();
      if (H.ChangingSource) {   // #92: closed from inside its own ExternalEvent: dispose the event once Revit is idle again
        var ev0 = Ev; EventHandler idle = null;
        idle = (s2, e2) => { WF.Application.Idle -= idle; try { ev0.Dispose(); } catch (Exception) { } };
        WF.Application.Idle += idle;
      } else { try { Ev.Dispose(); } catch (Exception) { } }
    };
  }

  // "select by ID" button picture: small ID card with a pointer
  static SD.Bitmap IdIcon() {
    var b = new SD.Bitmap(22, 20);
    using (var g = SD.Graphics.FromImage(b)) {
      g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
      g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
      var green = SD.Color.FromArgb(46, 140, 70);
      using (var br = new SD.SolidBrush(SD.Color.FromArgb(225, 242, 228))) g.FillRectangle(br, 1, 2, 15, 12);
      using (var p = new SD.Pen(green)) g.DrawRectangle(p, 1, 2, 15, 12);
      using (var f = new SD.Font("Segoe UI", 6.5f, SD.FontStyle.Bold)) using (var tb = new SD.SolidBrush(green)) g.DrawString("ID", f, tb, 1.5f, 2.5f);
      var pts = new[] { new SD.PointF(12, 8), new SD.PointF(12, 18), new SD.PointF(14.5f, 15.5f), new SD.PointF(16.5f, 19.2f), new SD.PointF(18.2f, 18.4f), new SD.PointF(16.2f, 14.8f), new SD.PointF(19.6f, 14.6f) };
      g.FillPolygon(SD.Brushes.White, pts);
      g.DrawPolygon(SD.Pens.Black, pts);
    }
    return b;
  }

  // status colours, same as the web navigator and the Excel export: Open orange, Closed green, Need clarification red, Action by others light blue
  static readonly SD.Color[] StCol = { SD.Color.FromArgb(245, 158, 11), SD.Color.FromArgb(46, 158, 91), SD.Color.FromArgb(214, 69, 69), SD.Color.FromArgb(56, 163, 224) };

  void DrawStatusItem(object sender, WF.DrawItemEventArgs e) {
    var cb = (WF.ComboBox)sender;
    e.DrawBackground();
    if (e.Index < 0 || e.Index >= cb.Items.Count) return;
    var g = e.Graphics;
    g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
    int d = Math.Max(6, Math.Min(10, e.Bounds.Height - 6));
    var r = new SD.Rectangle(e.Bounds.Left + 4, e.Bounds.Top + (e.Bounds.Height - d) / 2, d, d);
    using (var br = new SD.SolidBrush(StCol[Math.Min(e.Index, StCol.Length - 1)])) g.FillEllipse(br, r);
    var fc = (e.State & WF.DrawItemState.Selected) != 0 ? SD.SystemColors.HighlightText : cb.ForeColor;
    WF.TextRenderer.DrawText(g, cb.Items[e.Index].ToString(), e.Font, new SD.Rectangle(r.Right + 6, e.Bounds.Top, e.Bounds.Width - d - 10, e.Bounds.Height), fc, WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.Left | WF.TextFormatFlags.EndEllipsis);
    e.DrawFocusRectangle();
  }

  void SetCommentMode(bool editing) {
    editingComment = editing;
    txtComment.ReadOnly = !editing;
    txtComment.BackColor = editing ? SD.SystemColors.Window : SD.Color.FromArgb(242, 242, 242);
    btnPost.Text = editing ? "Post" : "Edit";
  }

  void PostComment() {
    if (curN < 1) return;
    pendingN = curN;
    FlushComment();
    SetCommentMode(txtComment.Text.Trim() == "");
    ShowMessage(txtComment.Text.Trim() == "" ? "Comment cleared." : "Comment posted.");
  }

  void FlushComment() {
    if (pendingN < 1 || pendingN > H.Cams.Count) return;
    var c = H.Cams[pendingN - 1];
    pendingN = 0;
    var st = NavData.GetState(c);
    if ((st != null ? st.c : "") == txtComment.Text) return;
    NavData.SetState(c, null, txtComment.Text);
    ShowDoneInfo(c);
  }

  void ShowDoneInfo(VpCam c) {
    var st = NavData.GetState(c);
    lblDone.Text = st == null || string.IsNullOrEmpty(st.by) ? "" : "Action by " + st.by + ",  " + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(st.t).ToLocalTime().ToString("dd MMM yyyy HH:mm");
  }

  bool SubOk(VpCam c) {
    int m = cmbSub.SelectedIndex;
    if (m == 1) { string q = txtSub.Text.Trim(); return q == "" || (c.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0; }
    if (m == 3) {
      var want = cmbStamp.Text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => System.Text.RegularExpressions.Regex.Replace(x, @"\(\d+\)\s*$", "").Trim()).Where(x => x != "").ToList();
      return want.Count == 0 || want.Any(w => string.Equals(w, c.Stamp, StringComparison.OrdinalIgnoreCase));
    }
    if (m == 2 && srcStatus) return cmbSubSt.SelectedItem != null && string.Equals(c.Status ?? "", cmbSubSt.SelectedItem.ToString(), StringComparison.OrdinalIgnoreCase);
    if (m == 2) { string st = NavData.StatusOf(c); int k = cmbSubSt.SelectedIndex; return st == StKeys[Math.Max(0, k)]; }
    return true;
  }

  void SubFilterChanged() {
    ApplyFilter(false);
    if (H.Order.Count == 0) { lblCount.Text = "No " + sourceWord + " match the filter."; return; }
    if (!H.Order.Contains(curN)) { H.Cur = 0; Go("Goto", 1); } else Sync(curN);
  }

  void ShowHits() {
    if (!isClash || curN < 1) return;
    var c = H.Cams[curN - 1];
    bool cur = H.Cur == curN;
    for (int i = 0; i < 2; i++) {
      if (i >= c.Items.Count) { lblItem[i].Text = i == 0 ? "No Element ID in the report for this clash" : ""; lblItem[i].ForeColor = SD.Color.Gray; btnPick[i].Enabled = false; continue; }
      var it = c.Items[i];
      var h = cur && i < H.Hits.Count && H.Hits[i].Id == it.Value ? H.Hits[i] : null;
      // #81: Found = current text + category; not found = red "<model> #id: NOT FOUND (<Where>)"
      if (h != null && !h.Found) lblItem[i].Text = (i + 1) + ". " + it.Key + " #" + it.Value + ": NOT FOUND (" + h.Where + ")";
      else lblItem[i].Text = (i + 1) + ". " + it.Key + "  #" + it.Value + (h != null ? "  -  " + h.Where + (h.CatName != "" ? " \u00B7 " + h.CatName : "") : "");
      lblItem[i].ForeColor = h == null ? SD.Color.DimGray : h.Found ? SD.Color.SeaGreen : SD.Color.Firebrick;
      btnPick[i].Enabled = h != null && h.Found;
    }
  }

  int FindModelItem(string text) {
    text = (text ?? "").Trim();
    if (text == "") return 0;
    for (int i = 0; i < cmbModel.Items.Count; i++) if (cmbModel.Items[i].ToString().StartsWith(text, StringComparison.OrdinalIgnoreCase)) return i;
    for (int i = 0; i < cmbModel.Items.Count; i++) if (cmbModel.Items[i].ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return i;
    return -1;
  }

  string testsForModel = null;   // model key the Test list was last built for

  bool InModel(VpCam c0, string key) {
    var ms = c0.Models.Count > 0 ? c0.Models : new List<string> { c0.Model ?? "" };
    if (key.StartsWith("\u0003")) { string g; return hitGroup.TryGetValue(c0.Index, out g) && tagOn.ContainsKey(g) && tagOn[g]; }
    return key == "" || (key == "\u0001none" && ms.All(x => x == "")) || ms.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
  }

  // Test list = only the tests that have clashes of the chosen model, counted for that model
  void RebuildTests(string key) {
    testsForModel = key;
    string keep = cmbTest.SelectedIndex >= 0 && cmbTest.SelectedIndex < testKeys.Count ? testKeys[cmbTest.SelectedIndex] : null;
    var inM = H.Cams.Where(c => InModel(c, key)).ToList();
    syncing = true;
    try {
      cmbTest.BeginUpdate();
      cmbTest.Items.Clear(); testKeys.Clear();
      int nDef = inM.Count(c => c.DefaultTest);
      if (nDef > 0) { cmbTest.Items.Add("New / Active (" + nDef + ")"); testKeys.Add("\u0002open"); }
      cmbTest.Items.Add("All tests (" + inM.Count + ")"); testKeys.Add("");
      foreach (var g in inM.GroupBy(c => c.Test)) { cmbTest.Items.Add(g.Key + "   (" + g.Count() + ")"); testKeys.Add(g.Key); }
      int k = keep == null ? -1 : testKeys.IndexOf(keep);
      cmbTest.SelectedIndex = k >= 0 ? k : 0;
      cmbTest.EndUpdate();
    } finally { syncing = false; }
  }

  string CurrentKey() { if (modelPat != "") return "\u0003" + modelPat + "|" + string.Join(",", tagKeys.Where(t => tagOn[t])); return (cmbModel.SelectedIndex >= 0 && cmbModel.SelectedIndex < modelKeys.Count) ? modelKeys[cmbModel.SelectedIndex] : ""; }

  // returns true if the model filter changed (and jumped to its first item)
  bool EnsureFilter() {
    if (CurrentKey() != appliedKey) { ApplyFilter(true); return true; }
    return false;
  }

  int PatternModels(string pat) {
    return H.Cams.SelectMany(c => c.Models.Count > 0 ? c.Models : new List<string> { c.Model ?? "" }).Distinct(StringComparer.OrdinalIgnoreCase).Count(m => NavHandler.PatternMatch(m, pat));
  }

  void ApplyModelSearch() {
    string q = txtMS.Text.Trim();
    if (q == modelPat) { txtMS.BackColor = SD.SystemColors.Window; return; }
    if (q != "" && !BuildSearch(q)) { txtMS.BackColor = SD.Color.FromArgb(255, 228, 228); ShowError("Nothing matches \"" + q + "\" (anchor model, zone or room)."); return; }
    txtMS.BackColor = SD.SystemColors.Window;
    modelPat = q;
    if (q == "") {
      hitGroup.Clear(); tagKeys.Clear(); tagOn.Clear(); searchKeys.Clear();
      syncing = true;
      try { cmbModel.BeginUpdate(); cmbModel.Items.Clear(); foreach (var it in fullItems) cmbModel.Items.Add(it); modelKeys = fullKeys.ToList(); cmbModel.SelectedIndex = Math.Min(fullIndex, cmbModel.Items.Count - 1); cmbModel.EndUpdate(); } finally { syncing = false; }
      ShowTagsHint();
    }
    SavePref();
    if (curN > 0 && curN <= H.Cams.Count) ShowWhy(H.Cams[curN - 1]);   // #70: search changed
    if (CurrentKey() != appliedKey) {
      ApplyFilter(true);
      if (q != "") ShowMessage("Code \"" + q + "\": " + H.Order.Count + " " + sourceWord + " shown." + (tagOn.ContainsKey("U") && !tagOn["U"] ? " Not located: " + hitGroup.Count(kv => kv.Value == "U") + " (click the orange tag to add them)." : ""));
    }
  }

  static string ShortModel(string m) {
    var f = System.IO.Path.GetFileNameWithoutExtension(m ?? "").Split('-');
    return f.Length >= 8 ? f[2] + "-" + f[3] + "-" + f[6] + (f[7].TrimStart('0') == "1" ? "" : "-" + f[7]) : m;
  }

  string TagText(string k, int n) {
    if (k == "X") return "Other anchor  (" + n + ")";
    if (k == "Z") return "Zone  (" + n + ")";
    if (k == "R") return "Room  (" + n + ")";
    if (k == "U") return "Not located  (" + n + ")";
    return ShortModel(k.Substring(2)) + "  (" + n + ")";
  }

  // groups the search finds; false when nothing (apart from 'not located') matches
  bool BuildSearch(string q) {
    var map = new Dictionary<int, string>();
    foreach (var c in H.Cams) { var g = NavHandler.SearchGroup(c, q); if (g != null) map[c.Index] = g; }
    if (!map.Values.Any(g => g != "U")) return false;
    hitGroup = map;
    var keys = map.Values.Distinct().Where(g => g.StartsWith("M:")).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
    foreach (var g in new[] { "X", "Z", "R", "U" }) if (map.ContainsValue(g)) keys.Add(g);
    var old = tagOn;
    tagKeys = keys;
    tagOn = keys.ToDictionary(k => k, k => old.ContainsKey(k) ? old[k] : k != "U");
    tagPanel.SuspendLayout();
    tagPanel.Controls.Clear();
    var tip = new WF.ToolTip();
    foreach (var k in keys) {
      int n = map.Count(kv => kv.Value == k);
      var cb = new WF.CheckBox { Appearance = WF.Appearance.Button, AutoSize = true, Text = TagText(k, n), Tag = k, FlatStyle = WF.FlatStyle.Flat, Font = new SD.Font("Segoe UI", 8f), Margin = new WF.Padding(2, 2, 2, 1), Padding = new WF.Padding(0), MinimumSize = new SD.Size(0, 24), Cursor = WF.Cursors.Hand };
      string kk = k;
      string tipText = k.StartsWith("M:") ? k.Substring(2) : string.Join("\n", H.Cams.Where(c => map.ContainsKey(c.Index) && map[c.Index] == kk).Select(c => kk == "Z" ? c.Zone : kk == "R" ? c.Room : kk == "X" ? ShortModel(c.Model) + ": " + (c.Zone + " " + c.Room).Trim() : "No anchor model / zone code / room: " + c.Name).Distinct().Take(25));
      tip.SetToolTip(cb, tipText + "\nClick to hide / show.");
      cb.CheckedChanged += (s, e) => { if (syncing) return; var b = (WF.CheckBox)s; tagOn[(string)b.Tag] = b.Checked; RefreshTags(); if (CurrentKey() != appliedKey) ApplyFilter(true); };
      tagPanel.Controls.Add(cb);
    }
    tagPanel.ResumeLayout();
    syncing = true;
    try {
      cmbModel.BeginUpdate(); cmbModel.Items.Clear(); searchKeys.Clear();
      cmbModel.Items.Add("All search results  (" + map.Count(kv => kv.Value != "U") + ")"); searchKeys.Add("*");
      foreach (var k in keys) { int n = map.Count(kv => kv.Value == k); cmbModel.Items.Add(k.StartsWith("M:") ? k.Substring(2) + "   (" + n + ")" : k == "X" ? "Anchored to another model - zone / room match  (" + n + ")" : k == "Z" ? "No anchor - zone matches  (" + n + ")" : k == "R" ? "No anchor - room matches  (" + n + ")" : "Not located  (" + n + ")"); searchKeys.Add(k); }
      cmbModel.Items.Add("Tags selected below"); searchKeys.Add("?");
      cmbModel.EndUpdate();
    } finally { syncing = false; }
    RefreshTags();
    return true;
  }

  // tag colours + the model list entry that matches the tag selection
  void RefreshTags() {
    syncing = true;
    try {
      foreach (WF.CheckBox cb in tagPanel.Controls.OfType<WF.CheckBox>()) {
        string k = (string)cb.Tag; bool on = tagOn.ContainsKey(k) && tagOn[k];
        cb.Checked = on;
        var col = k == "U" ? SD.Color.FromArgb(214, 120, 30) : k == "Z" || k == "R" || k == "X" ? SD.Color.FromArgb(120, 90, 180) : SD.Color.FromArgb(0, 120, 212);
        cb.BackColor = on ? col : SD.Color.White; cb.ForeColor = on ? SD.Color.White : col; cb.FlatAppearance.BorderColor = col; cb.FlatAppearance.CheckedBackColor = col;
      }
      if (searchKeys.Count > 0) {
        var onKeys = tagKeys.Where(t => tagOn[t]).ToList();
        int idx = onKeys.Count == 1 ? searchKeys.IndexOf(onKeys[0]) : (onKeys.Count == tagKeys.Count(t => t != "U") && !onKeys.Contains("U")) ? 0 : searchKeys.IndexOf("?");
        if (idx >= 0 && cmbModel.SelectedIndex != idx) cmbModel.SelectedIndex = idx;
      }
    } finally { syncing = false; }
  }

  void ShowTagsHint() {
    tagPanel.Controls.Clear();
    tagPanel.Controls.Add(new WF.Label { AutoSize = true, ForeColor = SD.Color.Gray, Font = new SD.Font("Segoe UI", 8f), Margin = new WF.Padding(3, 6, 3, 3), Text = anyModel ? "Type a building / volume code above (e.g. D0, 33, 03306):\nmatching models, zones and rooms show here as tags." : "No model names in this source." });
  }

  string PrefFile() { return System.IO.Path.Combine(NavData.AppDir, "model_search_" + (isClash ? "clash" : H.Source.StartsWith("Revizto") ? "revizto" : "viewpoints") + ".txt"); }
  string ReadPref() { try { var p = PrefFile(); return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p).Trim() : ""; } catch (Exception) { return ""; } }
  void SavePref() { try { System.IO.Directory.CreateDirectory(NavData.AppDir); System.IO.File.WriteAllText(PrefFile(), modelPat); } catch (Exception) { } }

  // rebuild the Order (filtered list) and the item drop-down; optionally jump to the first item of the new filter
  void ApplyFilter(bool go) {
    string key = CurrentKey();
    appliedKey = key;
    H.ModelPattern = modelPat != "" ? modelPat : null;
    H.SearchHit = modelPat != "" ? new HashSet<int>(hitGroup.Where(kv => tagOn.ContainsKey(kv.Value) && tagOn[kv.Value]).Select(kv => kv.Key)) : new HashSet<int>();
    H.Order = new List<int>();
    if (isClash && testsForModel != key) RebuildTests(key);
    string tkey = (isClash && cmbTest.SelectedIndex >= 0) ? testKeys[cmbTest.SelectedIndex] : "";
    for (int i = 0; i < H.Cams.Count; i++) {
      var c0 = H.Cams[i];
      bool okModel = InModel(c0, key);
      bool okTest = tkey == "" || (tkey == "\u0002open" ? c0.DefaultTest : c0.Test == tkey);
      bool okEmpty = !(chkHide != null && chkHide.Checked && H.EmptyIdx.Contains(c0.Index) && i + 1 != (keepN > 0 ? keepN : curN));   // #72
      if (okModel && okTest && SubOk(c0) && okEmpty) H.Order.Add(i + 1);
    }
    syncing = true;
    try {
      cmb.BeginUpdate();
      cmb.Items.Clear();
      for (int p = 0; p < H.Order.Count; p++) { var c = H.Cams[H.Order[p] - 1]; cmb.Items.Add((p + 1).ToString("00") + "   " + (c.IsClash ? c.Test + " · " : "") + c.Name); }
      cmb.EndUpdate();
    } finally { syncing = false; }
    if (go && H.Order.Count > 0) { H.Cur = 0; Go("Goto", 1); }
  }

  // No ribbon button uses GoNext / GoPrevious since the 2026-11-24 owner decision (Next / Previous live in the window); kept for possible later use.
  public void GoNext() { Go("Next", 0); }
  public void GoPrevious() { Go("Prev", 0); }

  // #100: clash snapshot in two clicks: 1) hint + button becomes "Capture view" (owner sets the camera in any view), 2) copy the active view.
  bool snapArmed;
  void SnapClick() {
    if (!isClash) { Go("Snapshot", 0); return; }
    if (!snapArmed) {
      snapArmed = true;
      btnSnap.Text = "Capture view"; btnSnap.BackColor = SD.Color.FromArgb(255, 230, 140);
      lblInfo.ForeColor = SD.Color.FromArgb(90, 60, 0);
      lblInfo.Text = "Solved snapshot: follow the 2 steps in the picture area. Esc cancels.";
      ShowPicture();   // #101: shows the step card instead of a picture while armed
      return;
    }
    DisarmSnap();
    Go("Snapshot", 0);
  }
  void DisarmSnap() {
    if (!snapArmed) return;
    snapArmed = false;
    btnSnap.Text = "Save snapshot"; btnSnap.BackColor = SD.SystemColors.Control; btnSnap.UseVisualStyleBackColor = true;
    ShowPicture();
  }

  // #101: step card in the picture area, so a first-time user sees what Capture view does.
  void ShowSnapCard() {
    int w = Math.Max(pic.ClientSize.Width, 100), h = Math.Max(pic.ClientSize.Height, 100);
    var bmp = new SD.Bitmap(w, h);
    using (var g = SD.Graphics.FromImage(bmp))
    using (var title = new SD.Font("Segoe UI Semibold", 11f))
    using (var body = new SD.Font("Segoe UI", 9.5f))
    using (var ink = new SD.SolidBrush(SD.Color.FromArgb(60, 40, 0))) {
      g.Clear(SD.Color.FromArgb(255, 243, 191));
      g.TextRenderingHint = SD.Text.TextRenderingHint.ClearTypeGridFit;
      float x = 12, y = 10, tw = w - 24;
      g.DrawString("Solved snapshot in 2 steps", title, ink, new SD.RectangleF(x, y, tw, 24)); y += 30;
      string[] steps = {
        "1.  In Revit, set up the 3D view the way the solved clash should be shown: orbit, zoom, pan. Any 3D view works, e.g. the WIP copy.",
        "2.  Click the yellow \"Capture view\" button. Everything you see in that Revit view is saved as the solved picture.",
        "To cancel: press Esc or go to another clash." };
      foreach (var t in steps) {
        var sz = g.MeasureString(t, body, (int)tw);
        g.DrawString(t, body, ink, new SD.RectangleF(x, y, tw, sz.Height + 2));
        y += sz.Height + 8;
      }
    }
    pic.Image = bmp;
    lblPic.Text = "Waiting for Capture view...";
  }

  void Go(string action, int idx) {
    if (action != "Snapshot" && snapArmed) { DisarmSnap(); lblInfo.Text = ""; }
    if (action != "Goto" && action != "Snapshot" && action != "CheckAll" && action != "ToggleColors" && EnsureFilter()) return; // model changed: already jumped to its first item
    H.Action = action; H.GoTo = idx;
    H.UseBox = chk.Checked; H.BoxM = (double)num.Value;
    Ev.Raise();
  }

  protected override bool ProcessCmdKey(ref WF.Message msg, WF.Keys keyData) {
    if ((ActiveControl is WF.TextBoxBase) && (keyData == WF.Keys.Right || keyData == WF.Keys.Left || keyData == WF.Keys.PageDown || keyData == WF.Keys.PageUp)) return base.ProcessCmdKey(ref msg, keyData);
    if (keyData == WF.Keys.Right || keyData == WF.Keys.PageDown) { Go("Next", 0); return true; }
    if (keyData == WF.Keys.Left || keyData == WF.Keys.PageUp) { Go("Prev", 0); return true; }
    if (keyData == (WF.Keys.Control | WF.Keys.S)) { SnapClick(); return true; }
    if (keyData == WF.Keys.Escape && snapArmed) { DisarmSnap(); lblInfo.Text = ""; return true; }
    return base.ProcessCmdKey(ref msg, keyData);
  }

  string CurrentPicturePath() {
    if (curN < 1 || curN > H.Cams.Count) return null;
    var c = H.Cams[curN - 1];
    return rbAfter.Checked ? NavData.AfterPath(H.PicDir, c) : NavData.BeforePath(H.PicDir, c);
  }

  public void ShowPicture() {
    var old = pic.Image; pic.Image = null; if (old != null) old.Dispose();
    if (snapArmed) { ShowSnapCard(); return; }   // #101
    var p = CurrentPicturePath();
    if (p == null) return;
    var img = NavData.LoadImageNoLock(p);
    pic.Image = img;
    lblPic.Text = img != null ? System.IO.Path.GetFileName(p) + "   (" + img.Width + " x " + img.Height + ")" : (rbAfter.Checked ? "No solved snapshot yet: " : "Picture not found (yet): ") + System.IO.Path.GetFileName(p);
  }

  // called from the download thread when a picture has arrived
  public void PictureArrived(int index, int done, int total) {
    if (IsDisposed) return;
    Text = "BIMRadar Clash Viewer - " + H.Source + (done < total ? "  (downloading pictures " + done + "/" + total + ")" : "");
    if (index == curN && !rbAfter.Checked) ShowPicture();
  }

  // #72 / #73: after the handler stored a location result: re-apply Hide empty (the item just opened stays), then redraw
  public void RefreshLoc(int n) {
    if (n < 1) n = curN;
    if (n < 1 || n > H.Cams.Count) return;
    if (chkHide.Checked) { keepN = n; try { ApplyFilter(false); } finally { keepN = 0; } }
    Sync(n);
  }

  public void Sync(int n) {
    if (pendingN > 0 && pendingN != n) FlushComment();
    syncing = true;
    try {
      if (n != curN) rbBefore.Checked = true;   // new item: show the source picture first
      curN = n;
      var c = H.Cams[n - 1];
      lblName.Text = c.Name;
      lblInfo.ForeColor = SD.Color.DimGray;
      bool hasAfter = false;
      try { hasAfter = System.IO.File.Exists(NavData.AfterPath(H.PicDir, c)); } catch (Exception) { }
      int pos = H.Order.IndexOf(n);
      lblCount.Text = (pos >= 0 ? (pos + 1) + " of " + H.Order.Count : "?") + "  |  " + c.Img + (hasAfter ? "  |  solved snapshot saved" : "");
      if (pos >= 0 && cmb.SelectedIndex != pos) cmb.SelectedIndex = pos;
      if (c.HasPlane) {
        double dist = c.PlaneN.DotProduct(c.Target - c.PlanePt) * FT;
        string kind = Math.Abs(c.PlaneN.Z) > 0.9 ? "horizontal (top) cut" : "vertical cut";
        lblInfo.Text = "Navisworks section plane: " + kind + "; focus point is " + dist.ToString("0.00") + " m " + (dist >= 0 ? "inside" : "outside") + " the kept side.";
      } else if (!string.IsNullOrEmpty(c.Note)) lblInfo.Text = c.Note;
      else lblInfo.Text = "No section plane was saved for this viewpoint.";
      if (c.IsClash && c.HasNavCam) lblInfo.Text = "Camera: from Navisworks viewpoint. " + lblInfo.Text;   // #23 (silent for the normal HTML-only workflow)
      var st = NavData.GetState(c);
      cmbSt.SelectedIndex = Math.Max(0, Array.IndexOf(StKeys, NavData.StatusOf(c)));
      if (pendingN != n) { txtComment.Text = st != null ? st.c : ""; SetCommentMode(txtComment.Text.Trim() == ""); }
      ShowDoneInfo(c);
      ShowHits();
      ShowWhy(c);   // #70 / #71
      SyncColors();   // #80
      if (isClash) {   // #82: Unisolate when isolated, Isolate when not
        btnIso.Text = H.Isolated ? "Unisolate" : "Isolate";
        btnIso.BackColor = H.Isolated ? SD.Color.FromArgb(255, 236, 179) : SD.Color.Empty; btnIso.UseVisualStyleBackColor = !H.Isolated;
        btnIso.Enabled = H.Cur > 0;   // a clash has been opened at least once
      }   // #93: no else: the button is hidden and never touched for Revizto / viewpoints
      ShowPicture();
    } finally { syncing = false; }
  }

  // #80: button text follows the handler state
  public NavHandler Handler { get { return H; } }   // #96: Clear Overrides button
  public void SyncColors() { btnColor.Text = H.ColorsOn ? "Clear override" : "Create override"; }

  // #70 / #71: line 1 = anchor model / zone (amber "Not anchored to a model" for a Revizto issue without anchor model),
  // line 2 = why the item is in the list, from the search group (same keys as SearchGroup / BuildSearch)
  void ShowWhy(VpCam c) {
    string where = (c.Zone != "" ? " · zone " + c.Zone : "") + (c.Room != "" ? " · room " + c.Room : "");
    var ms = (c.Models.Count > 0 ? c.Models : new List<string> { c.Model ?? "" }).Where(m => m != "").ToList();
    if (isRevizto && (c.Model ?? "") == "") {   // #71
      lblWhy1.ForeColor = SD.Color.FromArgb(214, 120, 30);
      lblWhy1.Text = "Not anchored to a model" + where;
    } else {
      lblWhy1.ForeColor = SD.Color.Gray;
      lblWhy1.Text = "Model: " + (ms.Count > 0 ? string.Join(", ", ms) : "none") + where;
    }
    string g;
    if (modelPat != "" && hitGroup.TryGetValue(c.Index, out g)) {   // #70
      lblWhy2.Text = g.StartsWith("M:") ? "Matched: anchor model " + g.Substring(2)
        : g == "X" ? "Matched: zone/room (anchored to another model " + (ms.Count > 0 ? ms[0] : "") + ")"
        : g == "Z" ? "Matched: zone " + c.Zone
        : g == "R" ? "Matched: room " + c.Room
        : "Not located";
    } else lblWhy2.Text = "";
    // #72 / #73: third line = what is at the location; yellow hint; Go to element when an element is far from the point
    NavHandler.LocResult lr = null;
    if (isRevizto) H.Loc.TryGetValue(c.Index, out lr);
    bool other = false;
    if (isRevizto) H.OtherIdx.TryGetValue(c.Index, out other);
    if (other) {   // #79: takes precedence over the grey summary; merges the empty-view warning
      lblWhy3.ForeColor = SD.Color.Firebrick;
      lblWhy3.Text = "Anchored to another model: " + ((c.Model ?? "") != "" ? c.Model : string.Join(", ", ms)) + " \u2014 the view may be empty or wrong here.";
    }
    else if (lr == null) lblWhy3.Text = "";
    else if (lr.Empty) { lblWhy3.ForeColor = SD.Color.Firebrick; lblWhy3.Text = "Nothing from this model at this location"; }
    else { lblWhy3.ForeColor = SD.Color.Gray; lblWhy3.Text = lr.Summary; }
    string hint = lr == null ? "" : lr.Hint;
    lblHint.Text = hint; lblHint.Visible = hint != "";
    bool gt = lr != null && lr.OffsetM != null && lr.OffsetM.Value > H.OffsetLimit;
    btnGoto.Visible = gt;
    if (!isClash) btnDup.Width = gt ? 232 : 356;   // #80 / #85: the clash layout is fixed (half width)
  }

  public void ShowSelection(int found, int total, bool clash, string linkNote) {
    if (clash && total > 0 && found == 0) {   // #81: never silently show the box
      lblInfo.ForeColor = SD.Color.Firebrick;
      lblInfo.Text = "Clash elements not found in this model or its links: the view shows everything in the box.";
      return;
    }
    if (!string.IsNullOrEmpty(linkNote)) lblInfo.Text = linkNote + ". " + lblInfo.Text;   // #81
    lblInfo.Text = (found > 0 ? "Found " + found + " of " + total + " element(s) by Element ID. " : "Element(s) not found in this model or its links. ") + lblInfo.Text;
  }

  public void ShowTime(long ms) { lblCount.Text += "  |  " + (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s"; }

  public void ShowMessage(string m) { lblInfo.ForeColor = SD.Color.DimGray; lblInfo.Text = m; }

  public void ShowError(string m) { lblInfo.ForeColor = SD.Color.Firebrick; lblInfo.Text = "Error: " + m; }

  public void ShowOtherModel(string model, string openTitle) {
    lblInfo.ForeColor = SD.Color.Firebrick;
    lblInfo.Text = "Preview only: this " + (isClash ? "clash" : "issue") + " is from another model (" + model + "). Open that model, or type part of its name (e.g. the building code) in Search to open it here.";
  }

  public void ShowSaved(string path) {
    lastSaved = path;
    lblInfo.Text = "Saved " + System.IO.Path.GetFileName(path) + " to " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) + ".";
    rbAfter.Checked = true; ShowPicture();
  }

  void Start(string file, string args) {
    try {
      if (args == null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
      else System.Diagnostics.Process.Start(file, args);
    } catch (Exception ex) { ShowError(ex.Message); }
  }

  void OpenWeb() {
    if (!string.IsNullOrEmpty(H.HtmlPath) && System.IO.File.Exists(H.HtmlPath)) Start(H.HtmlPath, null);
    else ShowError("The web navigator was not created" + (string.IsNullOrEmpty(H.HtmlError) ? "." : ": " + H.HtmlError));
  }

  void OpenFolder() {
    if (!string.IsNullOrEmpty(lastSaved) && System.IO.File.Exists(lastSaved)) Start("explorer.exe", "/select,\"" + lastSaved + "\"");
    else if (!string.IsNullOrEmpty(H.PicDir) && System.IO.Directory.Exists(H.PicDir)) Start("explorer.exe", "\"" + H.PicDir + "\"");
    else ShowError("Picture folder not found: " + H.PicDir);
  }
}
}
