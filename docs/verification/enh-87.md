# Verification #87: perspective fit with real horizontal / vertical half angles

Scope: this covers the distance maths only (reads of the formula, no Revit API calls). Writes (SetOrientation, the "Clash Viewer fit" transaction) and the real `UIView.GetZoomCorners` values are NOT verified here. They are proven only by the owner's Revit test on a model copy. Revit untested.

Formula per box corner, relative to the target: lateral offsets a (right), b (up), depth z along dir (positive = far side).
Required eye distance = max(|a| / tan(hx), |b| / tan(hy)) - z. d = max over the 8 corners x 1.3, minimum 2 m.

API note: `UIView.GetZoomCorners()` (IList<XYZ>, 2 corners) exists in Revit 2022-2027 (since 2011).

Script (run with `python3 -I`):

```python
import math, itertools
def fit(box_min, box_max, target, dirv, up, hx, hy, margin=1.3, dmin=2.0):
    dot=lambda a,b:sum(x*y for x,y in zip(a,b))
    cross=lambda a,b:(a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
    right=cross(dirv,up); req=0
    for c in itertools.product(*zip(box_min,box_max)):
        o=[c[i]-target[i] for i in range(3)]
        a,b,z=dot(o,right),dot(o,up),dot(o,dirv)
        req=max(req,max(abs(a)/math.tan(hx),abs(b)/math.tan(hy))-z)
    return max(dmin,req*margin), right
def inside(box_min,box_max,target,dirv,up,hx,hy,d):
    dot=lambda a,b:sum(x*y for x,y in zip(a,b))
    cross=lambda a,b:(a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
    right=cross(dirv,up)
    for c in itertools.product(*zip(box_min,box_max)):
        o=[c[i]-target[i] for i in range(3)]
        depth=d+dot(o,dirv)
        if abs(dot(o,right))>depth*math.tan(hx)+1e-9 or abs(dot(o,up))>depth*math.tan(hy)+1e-9: return False
    return True
D=math.radians
dirv=(0,1,0); up=(0,0,1)
# vertical dominates: tall wall 2 m wide, 8 m high, target at the top corner
bmin=(-1,-0.2,-8); bmax=(1,0.2,0); tgt=(0,0,0)
d,_=fit(bmin,bmax,tgt,dirv,up,D(25),D(15),1.0)
print("hx25 hy15 d(margin 1.0) =",round(d,3),"m; all corners inside:",inside(bmin,bmax,tgt,dirv,up,D(25),D(15),d))
print("old uniform 25deg radius formula:", round(max(math.dist(c,tgt) for c in itertools.product(*zip(bmin,bmax)))/math.tan(D(25)),3))
d13,_=fit(bmin,bmax,tgt,dirv,up,D(25),D(15)); print("with margin 1.3:",round(d13,3),inside(bmin,bmax,tgt,dirv,up,D(25),D(15),d13))
# which axis dominates
print("vertical term", 8/math.tan(D(15)), "horizontal term", 1/math.tan(D(25)))
# horizontal dominates: wide wall
bmin=(-10,-0.2,-1); bmax=(10,0.2,1)
d,_=fit(bmin,bmax,tgt,dirv,up,D(25),D(15),1.0); print("wide:",round(d,3),inside(bmin,bmax,tgt,dirv,up,D(25),D(15),d))
# depth term: box extends toward the far side only, 5 m deep
bmin=(-0.5,0,-0.5); bmax=(0.5,5,0.5)
d,_=fit(bmin,bmax,tgt,dirv,up,D(25),D(25),1.0); print("deep:",round(d,3),inside(bmin,bmax,tgt,dirv,up,D(25),D(25),d))
# real angle from zoom corners
eye=(0,-10,0); corners=[(-5,0,-2),(5,0,2)]
hx=math.atan(max(abs(c[0]-eye[0]) for c in corners)/10); hy=math.atan(max(abs(c[2]-eye[2]) for c in corners)/10)
print("real hx,hy deg:",round(math.degrees(hx),2),round(math.degrees(hy),2))
```

Output:
```
hx25 hy15 d(margin 1.0) = 30.056 m; all corners inside: True
old uniform 25deg radius formula: 17.295
with margin 1.3: 39.073 True
vertical term 29.85640646055102 horizontal term 2.1445069205095586
wide: 21.645 True
deep: 2.0 True
real hx,hy deg: 26.57 11.31
```

Sample with the vertical extent dominating (hx 25 deg, hy 15 deg, 2 m x 8 m wall, target at the top corner): the vertical term is 29.9 m against 2.1 m horizontal, d = 30.06 m (margin 1.0) and all 8 corners lie inside the frustum. The old uniform 25 deg formula gave 17.3 m, which would cut the top and bottom (the reported bug).
