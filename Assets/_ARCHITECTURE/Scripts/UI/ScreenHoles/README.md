# Screen holes fader

One full-screen black `Image` with rectangular holes punched through it. Everything behind a hole is
seen clearly; everything else is covered.

## Principle

The fader Image is drawn with the custom UI shader `UI/ScreenHoles`. The shader gets the holes as a
uniform array (`_Holes`, `_HoleParams`, `_HoleCount`). For every pixel it computes the signed
distance to each hole rectangle (rounded-box SDF) and forces the fader's alpha to `0` inside it.

`ScreenHolesFader` only reads the hole `RectTransform`s, converts their world corners into the
**canvas' local space** — the space UI vertices already arrive in once Unity batches a Canvas — and
uploads them. Nothing is cut out of the mesh, no second camera, no stencil masks: one Image, one
draw call, and holes can move, resize and animate for free.

## Scene setup

1. Under your Canvas, create the fader: **UI > Image**, anchors stretched `0..1`, offsets `0`,
   Source Image `None`, Color black with alpha `1`.
2. Add **ScreenHolesFader** to that same GameObject. The material is created at runtime from the
   shader, so nothing needs to be assigned.
3. Create the holes: empty `RectTransform`s (a plain GameObject with a RectTransform is enough)
   placed and sized where the holes should be. Either
   - drag them into the fader's **Holes** list, or
   - add a **ScreenHole** component to each — they register themselves on enable and unregister on
     disable, so toggling the GameObject opens/closes the hole.

Max 32 holes (`ScreenHolesFader.MaxHoles`, mirrored by `MAX_HOLES` in the shader).

## Per-hole settings (`ScreenHole`)

All sizes are in canvas units, i.e. the same numbers as the RectTransform Width/Height.

| Field | Meaning |
| --- | --- |
| `cornerRadius` | rounds the hole corners (`-1` = fader default) |
| `feather` | soft edge width on the inside of the hole (`-1` = fader default) |
| `padding` | grows/shrinks the hole on every side |
| `strength` | `0` = closed, `1` = fully open — tween it to animate |

## Code

```csharp
ScreenHolesFader fader = ScreenHolesFader.Instance;

fader.AddHole(someRect);                        // open a hole
fader.RemoveHole(someRect);                     // close it
fader.SetHoleStrength(someRect, 1f, 0.4f);      // animate a hole open over 0.4 s
fader.FadeTo(0.8f, 0.5f);                       // fade the whole cover
fader.ClearHoles();                             // plain solid fader again
```

Clicks inside a hole pass through to whatever is behind (`clicksPassThroughHoles`), while the
covered area still blocks input — handy for tutorial spotlights.

## Notes

- Holes are axis-aligned: a rotated hole RectTransform is treated as its bounding box.
- The fader and the holes must live under the same Canvas.
- `updateEveryFrame` can be turned off if the holes never move; call `UpdateNow()` yourself instead.
