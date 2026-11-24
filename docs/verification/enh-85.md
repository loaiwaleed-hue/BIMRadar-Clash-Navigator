# Verification #85: perspective camera fit and button layout

Scope: this covers the **maths** (reads of nothing, writes of nothing in Revit). The Revit writes (SetOrientation, layout) are not verified here; they need the owner's Revit test on a model copy.

## Maths check (Python, sample r = 1 m, f = 0.25)

Formulas used in `NavHandler.FitPerspective`:
- d = max(1.5 m, r / tan(25 deg) * 1.1)
- w = 2 * d * tan(25 deg) (view width at the target)
- shift s = w * f / 2 along RightDirection (right = dir x up), toward the side the form covers
- eye = target' - dir * d

```python
import math
t=math.tan(math.radians(25))
r=1.0; f=0.25
d=max(1.5, r/t*1.1)
w=2*d*t
s=w*f/2
print("r=%.3f m  d=%.4f m  w=%.4f m  shift=%.4f m"%(r,d,w,s))
assert abs(d-2.3590)<1e-3 and abs(w-2.2)<1e-9 and abs(s-0.275)<1e-9
# r small -> minimum
assert max(1.5,0.2/t*1.1)==1.5
# clash lands at screen fraction: offset/w = f/2 => centre of free area
assert abs(s/w-f/2)<1e-12
print("ok")
```

Output:

```
r=1.000 m  d=2.3590 m  w=2.2000 m  shift=0.2750 m
ok
```

So d = 2.359 m, w = 2.2 m, shift = 0.275 m. The shift is f/2 of the view width, which puts the clash in the middle of the free (1 - f) part only for small f; with the form on the left the clash sits at 0.5 + f/2 of the width. A small item (r = 0.2 m) gives the 1.5 m minimum.

## Not verified here
- Compilation (no .NET / nuget in the sandbox); GitHub Actions builds it.
- Real camera result, overlap direction (the sign of the shift), and the new button rows: owner test in Revit.
- API members used exist in Revit 2022-2027: View3D.IsPerspective, View.SetOrientation, ViewOrientation3D, UIView.GetWindowRectangle.
