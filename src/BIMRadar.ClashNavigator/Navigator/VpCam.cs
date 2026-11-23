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
public class VpCam {
  public string Name, Guid, Img;          // Img = Navisworks picture file name (vp0001.jpg or from the report)
  public int Index;                        // position of the <view> in the XML (1-based)
  public XYZ Eye, Fwd, Up, Target;
  public double FocalM;
  public double FovH = 0, Aspect = 1;      // Navisworks vertical field of view (radians) and width/height
  public double[] RawPos, RawQ;            // as in the XML (shared metres / quaternion) for the web navigator
  public bool HasPlane;
  public XYZ PlanePt, PlaneN;
  // used by other sources (Revizto)
  public string Id, Model = "", Url, Note = "", St0;
  public string Zone = "", Room = "", Stamp = "";   // Revizto: zone / room (building code when there is no anchor model), stamp abbreviation
  public double[] RawDir;                  // look direction in shared coordinates (instead of RawQ)
  public bool HasPoint;                    // issue point -> section box around it
  public XYZ Point;
  public List<int> Select = new List<int>(); // Revit element ids to select
  // Navisworks clash test
  public bool IsClash, DefaultTest;
  public string Test = "", Status = "";
  public List<string> Models = new List<string>();                                 // models involved (Revizto: none, Model used)
  public List<KeyValuePair<string, int>> Items = new List<KeyValuePair<string, int>>(); // (model file, Element ID)
}
}
