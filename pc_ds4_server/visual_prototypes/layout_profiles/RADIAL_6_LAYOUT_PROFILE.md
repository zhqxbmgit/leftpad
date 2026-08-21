# Layout Profile: LeftPad Radial 6

## 1. Profile identity

| Field | Value |
| --- | --- |
| Profile ID | `radial-6` |
| Layout Type | `radial` |
| Slot Count | 6 |
| Selection Model | `AngleSelection` |
| Current runtime compatibility | Existing `RadialSelectionEngine` |
| Selection asset mode | `canonical-transform` |
| Coordinate origin | top-left |
| Angle convention | clockwise from Top |

This profile contains the topology, geometry, transforms, material baseline, and verification rules specific to the approved LeftPad six-direction radial layout. Read it together with `../UI_VISUAL_PACK_GENERATION_SPEC.md`.

The current approved implementation facts come from:

```text
../raster-v5/build_radial_raster_v5.py
../raster-v5/radial-layout-v5.json
../raster-v5/radial-raster-v5-verification.json
```

The V5 Tactical HUD colors are an approved style baseline, not a mandatory palette for every `radial-6` theme. A new reference has visual priority after topology and geometry compatibility are established.

## 2. Interaction Topology

The profile exposes six logical Slot IDs in clockwise order:

| Slot ID | Direction | Angle |
| ---: | --- | ---: |
| 1 | Top | 0° |
| 2 | Upper Right | 60° |
| 3 | Lower Right | 120° |
| 4 | Bottom | 180° |
| 5 | Lower Left | 240° |
| 6 | Upper Left | 300° |

The topology, not the card silhouette, determines compatibility. A new theme can replace wedges with congruent circles, hexagons, diamonds, capsules, or floating cards at these six directional positions while continuing to use the existing `AngleSelection` model, provided the visual layout remains compatible and is verified.

Visual gaps do not produce selection holes. Static PNG alpha is not an interaction mask.

Prohibited selection techniques:

- bitmap hit testing;
- alpha hit testing;
- `GraphicsPath` hit testing;
- deriving a slot from the visible card boundary.

The existing `RadialSelectionEngine` remains responsible for converting direction into a Slot ID. This profile does not modify or implement that engine.

## 3. Approved design-space geometry

The frozen V5 master geometry is:

| Parameter | Value |
| --- | ---: |
| Master canvas | 1254 × 1254 RGBA |
| Wheel center | 627, 627 |
| Outer card radius | 450 px |
| Inner card radius | 198 px |
| Parallel gap target | 16 px |
| Corner radius | 10 px |
| Center radius | 182 px |
| Center inner radius | 154 px |
| Dotted ring radius | 132 px |
| Marker radius | 190 px |
| Marker size | 12 × 12 px |
| Default supersampling | 4× |
| Internal canvas | 5016 × 5016 |
| Downsample | Pillow LANCZOS |

These are design-space parameters. Runtime display size is derived from `ScalePercent`; it is not fixed at 1254 px.

## 4. Slot visual anchors

The approved V5 anchors are:

| Slot | Glyph X | Glyph Y | Label X | Label Y |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 627.000 | 307.000 | 627.000 | 262.000 |
| 2 | 904.128 | 467.000 | 943.099 | 444.500 |
| 3 | 904.128 | 787.000 | 943.099 | 809.500 |
| 4 | 627.000 | 947.000 | 627.000 | 992.000 |
| 5 | 349.872 | 787.000 | 310.901 | 809.500 |
| 6 | 349.872 | 467.000 | 310.901 | 444.500 |

Dynamic glyphs and labels are runtime content. The anchors are visual metadata and contain no action mapping.

## 5. Parallel constant-width gap

Do not manufacture the gap by reducing a 60° logical sector to a 56° visual sector (`60°→56°`). That angular shortcut creates an inner-narrow, outer-wide wedge gap.

Use the verified parallel constant-width gap construction:

1. Define logical radial boundaries at 60° intervals.
2. For each side of the canonical card, offset the boundary line inward by `gapPx / 2`, which is 8 px for the V5 baseline.
3. Intersect the offset line with the inner circle and outer circle.
4. Connect the outer intersections with the outer arc.
5. Connect the inner intersections with the inner arc.
6. Join inner and outer arcs along the offset side boundaries.
7. Apply the 10 px corner treatment.
8. Rotate the same canonical mask to the remaining slots.

For a boundary angle `θ`, radius `r`, and signed perpendicular offset `d`:

```text
t = sqrt(r² - d²)
point = center + radialUnit(θ) × t + perpendicularUnit(θ) × d
```

Use the correct sign for the left and right card boundaries. The resulting card-to-card gap remains approximately 16 px from inner to outer radius.

## 6. Canonical card and slot transforms

The canonical card is Slot 1 / Top. Normal and Selected geometry both derive from the same canonical mask.

`selectionAssetMode` is:

```text
canonical-transform
```

Transforms rotate around `(627, 627)`:

| Slot | Clockwise transform |
| ---: | ---: |
| 1 | 0° |
| 2 | 60° |
| 3 | 120° |
| 4 | 180° |
| 5 | 240° |
| 6 | 300° |

Do not create six separately painted Selected PNGs for this profile. Geometry, alpha, corners, and gaps must remain transform-identical.

## 7. Radial visual layer contract

### 7.1 Base Layer

Contains:

- six Normal cards;
- fixed Center HUD;
- six Normal directional markers;
- fixed borders, rings, and decoration.

Does not contain:

- Selected state;
- glyphs or labels;
- action mappings;
- confirm/cancel prompts;
- controller prompts or changing business text.

The V5 design master is `../raster-v5/radial-base-v5.png`.

### 7.2 Dynamic Selection Layer

Contains one canonical Slot 1 / Top Selected Card:

- selected body illumination;
- Wide Halo;
- Tight Halo;
- Main Cyan Border;
- Ice Core.

It excludes the Center, other cards, glyphs, labels, and business content. The V5 design master is `../raster-v5/radial-selected-card-v5.png`.

### 7.3 Dynamic Content Layer

Runtime draws glyphs, labels, action names, mappings, and confirm/cancel prompts at profile-defined anchors. None of that content is baked into V5 production layers.

## 8. Approved V5 Normal material baseline

### Fill

| Parameter | Value |
| --- | --- |
| Inner | `#0C0F13` |
| Middle | `#14181D` |
| Outer | `#1A1E24` |
| Middle stop | 0.56 |

### Border hierarchy

| Layer | Color | Width | Alpha |
| --- | --- | ---: | ---: |
| Dark backing | `#05070A` | 4.5 px | 107 |
| Main border | `#59636E` | 1.8 px | 217 |
| Fine highlight | `#8996A3` | 0.6 px | 46 |

The target is dark smoked glass with low saturation, subtle blue-gray layering, no visible noise, no bright-blue plastic appearance, and no white border.

## 9. Approved V5 Selected material baseline

### Body

| Parameter | Value |
| --- | --- |
| Inner | `#0D1824` |
| Middle | `#163B5A` |
| Outer | `#245A7E` |
| Middle stop | 0.62 |
| Illumination weight | 75% radial / 25% directional |

The generator applies a continuous baked tone-down after interpolation so the selected body remains dark. Do not interpret the stop colors as permission to fill the whole card with uniform blue.

### Edge and glow hierarchy

| Layer | Color | Radius/Width | Alpha |
| --- | --- | ---: | ---: |
| Wide Halo | `#59D2FF` | 11 px radius | 70 |
| Tight Halo | `#66D8FF` | 4 px radius | 120 |
| Main Cyan Border | `#58CCFF` | 2.8 px | 235 |
| Ice Core | `#E7FAFF` | 0.9 px | 245 |

Directional strength:

| Region | Wide Halo | Tight Halo |
| --- | ---: | ---: |
| Outer Arc | 100% | 100% |
| Side Edges | 80% | 82% |
| Inner Arc | 55% | 60% |

Side and inner halo energy is primarily inward. The outer arc may bloom outward. Bloom must stay localized and must not alter the Center or adjacent slots.

## 10. Approved V5 Center and markers

### Center material

| Layer | Value |
| --- | --- |
| Inner | `#070E17` |
| Middle | `#0B131E` |
| Outer | `#101923` |
| Border | `#505B67` |
| Fine highlight | `#7E8B98` |
| Inner ring | `#697687` |
| Dotted ring | `#8695A5` |

Generator alpha baselines:

| Element | Alpha |
| --- | ---: |
| Center border | 220 |
| Fine highlight | 41 |
| Inner ring | 105 |
| Dotted ring | 92 |

### Markers

| State | Color | Alpha/behavior |
| --- | --- | --- |
| Normal marker | `#8995A2` | 151 |
| Selected marker configuration | `#67D5FF` | no added glow |

The frozen Selected Card does not include the Center or markers. State-preview isolation keeps the Center unchanged.

## 11. Frozen V5 filename compatibility

The approved V5 files are:

```text
../raster-v5/radial-base-v5.png
../raster-v5/radial-selected-card-v5.png
../raster-v5/radial-layout-v5.json
```

Do not rename, overwrite, or regenerate these frozen files merely to match generic names such as `base.png`, `selected.png`, or `layout.json`. A future manifest maps logical roles to actual filenames.

The current frozen directory also retains generator, previews, comparison, verification, and README artifacts.

## 12. Profile-specific verification

In addition to generic verification, `radial-6` requires:

- master canvas exactly 1254×1254 RGBA;
- wheel center `(627, 627)`;
- six slots and exact clockwise ordering;
- canonical mask identity across every transform;
- 16 px parallel gap target;
- 10 px corner geometry;
- center geometry unchanged;
- rotations at 0°, 60°, 120°, 180°, 240°, and 300°;
- Selected body exact alignment with the target slot;
- no Selected pixels altering non-target slot interiors;
- Center unchanged between Base and every Selected preview;
- halo within canvas bounds;
- transparent logical gap samples;
- alpha-zero RGB cleanup;
- deterministic regeneration and freeze hashes.

The frozen V5 verification recorded:

```text
Slot 1 mask SHA-256:
09D5A6A3B4BFC0CD4A48244364205C50C39EED8FB50AC8DD125B148125959C1E

Center mask SHA-256:
79002A3052A786D2BC28AF31EF8B205D4619CE458E0C6C8B97EE70720121A472
```

All six transform comparisons reported zero differing mask pixels above the verification tolerance. Every preview reported zero changed Center pixels and zero changed non-target slot pixels.

## 13. Radial review contract

Before Human Approval, generate:

- Normal/Base preview;
- representative Selected Slot 2 preview;
- six-slot All Slots Selected review;
- reference/Normal/Selected comparison sheet.

Use the same review background and scale across panels where practical. Inspect rotation, gap consistency, halo clipping, selected-body darkness, Center hierarchy, and non-target isolation.

## 14. Visual-only change compatibility

The following may remain compatible with `radial-6` when Slot IDs and six directional positions are unchanged:

- wedge-to-circle visual change;
- wedge-to-hexagon visual change;
- floating congruent rectangles;
- congruent diamonds or capsules;
- alternate fixed Center treatment;
- different material, border, or glow language.

These changes require a new Visual Pack and may require updated visual anchors, but they do not automatically require a new Selection Engine.

If the reference changes to a grid, horizontal strip, cross, a different slot count, or different navigation semantics, classify it as an Interaction Topology change and follow the Runtime Capability Gap process in the generic specification.

## 15. Radial-specific Deprecated / Avoid Unless Explicitly Requested

- using `60°→56°` to create the visual gap;
- treating transparent radial gaps as selection holes;
- bitmap, alpha, or `GraphicsPath` hit testing;
- manually drawing six cards instead of rotating one canonical mask;
- six independent Selected assets for congruent slots;
- remapping or inpainting an already composed eight-slot screenshot to reconstruct hidden six-slot material;
- repeated whole-image generation that drifts the approved geometry;
- runtime Gaussian Blur for every radial selection change;
- a large procedural runtime material renderer when the material can be baked offline.

## 16. Approval and runtime boundary

Script PASS, geometry PASS, transform PASS, and verification PASS do not replace Human Approval. Freeze and Runtime Integration occur only after a reviewer accepts the Normal, Selected, All Slots, and Comparison artifacts.

This profile defines a visual and compatibility contract only. It does not create a Theme Manager, manifest parser, renderer, cache implementation, or Selection Engine.
