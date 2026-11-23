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
public class Win32Owner : WF.IWin32Window {
  private readonly IntPtr _h;
  public Win32Owner(IntPtr h) { _h = h; }
  public IntPtr Handle { get { return _h; } }
}
}
