# BIMRadar icons

Shared style rules for the BIMRadar Revit ribbon tab. Other BIMRadar repos reuse these rules.

## Style

- Line icons, transparent background, no fill except small accents (e.g. a centre dot).
- Master size: `viewBox="0 0 32 32"`, exported at 32 px (large button) and 16 px (small button).
- Stroke width 2 (1.5 for fine detail), round caps and joins.
- Keep shapes bold and simple: they must read at 16 px.
- If the 32 px design is muddy at 16 px, add a simplified `<name>-16.svg` master (stroke 3, fewer details). Its viewBox stays 0 0 32 32.
- One accent colour per panel. Dark-theme variants (`*.dark.svg`) use a lighter tint for the Revit dark UI.

| Panel  | Light     | Dark      |
|--------|-----------|-----------|
| Health | `#dc2626` | `#f87171` |
| Check  | `#16a34a` | `#4ade80` |
| Fix    | `#ea580c` | `#fb923c` |
| Review | `#2563eb` | `#60a5fa` |
| Audit  | `#374151` | `#9ca3af` |

Only the Review pair is in use so far; the other dark tints are proposals (Tailwind 400 shades).

## Files

- `<name>.svg`, `<name>.dark.svg`: 32 px masters.
- `<name>-16.svg`, `<name>.dark-16.svg`: simplified 16 px masters (where present).
- `png/<name>-32.png`, `-16.png`, `<name>.dark-32.png`, `<name>.dark-16.png`: exports.

## Re-export

Run from this folder (needs `rsvg-convert`, from librsvg):

```sh
for f in clash-navigator next previous; do
  for s in "" .dark; do
    rsvg-convert -w 32 -h 32 $f$s.svg -o png/$f$s-32.png
    src=$f$s.svg; [ -f $f$s-16.svg ] && src=$f$s-16.svg
    rsvg-convert -w 16 -h 16 $src -o png/$f$s-16.png
  done
done
```

Then copy the light PNGs to `src/BIMRadar.ClashNavigator/Resources/` (`ClashNavigator32.png`, `Next16.png`, ...) and, for pyRevit buttons, to each pushbutton's `icon.png` (32 px; `icon.dark.png` for the dark variant).
