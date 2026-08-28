# LeftPad UI Theme Package V2 — Normative Protocol

Status: Phase 0 contract with the coordinate/placement amendment. This document,
`schemas/ui-theme-v2.schema.json`, and `validate_theme_v2.py` define the V2
package boundary. Phase 0 does not add a runtime loader, renderer, composer,
discovery path, or theme switch.

Normative words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are requirements.
If prose and schema disagree, the schema owns JSON shape and this document owns
cross-field semantics. The semantic validator is the executable interpretation.

## 1. Version and compatibility boundary

- A V2 manifest MUST set `protocolVersion` to integer `2`. This selects runtime
  schema compatibility and changes only with a protocol revision.
- `packageRevision` is a positive integer owned by the theme package. It changes
  when that package's manifest/assets are revised and MUST NOT be interpreted as
  a protocol version.
- `surface` is exactly `radial-overlay` in V2 Phase 0. Unknown surfaces are
  rejected. Future surface adapters require an explicit future schema/protocol
  extension; no other adapter is implied now.
- V2 is additive. Existing V1 manifests, assets, discovery, loading, selection,
  actions, mappings, persistence, DPI behavior, and fallback remain unchanged.
- A V2 parser MUST NOT reinterpret a V1 manifest as V2. A V1 parser MUST NOT be
  changed to discover V2 during Phase 0.
- An unknown protocol version, invalid candidate, unsupported required
  capability, missing required state, missing asset, or hash mismatch is a hard
  candidate failure. If an active theme exists it remains active atomically.
- `fallback.onInvalidCandidate` and `fallback.onUnsupportedVersion` are therefore
  fixed to `retain-active`. `startupThemeId` names the separately installed safe
  startup theme; it is an ID, never a filesystem path.
- Phase 0 permits only `transitions.mode: "instant"`. No animation timing is
  implied or reserved by arbitrary fields.

## 2. Closed manifest and coordinate/placement system

Unknown fields are rejected at every schema-defined object. Layer, anchor, mask,
and visual-region authored geometry is in Reference Space; `contentOrigin` and
`activationAnchor` are the explicitly identified exceptions in Logical Surface
Space. Manifests MUST NOT contain physical DPI sizes or per-monitor cache
coordinates.

- **Reference Space** is the authored pixel grid defined by
  `referenceCanvas.width/height`. Its bounds are
  `[0, referenceWidth) x [0, referenceHeight)`.
- `referenceCanvas` is `sRGB` with `straight` alpha.
- **Logical Surface Space** is the complete logical overlay surface defined by
  `referenceScale.logicalWidth/logicalHeight`. Its bounds are
  `[0, logicalWidth) x [0, logicalHeight)`. These dimensions are the future
  overlay logical bounds, not a content bounding box, an outer contain viewport,
  or an HWND offset. Each dimension is greater than zero and at most 4096 logical
  units.
- `referenceScale.fit` is exactly `contain`. The author MUST explicitly provide
  `referenceScale.contentOrigin` in Logical Surface units. It is the top-left of
  the contain-scaled Reference artwork in Logical Surface Space; no implicit
  centering or other alignment state exists.
- Bounds use `{x, y, width, height}` and MUST be finite, positive-sized, and
  wholly inside the reference canvas.
- `placement.activationAnchor` is a finite point in Logical Surface Space. It is
  the point within the complete logical surface that `ShowAt(screenPoint)` aligns
  with `screenPoint`; it affects visual placement only.

The Reference-to-Logical transform is exactly:

```text
scale = min(logicalWidth / referenceWidth,
            logicalHeight / referenceHeight)
contentWidth  = referenceWidth  * scale
contentHeight = referenceHeight * scale
logicalX = contentOrigin.x + referenceX * scale
logicalY = contentOrigin.y + referenceY * scale
```

`contentOrigin.x/y` MUST be non-negative, and the content rectangle MUST remain
inside the Logical Surface:

```text
contentOrigin.x + contentWidth  <= logicalWidth
contentOrigin.y + contentHeight <= logicalHeight
```

Semantic validation uses an absolute tolerance of `1e-9` logical units for only
the two right/bottom containment comparisons. The stored origin is never changed
or snapped by that tolerance. Negative origins, implicit surface expansion,
implicit clipping, and content-driven HWND translation are forbidden in V2 MVP.
Letterbox pixels not covered by authored content are transparent; Runtime MUST
NOT synthesize black, white, or theme-colored fill.

Runtime implementation in a later phase MUST compute this one
Reference-to-Logical transform and then the Logical-to-Physical DPI transform.
It MUST NOT feed Reference Space directly into native window coordinates or
render a low-resolution logical bitmap and subsequently upscale it. A straight-
alpha source is normalized to a PArgb master, resampled directly to the final
Physical content rectangle, and composed into the transparent full Physical
Surface cache.

### 2.1 Physical Surface and deterministic DPI rounding

**Physical Surface Space** is derived from Logical Surface Space at the current
monitor DPI. It determines the layered HWND backing-bitmap size and the
`UpdateLayeredWindow` width and height. `LogicalEdgeToPhysical` is the single
normative conversion for every surface edge, content edge, and activation-anchor
coordinate:

```text
LogicalEdgeToPhysical(value, dpi) =
    Round(value * dpi / 96, midpoint = AwayFromZero)
```

All calculations remain continuous logical values until an edge or anchor is
converted. Surface physical width/height are the converted logical right/bottom
edges. A content rectangle converts `left`, `top`, `right = x + width`, and
`bottom = y + height` independently; physical width is `right - left` and
physical height is `bottom - top`. Runtime MUST NOT round `x` and `width`
separately and add them, because that can accumulate drift. An activation anchor
is converted directly with the same function.

For a physical screen point supplied to `ShowAt`, placement is exactly:

```text
hwndLeft = screenPointPhysical.x - LogicalEdgeToPhysical(activationAnchor.x, dpi)
hwndTop  = screenPointPhysical.y - LogicalEdgeToPhysical(activationAnchor.y, dpi)
```

`screenPoint` therefore denotes surface center only when the manifest explicitly
sets the activation anchor to `(logicalWidth / 2, logicalHeight / 2)`.

### 2.2 Selection authority and WheelCenter isolation

`placement.activationAnchor` affects visual overlay placement only. It MUST NOT
participate in angle calculation, deadzone, slot boundaries, tie-breaks, or slot
selection. The existing input/session selection origin and `LayoutDefinition`
remain the selection authority.

For V2 full-state-frame and layered-state rendering,
`LayoutDefinition.WheelCenter` does not define the Visual Surface center or HWND
anchor and need not coincide with Reference Canvas center, Logical Surface
center, or `activationAnchor`. Full-state-frame rendering does not use
`WheelCenter` for a visual transform. The value remains layout/profile authority
for existing logic and legacy rendering that requires layout geometry.

The V1 compatibility mapping remains unchanged: a `1254 x 1254` reference with
WheelCenter `(627, 627)` mapped into the existing `280 x 280` logical surface
produces activation anchor `(140, 140)`. V1 adapters derive that value from the
legacy reference/layout scale; they do not introduce a new hard-coded placement
constant.

### 2.3 Normative non-square examples

Centered artwork example:

```text
referenceCanvas = 800 x 500
logical surface = 400 x 400
scale = 0.5
content size = 400 x 250
contentOrigin = (0, 75)
activationAnchor = (120, 200)
Reference (0, 0) -> Logical (0, 75)
Reference (800, 500) -> Logical (400, 325)
Reference (400, 250) -> Logical (200, 200)
```

`ShowAt(screenPoint)` aligns `screenPoint` to Logical `(120, 200)`, not to the
surface center `(200, 200)`.

Non-centered artwork example:

```text
referenceCanvas = 800 x 500
logical surface = 500 x 300
scale = 0.6
content size = 480 x 300
contentOrigin = (20, 0)
activationAnchor = (100, 150)
Reference (0, 0) -> Logical (20, 0)
Reference (800, 500) -> Logical (500, 300)
```

`layoutProfile` is the primary authoring topology. `compatibleLayouts` is a
non-empty unique set containing it. V2 initially recognizes `radial-6` and
`radial-8`; both are stable runtime-integrated profiles at this baseline.

## 3. Alpha pipeline

The package-to-presentation conversion is frozen as follows:

```text
SOURCE / PACKAGE SPACE
PNG-decoded RGBA, sRGB, straight alpha
  -> decode and normalize to a premultiplied master
PREMULTIPLIED RESAMPLING SPACE
ASSET TRANSFORM / SCALE in 32-bpp premultiplied alpha / Format32bppPArgb
  -> high-quality transform/scale directly to the final Physical target size
RUNTIME CACHE SPACE
32-bpp premultiplied alpha / Format32bppPArgb
  -> ordinary alpha composition
  -> UpdateLayeredWindow with per-pixel alpha
```

Theme authors and PNG producers MUST supply standard sRGB straight-alpha PNGs.
They MUST NOT hand-author premultiplied PNG data. A future Runtime cache builder
or normalized-renderer preparation step owns straight RGBA to PArgb conversion.

The conversion to premultiplied representation occurs before interpolation so
transparent-pixel RGB cannot create black/white/color fringes during resampling.
The normalized premultiplied master is transformed directly to the final
Physical content rectangle determined from Reference Space, `contentOrigin`,
logical surface size, user scale, and current DPI. An intermediate low-resolution
logical raster that is subsequently upscaled is forbidden. Composition into the
transparent full Physical Surface remains premultiplied through the final
layered-window bitmap.

This ordering matches the stable V1 pipeline audited in Phase 0: decode into
`Format32bppPArgb`, direct high-quality scaling into the Physical-size PArgb
cache, SourceCopy/SourceOver composition, then `UpdateLayeredWindow` with
`AC_SRC_ALPHA`. Phase 0 documents this invariant but does not modify Runtime.

Phase 1 acceptance MUST include synthetic assets containing a
semi-transparent red edge, soft white glow, dark shadow, and transparent colored pixel. The
decoded/scaled/composited result MUST show no black fringe, white fringe,
unexpected RGB bleed, or alpha inversion at every supported DPI/cache size.

## 4. Runtime render strategies and authoring provenance

`renderStrategy` describes only how Runtime plays compiled package assets. It is
exactly one of:

- `full-state-frame`: required state assets include a required full-canvas
  `stateAsset` layer. It declares required capability `fullStateFrame` and MUST
  declare exactly one `compatibleLayouts` entry. A different layout requires a
  different theme package in V2 MVP.
- `layered-state`: shared/state-specific asset layers are composed in `zIndex`
  order. It declares required capability `layeredState`. It MAY declare multiple
  compatible layouts only when the same declared assets, states, anchors, masks,
  and regions are genuinely valid for every listed topology. V2 MVP has no
  per-layout asset-variant map; layout-specific scenes require separate packages.

For equal `zIndex`, declaration order is the deterministic tie-breaker.

Optional `authoring.method` is limited to `external-artwork`, `procedural`, or
`mixed`. It records provenance for compiler reports and review only. It MUST NOT
affect renderer dispatch, Runtime capabilities, selection, state lookup, DPI,
fallback, or package acceptance beyond validating the metadata value. A theme
produced by Pillow/NumPy therefore declares `authoring.method: "procedural"`
while its compiled `renderStrategy` remains `full-state-frame` or
`layered-state`. Runtime neither knows nor cares how those assets were authored.

## 5. State contract

Every compatible package MUST contain:

- `idle`, with `slotId: null`;
- `selected-1` through `selected-N`, where `N` is the largest slot count among
  `compatibleLayouts`; each `slotId` MUST equal its suffix.

Missing required states fail validation; they MUST NOT silently fall back to
`idle`. Reserved state names are `pressed-1..8`, `confirm`, `cancel`, `opening`,
and `closing`. Presence of any reserved state requires the `futureStates`
capability, so current runtime capability negotiation may reject it.

Each state maps state-layer IDs to package-relative asset paths using `assets`.
Every required `stateAsset` layer MUST have a binding in every required state.
Bindings to an unknown or non-state layer are invalid.

Visibility selectors on layers and anchors are deterministic: `all`, `selected`,
`pressed`, or an exact declared state name. An unknown selector is invalid.

## 6. Layer and ownership contract

Every layer declares `id`, `kind`, `zIndex`, `bounds`, `visibleStates`,
`ownership`, and `required`.

| Kind | Asset source | Purpose |
| --- | --- | --- |
| `stateAsset` | state `assets` binding | state-dependent authored pixels |
| `staticAsset` | layer `asset` | shared authored pixels |
| `dynamicText` | no asset | runtime-owned text surface |
| `dynamicGlyph` | no asset | runtime-owned glyph surface |
| `occlusionAsset` | layer `asset` | authored foreground cover |
| `safeSurfaceAsset` | layer `asset` | authored visual safe-area aid |

Asset-backed layer kinds MUST have `asset`; state and dynamic kinds MUST NOT.
Layer ownership is `STATIC`, `DYNAMIC`, or `STATE_ASSET` and is coherent with
kind: shared/occlusion/safe-surface assets are STATIC, dynamic text/glyph layers
are DYNAMIC, and state assets are STATE_ASSET.

`elementOwnership` is mandatory and non-empty. Each semantic element appears
exactly once with owner `STATIC`, `DYNAMIC`, or `STATE_ASSET` and references one
layer of the same ownership. `DYNAMIC` entries also declare `contentKey`.
Conflicts, duplicates, unowned layers, dynamic anchors without matching DYNAMIC
entries, and unknown layer references fail validation.

Ownership means:

- STATIC: pixels are package-authored and invariant across runtime states.
- DYNAMIC: runtime supplies user/action-dependent content through `contentKey`.
- STATE_ASSET: package-authored pixels are selected by the declared state map.

## 7. Dynamic anchors and styles

Each anchor declares `id`, `layerId`, `role`, `bounds`, horizontal and vertical
alignment, `overflowPolicy`, `minimumScale`, `rotation`, `visibleStates`, and
`styleRole`. A glyph anchor also declares `glyphRole`; `safeSurface` may name a
safe-surface mask. Text anchors bind `dynamicText` layers and glyph anchors bind
`dynamicGlyph` layers.

Overflow is explicit: `ellipsis`, `shrink`, `clip`, or `hide`. Runtime MUST
apply the declared policy and MUST NOT invent an unbounded resize.

### 7.1 Dynamic layout, rotation, and overflow semantics

Empty content is nothing to render: it produces no pixels and does not enter
fit or overflow processing. After non-empty dynamic content, style, glyph
source, and a stable Runtime-session font fallback have been resolved, the
normative pipeline is:

1. choose a candidate scale;
2. perform layout and wrapping at that scale;
3. enforce `maxLines` during line generation when it is present;
4. compute the unrotated layout box;
5. apply horizontal and vertical alignment inside the anchor;
6. rotate the aligned geometry around the anchor center;
7. compute the final rotated continuous geometry bounds;
8. apply the overflow-policy fit decision in Reference Space;
9. transform Reference Space to Logical and Physical Space;
10. rasterize and apply the final anchor clip.

Alignment occurs before rotation. The horizontal and vertical alignment values
position the unrotated layout box inside the anchor; Runtime MUST NOT translate,
nudge, recenter, or snap the geometry back inside after rotation. The rotation
center is the geometric center of the anchor bounds:

```text
centerX = anchor.x + anchor.width / 2
centerY = anchor.y + anchor.height / 2
```

`rotation` is measured in degrees in screen coordinates (`+x` right, `+y`
down). Zero means no rotation. Positive `rotation` is clockwise and negative
`rotation` is counter-clockwise. Text, runtime symbols, and theme-asset glyphs
all use this same anchor-center contract.

Fit is evaluated after rotation. Reference Space continuous geometry is the
semantic fit authority; Physical anti-aliased pixel extents are not. Styled
Semantic Geometry includes the visible fill, outline, and finite shadow support
defined below. A layout
fully fits only when its line count is no greater than `maxLines` when that
field is present, it needs neither ellipsis nor clipping to make the original
semantic content legal, and its final rotated continuous geometry bounds are
completely contained by the anchor bounds. An ellipsized representation is not
a complete fit of the original content; it is eligible for rendering only when
the truncated representation itself satisfies the declared line limit and
post-rotation geometry containment. Dynamic geometry containment uses a
deterministic absolute tolerance of `1e-9` Reference units. The tolerance does
not move or resize the geometry.

The same Theme, mapping content, and resolved font role MUST produce the same
chosen scale, ellipsized content, and show/hide decision at 96, 120, 144, 168,
and 192 DPI. DPI MUST NOT change the chosen scale, ellipsized content, or
show/hide decision; DPI changes only the final Physical raster size. Font
fallback MUST remain stable across states and DPIs within one Runtime session.

When present, `maxLines` participates in layout for every policy. It
participates in fit for `ellipsis`, `shrink`, and `hide`; `clip` still limits
line generation to the declared number of lines, without adding an ellipsis
marker, but does not require the rotated geometry to fit. When `maxLines` is
absent there is no independent line-count limit; anchor bounds and the selected
overflow policy still apply. Each `shrink` candidate is laid out and wrapped
again because its line breaks may change.

| Policy | Resize | Ellipsis | Post-rotation fit required | Terminal behavior |
| --- | --- | --- | --- | --- |
| `ellipsis` | No; authored style size | Yes | Yes | Render the longest fitting ellipsized representation, or hide if even the marker cannot fit |
| `shrink` | Yes, from `1.0` down to `minimumScale` | No | Yes | Render at the largest fitting representable scale, or hide if `minimumScale` cannot fit |
| `clip` | No; authored style size | No | No | Render semantic geometry and strictly clip final pixels to the anchor |
| `hide` | No; authored style size | No | Yes | Render only if the authored-size content fits; otherwise hide |

`ellipsis` uses authored size and MUST NOT shrink. If the original content does
not fit, Runtime selects the longest leading sequence of Unicode text elements
that, followed by one U+2026 ellipsis marker, satisfies `maxLines` and the
post-rotation fit requirement. The layout, alignment, and rotation are repeated
for each candidate. If even the ellipsis marker cannot fit, the entire dynamic
element is hidden. It MUST NOT be clipped and MUST NOT invalidate the Theme.

`shrink` first tests scale `1.0`. If that does not fit, it searches
`minimumScale <= scale < 1.0` and selects the largest fitting scale that the
Runtime can express. Every candidate repeats font sizing, wrapping,
`maxLines`, alignment, rotation, and final geometry measurement. Scale
selection MUST be deterministic and monotonic; a Runtime with discrete font or
scale precision MUST use one fixed Reference-space precision, document and test
it, and keep it independent of DPI. If `minimumScale` still does not fit, the
entire dynamic element is hidden. `shrink` MUST NOT fall back to ellipsis.
`shrink` MUST NOT fall back to clip, overflow the anchor, or invalidate the
Theme candidate.

`hide` tests authored size only and MUST NOT shrink or ellipsize. It renders the
element only when authored-size layout, alignment, and rotation fully fit;
otherwise the entire element is hidden.

`clip` uses authored size, performs normal layout, alignment, and rotation, and
does not treat geometry outside the anchor as a fit failure. `clip` is the only
policy that permits rotated semantic geometry outside the anchor before final
raster clipping. Its final pixels are strictly clipped to the anchor bounds.

All policies apply a final anchor clip as raster safety against anti-alias
fringes, font hinting, rounding, and graphics overdraw. For `ellipsis`,
`shrink`, and `hide`, this safety clip MUST NOT make non-fitting semantic
geometry count as fitting; only the `clip` policy assigns semantic meaning to
cropped output.

Mapping-derived overflow MUST NOT invalidate the Theme, reject or unload the
candidate, activate a fallback Theme, reset mappings or selection, or fail the
associated input/action. An overflow decision affects only that dynamic
element's pixels in the applicable rendered state.

For example, an anchor measuring `100 x 50` and an aligned layout measuring
`80 x 30` fit before rotation. At `rotation: 90`, its rotated bounds are
approximately `30 x 80`, so post-rotation fit fails: `hide` hides it, `shrink`
must continue until the rotated height fits within `50`, and `clip` draws it
then clips it. At `rotation: 0`, pre- and post-rotation bounds are identical.

### 7.2 Styled effect geometry

Styled Semantic Geometry is the union of visible fill geometry, visible
centered-outline geometry, and visible finite shadow-support geometry. For
`ellipsis`, `shrink`, and `hide`, the entire union MUST fit inside the anchor;
checking only text or glyph fill is forbidden. If fill fits but a visible
outline or shadow exceeds the anchor, the styled element does not fit. The
`clip` policy is the sole exception and permits the complete styled geometry to
extend outside the anchor before final clipping.

Fill geometry is the continuous Reference-space text or glyph path produced by
the resolved font or glyph source at the candidate scale. Wrapping and
`maxLines` determine its layout. Runtime aligns the fill layout before
constructing and rotating the styled silhouette.

#### 7.2.1 Outline model

`outlineRole.width` is the full width of a centered stroke in Reference Space.
The stroke extends `width / 2` inward and `width / 2` outward from the fill path
centerline; Runtime MUST NOT reinterpret it as an outside radius, inside-only
stroke, or platform-default stroke placement. A zero width contributes no
outline pixels or extra semantic extent.

A visible outline is constructed around the fill glyph/path geometry after
layout and alignment, and fill plus outline rotate together around the anchor
center. At shrink candidate scale `s`, both the authored fill size and outline
width are multiplied by `s`. Other overflow policies use `s = 1.0`.

Semantic outline bounds have one normative, rectangle-based authority. Let `F`
be the aligned, unrotated continuous fill/path AABB in Reference Space. For a
visible outline of full width `w`, let `r = w / 2` and define:

```text
O = inflate(F, r)
```

If the outline is invisible or `w = 0`, `O = F`. `O` is the normative
pre-rotation fill-plus-outline semantic rectangle. Runtime transforms all four
corners of `O` around the anchor geometric center using the declared clockwise
rotation, then takes their continuous axis-aligned bounding box:

```text
R = AABB(rotateCorners(O, anchorCenter, rotation))
```

`R` is the normative rotated fill-plus-outline semantic bound. Overflow fit,
ellipsis, shrink, and hide MUST use `R`; actual platform stroked-path bounds,
GDI stroker bounds, and final raster pixels are not fit authority. The
rectangle model is deterministic, platform-independent within the LeftPad
contract, DPI-independent, and conservative. Actual raster geometry may occupy
less space, but that MUST NOT turn a semantic NOT FIT result into FIT.

Actual outline rasterization uses a centered stroke with round line joins and
round line caps for text paths, runtime-symbol paths, and any future supported
glyph vector path. Miter, miter-clipped, bevel, platform-default joins, square
caps, and platform-default caps are forbidden. Round joins and caps keep the
ideal centered stroke within the `w / 2` outward radius represented by `O`.
Closed text glyphs use the same round-cap rule even when caps have no visible
effect, so open runtime-symbol contours do not introduce another model. Actual
vector stroke geometry is rasterization input only and MUST NOT redefine `O`,
`R`, or any overflow decision.

An outline whose resolved color alpha is zero is neither drawn nor included in
semantic extent or the shadow-source silhouette, even when width is nonzero.
Every nonzero outline alpha participates fully in semantic extent; low alpha
MUST NOT weaken or remove its fit contribution.

#### 7.2.2 Shadow model

The raster shadow source silhouette is the rotated fill path plus any visible
round-join/round-cap outline. The source is derived after element rotation.
Its semantic base authority is the normative rectangle `R`, never an
implementation-specific stroked-path bound. Runtime then applies
`shadowRole.offsetX` and `offsetY` in final Reference screen axes (`+x` right,
`+y` down), followed by finite blur support. Shadow offset is applied after
rotation and MUST NOT rotate with the element. For example, offset `(4, 4)`
still points screen-right and down when the element rotation is `90` degrees.

`shadowRole.blur` is Gaussian sigma in Reference Space. For `blur > 0`, the
finite kernel is truncated at plus or minus three sigma and
`supportRadius = 3 * blur`. Alpha outside that support MUST be zero. Runtime
may discretely sample the kernel, but MUST derive its radius from three sigma,
not from an infinite tail, a platform-selected extent, or a DPI-specific
cutoff. `blur = 0` is a hard shadow with no blur expansion; it MUST NOT cause
division by zero, an arbitrary one-pixel blur, or shadow removal.

At shrink candidate scale `s`, the authored shadow offsets and blur sigma are
all multiplied by `s`, so the support radius is `3 * blur * s`. At scale `1.0`
the authored values are used unchanged. Semantic offset, sigma, and support are
resolved in Reference Space before Reference-to-Logical-to-Physical scaling;
DPI changes sampling density only.

Let `R` be the normative rotated fill-plus-visible-outline semantic bound,
`offset = (dx, dy)`, and `radius = 3 * blur` after candidate scaling. The shadow
support AABB is:

```text
S.left   = R.left   + dx - radius
S.top    = R.top    + dy - radius
S.right  = R.right  + dx + radius
S.bottom = R.bottom + dy + radius
```

For visible fill or outline, the base fit contribution is `R`. For a visible
shadow, the shadow contribution is `S`. Styled Semantic Bounds are `union(R,
S)` when both base pixels and shadow are visible, `R` when only fill/outline is
visible, and `S` when only shadow is visible. If fill, outline, and shadow are
all invisible, the element has no visible pixels. Runtime MUST derive these
bounds from the normative continuous rectangles, not by scanning final nonzero
Physical pixels.

A shadow whose resolved color alpha is zero is neither drawn nor included in
semantic extent; its offset and blur do not affect fit. Every nonzero shadow
alpha participates fully. A fill color whose alpha is zero contributes no fill
pixels, but its glyph/path geometry remains available as the source for a
visible outline or shadow. Consequently an outline-only or shadow-only dynamic
element is valid. A visible shadow uses the glyph/path silhouette even when the
fill itself is transparent, and includes the visible outline silhouette when
one exists.

#### 7.2.3 Effect pipeline and overflow interaction

The following is the style-effect refinement of the existing dynamic layout
pipeline; it preserves alignment-before-rotation and post-rotation fit:

1. resolve fill geometry and style colors;
2. apply the candidate scale;
3. perform layout, wrapping, and declared `maxLines` generation;
4. align the fill layout and derive its unrotated AABB `F`;
5. derive `O = inflate(F, outlineWidth / 2)` for a visible outline, otherwise `O = F`;
6. rotate the four corners of `O` around the anchor center and derive `R`;
7. construct the round-join/round-cap raster outline and rotated shadow source;
8. apply the shadow offset in Reference screen axes;
9. derive finite three-sigma shadow rectangle `S`;
10. union the applicable visible `R` and `S` contributions;
11. make the overflow fit decision;
12. transform Reference Space to Logical and Physical Space;
13. rasterize the styled element;
14. apply the final anchor safety clip.

Each ellipsis candidate MUST recompute `F`, `O`, `R`, `S`, and Styled Semantic
Bounds without consulting platform stroke bounds. Each shrink candidate MUST
repeat layout and recompute `F`, scaled outline radius, `O`, `R`, scaled shadow
offset and blur, `S`, and the final union; scaling a previously rotated AABB is
forbidden. `hide` uses the same `F -> O -> R -> S` authority at authored scale
`1.0`. Effects do not create additional logical lines; `maxLines` controls only
line generation but effects participate in final styled fit.

For `ellipsis`, `shrink`, and `hide`, any visible effect outside the anchor is a
semantic NOT FIT result. Their final safety clip defends only against
anti-alias fringe, finite-kernel discretization, font hinting, Physical
rounding, and graphics overdraw; it MUST NOT be used to crop an ordinary
outline or shadow into compliance. Under `clip`, fill, outline, and shadow may
all exceed the anchor and the entire styled raster is strictly clipped to the
anchor; Runtime MUST NOT clip only the text while allowing shadow leakage.
`clip` uses the same `F -> O -> R -> S` semantic model but does not require its
Styled Semantic Bounds to be contained by the anchor. For non-clip policies,
round joins and caps ensure the ideal vector outline does not systematically
escape the normative outline authority; only minor raster fringes may be
removed by the safety clip.

Styled effect geometry and fit decisions are DPI-independent. The same Theme,
content, resolved font, and candidate scale MUST produce the same Reference
bounds, ellipsis result, shrink scale, and show/hide decision at every DPI.
Physical rasterization scales Reference-space outline width, shadow offset,
sigma, and support, changing sample density without changing semantic bounds.

The checked-in example uses outline width `2` and shadow blur `4`; at authored
scale its semantic shadow support radius is therefore `12` Reference units.

Styles are semantic roles rather than CSS or platform font names:

- `fontRoles`: abstract `ui`, `display`, `monospace`, or `symbol` roles;
- `colorRoles`: `#RRGGBBAA` colors;
- `outlineRoles` and `shadowRoles`: optional effects referencing color roles;
- `dynamicRoles`: anchor-facing roles referencing font/color and optional
  outline/shadow roles plus a Reference Space size.

Every role reference MUST resolve. Later runtime implementations choose actual
platform font fallbacks without changing the manifest contract.

## 8. Glyph policy

Each glyph role defines all four input families: `keyboard`,
`keyboardShortcut`, `ds4`, and `genericAction`. Every family has a non-empty,
ordered `sources` fallback chain. Source types are:

- `text`, referencing a dynamic style role;
- `themeAsset`, mapping glyph IDs to hashed package assets;
- `runtimeSymbol`, naming a stable runtime symbol set.

The first source capable of representing the requested glyph wins. A source
type may appear at most once per family, so fallback is deterministic. Theme
asset sources require `themeGlyphAssets`. If no source can represent the glyph,
the candidate rendering operation reports a missing-glyph failure; it MUST NOT
reinterpret an unrelated asset or bake user mappings into STATIC/STATE_ASSET
pixels.

## 9. Masks, safe surfaces, occlusion, and regions

Masks are ordinary hashed package assets with unique IDs, canvas-contained
bounds, a purpose (`safeSurface`, `compileClip`, or `debugRegion`), and existing
target layer IDs. Presence requires `maskAssets`.

An anchor `safeSurface` reference MUST resolve to a mask whose purpose is
`safeSurface`. Occlusion and safe-surface asset layers respectively require
`occlusionLayers` and `safeDynamicSurfaces`.

Optional `visualRegions` provide named slot polygons for analysis/debugging.
Points MUST be inside the reference canvas, slot IDs MUST fit the largest
compatible layout, and presence requires `visualRegions`. They do not replace
the existing logical hit-test or selection topology.

## 10. Capabilities

`capabilities.required` and `.optional` are unique, disjoint lists. Required
capabilities are load gates; optional capabilities are advisory and MUST NOT be
necessary for correct baseline rendering. The vocabulary is closed by schema.

Portable validation checks declarations and feature coherence. Runtime-context
validation additionally receives a supported-capability set and rejects any
missing required capability. Phase 0 adds no V2 runtime support; the validator's
context parameter is a contract/test mechanism, not an integration claim.

## 11. Asset path and integrity contract

Every referenced asset path MUST:

- be a non-empty package-relative POSIX path;
- use `/`, never `\`;
- contain no empty, `.` or `..` segment;
- contain no drive, URI scheme, absolute root, home marker, or colon;
- resolve inside the package root when files are checked.

`assetHashes` is an exact set: every referenced asset has exactly one SHA-256
record and no orphan record is permitted. Digests are 64 hexadecimal characters
and comparisons are case-insensitive. Production validation MUST also require
every file and verify content hashes before bundle construction. No asset may be
decoded or composed before manifest, capability, path, reference, and hash-set
validation succeeds.

## 12. Atomic loading and failure semantics

A future runtime loader MUST build and validate a complete candidate off to the
side. It swaps the active bundle only after all required assets, references,
capabilities, and decoded surfaces succeed. On any failure it disposes the
candidate and retains the prior active theme. Startup uses the separately known
safe theme ID. Partial theme visibility and mixed old/new bundles are forbidden.

## 13. Tooling boundary

Phase 0 validation is available as:

```powershell
python validate_theme_v2.py examples/reference-theme-v2.example.json
python validate_theme_v2.py path/to/manifest.json --check-assets
python validate_theme_v2.py path/to/manifest.json `
  --supported-capability fullStateFrame `
  --supported-capability instantTransitions
```

The checked-in example is manifest-only and explicitly marked `exampleOnly`;
it is outside production discovery and intentionally has no large PNG assets.

## 14. Future compiler contract and production route

The high-fidelity production route is:

```text
External Artwork -> Full State / Layered Assets -> Compiler -> Theme Package -> Runtime
```

Pillow/NumPy or other procedural tooling remains an optional authoring backend,
not the sole production pipeline. A future compiler accepts a manifest plus
source assets, then performs Validate -> Stage -> Hash -> Review Artifacts ->
Atomic Publish. It MUST NOT generate or reinterpret artwork. Phase 0 defines
this interface only and implements no compiler or publisher.
