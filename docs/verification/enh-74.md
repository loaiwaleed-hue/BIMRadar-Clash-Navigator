# Verification: enhancement #74 (clash: section box with the isolated elements)

**Kind of check: code read plus a pure-math check in Python. This covers the box math only.** Nothing was compiled or run against Revit. The Revit writes (section box, isolation, work view) are tested by the owner on a model copy.

## Investigation: what happened before the change
- **Which box was set.** For a clash, `ClashSetup` computed `clashBox`. `Execute` used it first (`UseBox && clashBox != null`), before the plane and point branches. The camera/point box was not used for clashes.
- **Checkbox off.** `Go` copies the checkbox into `UseBox`. With it off, every box branch was skipped and the `else` set `IsSectionBoxActive = false`. Isolation is independent of `UseBox` (clash: always on), so it stayed on. Correct, and unchanged.
- **Isolation and the box.** Both were in the same "Viewpoint Navigator" transaction, so isolation did not skip the box.
- **Linked elements.** `ApplyIsolation` isolates the link instance and hides the other model categories. `HitBox` already moved the linked element's bounding box through `Link.GetTotalTransform()` (all 8 corners, then min/max), so linked elements were part of the box.
- **Where it fell short.**
  - The box started from the clash point and added a fixed 0.3 m pad, not Box size / 2.
  - It was then clamped to Box size around the clash point (`cap`). Elements larger than that were cut off, so the isolated IDs did not show in full.
  - The "Duplicate 3D view" work view copied the VP view's box. When the checkbox was off, it created a point box anyway.

## What changed (`Navigator/NavHandler.cs`, marked `// #74`)
- New `ClashBox(doc, c, hits, out lo, out hi)`.
  - Elements found: min/max over all `HitBox` boxes (host, and linked with the link transform), grown by `max(BoxM / 2, 0.5 m)` on each side. No clamp and no forced inclusion of the clash point.
  - No element found: a box of `max(BoxM, 1 m)` centred on the clash point (unchanged behaviour).
- `ClashSetup` uses `ClashBox`. The camera placement from the box is unchanged.
- `Execute`: unchanged. The clash box and the isolation are already applied together in one transaction. Checkbox off gives no box, isolation stays.
- `DuplicateOrtho`: for a clash, the work view gets `ClashBox` when "Section box" is checked and no box when it is off. Isolation is applied in the same transaction as before. Non-clash items keep the old behaviour.
- No new Revit API members were used. `Compat.cs` is untouched.

## Math check (Python, sample numbers)
The same formula as `ClashBox`. Host element 0..1 m cube. Linked element, already transformed, 0.5..2 x 0.5..1.5 x 0..0.4 m.

| case | min (m) | max (m) |
|---|---|---|
| Box size 3.0 | -1.5, -1.5, -1.5 | 3.5, 3.0, 2.5 |
| Box size 0.5 | -0.5, -0.5, -0.5 | 2.5, 2.0, 1.5 |
| Box size 0.2 (grow floor 0.5 m) | -0.5, -0.5, -0.5 | 2.5, 2.0, 1.5 |
| no element, point (10, 20, 3), Box 3.0 | 8.5, 18.5, 1.5 | 11.5, 21.5, 4.5 |

These match the ticket: the union of the element boxes grown by Box size / 2 (min 0.5 m), or Box size centred on the point.

## Not verified here
Compiling, `SetSectionBox` on a perspective VP view, linked bounding boxes in a real model, and the work view. The owner tests these in Revit.
