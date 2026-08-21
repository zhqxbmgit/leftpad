# RADIAL-8 Layout Profile

## Profile Identity

| Field | Value |
| --- | --- |
| `layoutProfile` | `radial-8` |
| Layout family | `radial` |
| Status | `experimental` |
| Runtime | `not integrated` |
| Runtime Capability Gap | `EXISTS` |

`radial-8` is an offline Visual Pack Toolchain profile. A pack may pass schema,
Validator, Generator, and Comparison checks while remaining unavailable to the
current LeftPad Runtime. Toolchain validity must never be reported as Runtime
compatibility.

## Topology

`slotCount` is exactly `8`.

Recommended clockwise ordering from top:

| Slot | Name | Reference angle |
| --- | --- | --- |
| 1 | Top | 0° |
| 2 | Upper Right | 45° |
| 3 | Right | 90° |
| 4 | Lower Right | 135° |
| 5 | Bottom | 180° |
| 6 | Lower Left | 225° |
| 7 | Left | 270° |
| 8 | Upper Left | 315° |

The ordered values in `layout.json.slotAnglesDegrees` are authoritative. Each
`slots[]` record must use the same ID/order and angle as the corresponding
entry in that array. The current experimental contract uses the evenly spaced
reference angles above.

## Geometry Contract

The master layout remains a `1254 × 1254` RGBA canvas. Required logical fields
and their JSON representation are:

| Logical field | `layout.json` field |
| --- | --- |
| `canvasWidth` | `canvas.width` |
| `canvasHeight` | `canvas.height` |
| `wheelCenter` | `wheelCenter.{x,y}` |
| `outerRadius` | `geometry.outerRadius` |
| `innerRadius` | `geometry.innerRadius` |
| `gapPx` | `geometry.gapPx` |
| `cornerRadius` | `geometry.cornerRadius` |
| `centerRadius` | `geometry.centerRadius` |
| `centerInnerRadius` | `geometry.centerInnerRadius` |
| `slots[]` | `slots[]` |

The required radius relationship is:

```text
outerRadius > innerRadius > centerRadius > centerInnerRadius > 0
```

`gapPx` and `cornerRadius` must be non-negative. `wheelCenter` is `(627, 627)`
for the current 1254 master contract.

Each `slots[]` entry contains:

| Logical field | JSON field |
| --- | --- |
| `id` | `slot` |
| `angle` | `angleDegreesClockwiseFromTop` |
| `glyphAnchor` | `glyphAnchor.{x,y}` |
| `labelAnchor` | `labelAnchor.{x,y}` |

Anchors must be finite points within the master canvas.

## Selection Asset Mode

Preferred mode: `canonical-transform`.

An eight-slot radial topology remains rotationally symmetric, so one canonical
Slot 1 Selected asset can be rotated through the angles in `layout.json`.

The profile design also allows a future `per-slot` representation. The current
v1 manifest and Toolchain asset contract expose one `selected` filename only,
so `per-slot` remains reserved until its filename mapping is formally defined.
It is not accepted by the current Validator or Generator Wrapper.

## Center Contract

Center is an independent visual layer. It may contain:

- center base material;
- an optional static center glyph.

It must not contain:

- Runtime action text;
- keyboard mappings;
- DS4 mappings;
- user-specific data.

Dynamic Runtime content is outside this experimental layout profile.

## Reference Image Analysis

The current source reference belongs to the `radial` family, but it is not a
`radial-6` topology: it contains eight outer segments. Reusing the radial-6
profile would misrepresent slot ordering, transforms, and anchors. It therefore
requires the new `radial-8` layout profile.

The reference supplies visual language only. Business icons, numbers, action
prompts, keyboard mappings, and DS4 mappings are not part of the Visual Pack
contract.

## Toolchain and Runtime Boundary

The offline Toolchain may:

- validate radial-8 manifests, layouts, PNGs, alpha, and hashes;
- package designer-exported radial-8 assets;
- generate Reference/Base/Selected/All Slots comparison sheets.

The current Runtime may not discover, select, compose, or execute radial-8.
Runtime integration requires a separate capability task and is explicitly out
of scope for this profile definition.
