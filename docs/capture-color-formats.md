# Color formats and watermark time

In **Settings → Screenshot → Magnifier color format**, open the format manager. Enable, reorder, edit or add formats there. The magnifier, color copying and format switching use the same ordered list.

A format contains literal text and case-sensitive parameters. For example:

```text
rgb(%Re, %Gr, %Bl)
#%ReX%GrX%BlX
hsv(%Hu, %Sb%, %Va%)
%Mx,%My — %Rw × %Rh
```

| Parameters | Values |
| --- | --- |
| `Re Gr Bl Al` | Red, green, blue, alpha |
| `Cy Ma Ye Bk` | Cyan, magenta, yellow, black |
| `Hu Hn` | Hue angle, natural hue notation |
| `Si In` | HSI saturation and intensity |
| `Sl Ll Li` | HSL saturation and lightness |
| `Sb Va Br` | HSV saturation and value |
| `Wh Bn` | HWB whiteness and blackness |
| `Lc Ca Cb` | CIE Lab channels, D65 white |
| `Lo Oa Ob Oc Oh` | Oklab and OkLch channels |
| `Xv Yv Zv` | XYZ channels, D65 white |
| `Dr Dv Na` | Packed RGB/BGR decimal and exact color name |
| `Mx My` | Pointer coordinates in physical pixels |
| `Rx Ry Rw Rh` | Selection origin and size in physical pixels |

For byte channels, append `b` for decimal, `x`/`X` for two-digit hexadecimal, `h`/`H` for a hexadecimal high nibble, or `f`/`F` for a normalized fraction. `F` omits the leading zero. Numeric parameters support fractional `f`/`F` or rounded `i`; percentage parameters also support `p`. A trailing percent sign is literal: `%Sb%` displays the saturation followed by `%`.

The editor previews the result and identifies invalid parameters or unsupported suffixes. If every format is disabled, the current format stays available. Restoring defaults supplies RGB, BGR, HEX, #HEX, HSV and HSL.

## Watermark time

Edit watermark content in the watermark tool's text editor. **Insert time** adds a date, time or combined template. Text, font, image and placement stay in that tool's properties.

```text
Captured $yyyy-MM-dd HH:mm:ss$
Copyright $$ OLC
```

Text between dollar signs uses date/time formatting. Common fields are `yyyy`, `MM`, `dd`, `HH`, `mm`, `ss` and `zzz` for milliseconds. Single quotes inside a time block quote literal text. `$$` writes one dollar sign.

Each watermark keeps the timestamp from its creation. Moving, undoing, previewing or exporting it keeps the same text. An unclosed time block is shown literally and logged.

**Settings → Annotation → Watermark → Automatically add watermark** inserts the configured watermark once when a screenshot first receives a valid selection. It is an ordinary editable object: undo or delete removes it, and moving the selection does not add it again. This option does not add a second watermark on every export or apply to recordings.
