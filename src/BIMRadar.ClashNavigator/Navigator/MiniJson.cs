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
// small JSON reader (objects, arrays, strings, numbers, true/false/null) - works in every Revit version
public static class MiniJson {
  public static object Parse(string s) { int i = 0; return Val(s, ref i); }
  static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
  static object Val(string s, ref int i) {
    Ws(s, ref i);
    if (i >= s.Length) return null;
    char ch = s[i];
    if (ch == '{') {
      var d = new Dictionary<string, object>(); i++;
      while (true) { Ws(s, ref i); if (i < s.Length && s[i] == '}') { i++; return d; }
        string k = Str(s, ref i); Ws(s, ref i); i++; d[k] = Val(s, ref i); Ws(s, ref i);
        if (i < s.Length && s[i] == ',') i++; else { Ws(s, ref i); if (i < s.Length && s[i] == '}') i++; return d; } }
    }
    if (ch == '[') {
      var l = new List<object>(); i++;
      while (true) { Ws(s, ref i); if (i < s.Length && s[i] == ']') { i++; return l; }
        l.Add(Val(s, ref i)); Ws(s, ref i);
        if (i < s.Length && s[i] == ',') i++; else { Ws(s, ref i); if (i < s.Length && s[i] == ']') i++; return l; } }
    }
    if (ch == '"') return Str(s, ref i);
    if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
    if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
    if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
    int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
    double r; double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out r); return r;
  }
  static string Str(string s, ref int i) {
    var sb = new StringBuilder(); i++;
    while (i < s.Length && s[i] != '"') {
      if (s[i] == '\\' && i + 1 < s.Length) {
        i++; char e = s[i];
        if (e == 'n') sb.Append('\n'); else if (e == 't') sb.Append('\t'); else if (e == 'r') sb.Append('\r'); else if (e == 'b') sb.Append('\b'); else if (e == 'f') sb.Append('\f');
        else if (e == 'u' && i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
        else sb.Append(e);
      } else sb.Append(s[i]);
      i++;
    }
    i++; return sb.ToString();
  }
}
}
