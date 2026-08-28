# Reference Theme Decomposition — Normative Phase 0 Spec

This specification converts a visual reference into V2 package data without
embedding business content, user mappings, or new runtime behavior. It is a
design/export contract, not permission to modify production V1 assets.

## 1. Required analysis output

Before authoring assets, record:

1. primary `layoutProfile` and all genuinely compatible layouts;
2. Reference Space canvas, complete Logical Surface dimensions, explicit
   `contentOrigin`, and `placement.activationAnchor`;
3. selected Runtime `renderStrategy`: `full-state-frame` or `layered-state`;
4. optional authoring provenance: `external-artwork`, `procedural`, or `mixed`;
5. required state matrix (`idle` and every selected slot);
6. ordered layer plan with bounds, visibility, and ownership;
7. dynamic text/glyph anchors, overflow behavior, and safe surfaces;
8. semantic style and glyph roles;
9. masks, occlusion layers, and optional visual regions;
10. exact referenced-asset/hash inventory;
11. required capabilities and deterministic fallbacks.

Do not proceed if topology, ownership, or dynamic-safe regions remain ambiguous.

The coordinate record MUST distinguish the complete Logical Surface from the
contain-scaled artwork. Authors compute `scale` as the smaller logical/reference
dimension ratio and explicitly write the scaled artwork's top-left as
`contentOrigin`; there is no implicit centering. The content rectangle remains
inside the Logical Surface, uncovered letterbox is transparent, and
`activationAnchor` is the Logical Surface point aligned with the physical
`ShowAt(screenPoint)`. The activation anchor controls visual placement only and
does not redefine selection geometry or `LayoutDefinition.WheelCenter`.

## 2. Classification rules

Every visible element MUST be classified once in `elementOwnership`:

- **STATIC**: reference-derived texture, material, border, decoration, or static
  illustration that does not vary with user data.
- **DYNAMIC**: action label, keyboard mapping, shortcut, DS4 mapping, or other
  user/session-dependent content. It requires a `contentKey`.
- **STATE_ASSET**: package-authored pixels selected through the state-to-layer
  asset map.

If an authored image contains DYNAMIC content, the image MUST be decomposed so
the dynamic content is removed and replaced by an anchor. Baking keyboard keys,
DS4 buttons, action labels, or user mappings into STATIC or STATE_ASSET pixels
is invalid.

## 3. Strategy selection

Use `full-state-frame` when each state is best represented by a complete authored
frame and memory/export cost is acceptable. A full-state-frame package MUST
target exactly one compatible layout; author a separate package for a different
layout. Use `layered-state` when shared art, slot highlights, foreground
occlusion, or runtime surfaces must compose independently. A layered package MAY
list multiple layouts only when one asset/anchor/state definition is valid for
all of them; the MVP has no per-layout asset variants.

Procedural generation is an offline authoring method, never a Runtime render
strategy. A Pillow/NumPy-produced theme may record `authoring.method` as
`procedural`, but the compiled package still selects `full-state-frame` or
`layered-state`. The metadata is provenance only and does not alter capabilities,
selection, state lookup, DPI, fallback, or rendering.

Strategy selection MUST NOT change logical slot ordering, hit testing, actions,
mappings, or DPI semantics. A radial-8 reference uses `radial-8`; it is not
forced into radial-6 merely to reuse assets.

Authors MUST export standard sRGB straight-alpha PNGs and MUST NOT manually
premultiply PNG pixels. Runtime cache preparation owns conversion to PArgb before
high-quality resampling to the final Physical target. Review MUST specifically
inspect translucent edges, glows, shadows, and fully transparent colored pixels
for fringe or RGB-bleed risk.

## 4. State and layer matrix

The decomposition record MUST enumerate each required state against every
required `stateAsset` layer. Empty cells are errors. Shared pixels use
`staticAsset`; runtime text/glyph surfaces use their dynamic layer kinds;
foreground cover art uses `occlusionAsset` when it must render above dynamic
content.

Layers are ordered by `zIndex`, then declaration order. Bounds MUST be expressed
in Reference Space and contained by the reference canvas. `visibleStates` uses
only exact declared states or the selectors `all`, `selected`, and `pressed`.

## 5. Dynamic-safe composition

For every DYNAMIC element:

- allocate a matching text or glyph layer;
- create a bound anchor with explicit bounds, alignment, overflow, minimum scale,
  rotation, visibility, and style role;
- add a glyph role for glyph content;
- add a safe-surface mask when the reference contains irregular usable space;
- add an occlusion layer when reference art must cover part of dynamic content.

Anchors MUST fit the reference canvas. Safe surfaces and occlusion are authored
composition data; neither may redefine logical input geometry.

Dynamic rotation is clockwise for positive degrees and always uses the
geometric center of the anchor bounds. Authors MUST reserve enough anchor area
for content after alignment and rotation: `ellipsis`, `shrink`, and `hide`
judge fit from the final rotated continuous geometry in Reference Space, not
from the unrotated layout box or Physical pixels. Rotation never causes Runtime
to realign, nudge, or recenter the content inside the anchor.

Choose overflow behavior with extreme Runtime mappings in mind. `shrink` may
make the entire element disappear when content still cannot fit at
`minimumScale`; it never falls back to ellipsis or visible clipping. Use
`ellipsis` when a fitting truncated representation must be attempted. Choose
`clip` only when cropped output, including cropped rotated output, is visually
acceptable. `hide` is appropriate when authored-size content should be shown
whole or not at all. If an element must preserve partial visible content,
authors MUST select `ellipsis` or `clip`, not `shrink`.

Reserve anchor space for the complete styled element, not only its fill. A
visible outline is a centered stroke whose outward extent reduces the available
fit area. A visible shadow's screen-axis offset and finite three-sigma blur
support must also remain inside the anchor for `ellipsis`, `shrink`, and
`hide`. Large outlines, offsets, or blur values therefore make shrink or hide
more likely, especially for rotated labels.

Authors MUST leave enough room around rotated text and glyphs for both outline
and shadow support. `clip` is the only policy under which cropping these effects
is intentional visual behavior; the final safety clip is not an authoring
fallback for the other policies. A fully transparent outline or shadow does not
affect fit, while any nonzero effect alpha contributes its complete semantic
extent. Transparent fill may still produce a visible outline-only or
shadow-only element, so it does not by itself eliminate the underlying glyph
geometry.

Outline space MUST be authored against the conservative semantic rectangle:
take the aligned fill/path AABB, inflate every side by `outline.width / 2`, then
rotate that rectangle around the anchor center and bound its four transformed
corners. Do not assume that empty corners in a particular font's actual stroke
or platform rasterizer will let the element fit a smaller anchor; actual
stroked-path bounds never replace the contract rectangle for overflow
decisions. Raster outlines use round joins and round caps.

## 6. Style and glyph extraction

Extract style roles by purpose, not by a specific machine font name. Define
abstract font roles, `#RRGGBBAA` color roles, optional outline/shadow roles, and
anchor-facing dynamic roles. Every reference must resolve.

Every glyph role MUST define `keyboard`, `keyboardShortcut`, `ds4`, and
`genericAction`. For each family, order `text`, `themeAsset`, and/or
`runtimeSymbol` sources as the intended deterministic fallback chain. Include
only theme assets the package actually owns and hash every included asset.

## 7. Masks and visual regions

Masks MUST have a single declared purpose and explicit target layers. A
`safeSurface` anchor reference may only target a safe-surface mask. Visual region
polygons are optional analysis/debug metadata; they remain inside the canvas and
do not become runtime hit-test definitions.

## 8. Export acceptance checklist

A decomposition is ready for later compiler/runtime phases only when:

- required state coverage is complete for the largest compatible layout;
- layer, anchor, mask, region, and ownership IDs are unique;
- all references resolve and all bounds are inside the canvas;
- STATIC/DYNAMIC/STATE_ASSET ownership has no conflict;
- required capabilities match every used feature;
- rotated dynamic content has sufficient anchor area for its selected overflow
  policy, and intentional cropped output uses `clip`;
- visible outline and finite shadow support fit the anchor after rotation, or
  the selected policy intentionally handles the resulting NOT FIT condition;
- package paths are safe and the hash set exactly equals referenced assets;
- the V2 validator passes in both portable mode and the intended runtime
  capability context;
- V1 assets and runtime behavior have not changed.

The final source of truth for JSON field shape is
`schemas/ui-theme-v2.schema.json`; cross-field acceptance is implemented by
`validate_theme_v2.py` and defined by `THEME_RUNTIME_PROTOCOL_V2.md`.

## 9. Validator limitation and approval boundary

Schema and semantic validation detect declarations, references, bounds,
ownership conflicts, and asset integrity. They cannot determine whether an
artist secretly baked an undeclared glyph or dynamic label into PNG pixels.
Production approval therefore still requires this decomposition record,
forbidden-dynamic-region notes when applicable, a review sheet, and human visual
review. Automated validation MUST NOT be represented as pixel-semantic analysis.
