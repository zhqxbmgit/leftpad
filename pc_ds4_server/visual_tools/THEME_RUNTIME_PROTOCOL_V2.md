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

Overflow is explicit: `ellipsis`, `shrink`, `clip`, or `hide`. `maxLines`
constrains the authored line box. Runtime MUST apply the declared policy and
MUST NOT invent an unbounded resize.

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
