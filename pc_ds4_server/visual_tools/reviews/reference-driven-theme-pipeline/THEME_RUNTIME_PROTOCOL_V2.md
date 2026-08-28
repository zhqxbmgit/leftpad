# THEME RUNTIME PROTOCOL V2

Status: architecture proposal; not implemented.

Protocol identity: **LeftPad UI Theme Package V2**.

Initial surface adapter: `radial-overlay`.

## 1. Purpose

V2 defines an asset-first boundary between authored UI art and the stable runtime. It permits a reference image to be decomposed into complete state frames, shared layers, and dynamic content anchors without requiring the runtime to reproduce the art through procedural geometry.

The protocol is intentionally narrower than HTML/CSS and broader than the current radial V1 pack. It provides states, ordered alpha layers, constrained dynamic text/glyph roles, integrity, compatibility, and fallback. It is not a general game engine or document-layout system.

## 2. Normative principles

The words MUST, MUST NOT, SHOULD, and MAY describe proposed normative behavior.

1. Logical input selection MUST remain outside the theme package.
2. A theme MUST NOT define hit testing, slot angles, dead-zone thresholds, actions, mappings, confirm/cancel behavior, or input timing.
3. Bitmap alpha, masks, regions, and anchors MUST NOT influence selection.
4. The radial adapter MUST map logical slot `0` to `idle` and slot `N` to `selected-N`.
5. Runtime MUST use pre-authored/precompiled art and MUST NOT generate theme-specific materials on the selection hot path.
6. Every named visual element MUST have exactly one declared owner: `STATIC`, `DYNAMIC`, or `STATE_ASSET`.
7. Required state assets MUST be decoded and cached before theme activation.
8. Theme activation MUST be transactional. Failure MUST retain the previous valid session when one exists.
9. V1 packages MUST continue to run unchanged through a compatibility adapter.
10. MVP transitions MUST be direct and MUST NOT delay logical selection, confirm, or cancel.

## 3. Terms

| Term | Meaning |
| --- | --- |
| Package | A directory containing `theme.json` and all referenced local assets |
| Surface adapter | Runtime integration for one UI surface, initially `radial-overlay` |
| Layout Profile | Stable logical topology such as `radial-6` or `radial-8` |
| Reference Space | Authored pixel coordinate system declared by `canvas` |
| Logical Space | DPI-independent runtime size |
| Physical Space | Final monitor pixel backing used for presentation |
| State | Visual key such as `idle` or `selected-3` |
| Layer definition | Ordered composition role and kind |
| State binding | Asset assigned to a state-aware layer for one state |
| Dynamic anchor | Bounded location for a supported runtime content key |
| Visual Region | Optional author/debug semantic region; never hit-test data |
| Render bundle | Immutable, fully validated physical-space caches for one package/layout/DPI/content snapshot |

## 4. Package layout

Recommended production layout:

```text
<theme-id>/
  theme.json
  states/
    idle.png
    selected-1.png
    ...
  layers/
    decoration.png
    dynamic-underlay.png
    foreground-occluder.png
  glyphs/
    ds4-cross.png
    ...
  build-report.json          optional
```

Paths in `theme.json` MUST be normalized forward-slash relative paths within the package. Absolute paths, URI schemes, empty segments, `.` segments, `..` traversal, and resolved paths outside the package MUST be rejected. Case-colliding paths SHOULD be rejected so a package behaves consistently on case-sensitive and case-insensitive filesystems.

## 5. Coordinate and DPI contract

### 5.1 Reference Space

`canvas` defines source dimensions and pixel format. All frames bound to a full-canvas layer MUST exactly match it. Layer assets either MUST match the canvas or MUST declare a validated destination rectangle in a future protocol extension; the V2 MVP SHOULD require full-canvas RGBA assets to keep composition deterministic.

All anchor rectangles, semantic-region polygons, and compiler masks use Reference Space.

### 5.2 Logical Space

`referenceScale.logicalWidth` and `logicalHeight` declare the package's size at application scale 100% and DPI 96. The existing radial `ScalePercent` multiplies these values uniformly. The V2 radial MVP SHOULD require 280×280 logical size to preserve existing overlay scale semantics.

`referenceScale.anchor` is a Reference-Space point mapped to the input session's physical screen anchor. It replaces the hidden assumption that the visual origin is always the geometric canvas midpoint, while leaving logical radial angles and cursor deltas untouched.

### 5.3 Physical Space

For each dimension:

```text
physical = round(logical × existing ScalePercent / 100 × effectiveDpi / 96)
```

The implementation MUST use existing rounding/DPI semantics. Source assets MUST scale directly from Reference Space into final Physical Space. Reference -> low-resolution logical bitmap -> physical upscale is prohibited.

### 5.4 Color and alpha

- V2 MVP `canvas.mode` MUST be `RGBA`.
- `canvas.colorSpace` MUST be `sRGB` for V2 MVP.
- Source PNGs use straight alpha on disk unless the image decoder specifies otherwise.
- Physical render caches MUST use the runtime's 32-bpp premultiplied-alpha representation (`Format32bppPArgb` in the current implementation).
- RGB residue in fully transparent pixels SHOULD be cleaned by the compiler to avoid resampling fringes.

## 6. Manifest contract

The proposed production filename is `theme.json`. `EXAMPLE_THEME_MANIFEST.json` demonstrates the complete shape.

### 6.1 Required top-level fields

| Field | Type | Constraint / meaning |
| --- | --- | --- |
| `id` | string | Stable non-empty package ID; ordinal comparison |
| `displayName` | string | Human-readable name |
| `version` | integer | Exactly `2` |
| `packageRevision` | string | Authoring revision such as SemVer; changes when package content changes |
| `surface` | string | V2 MVP: `radial-overlay` |
| `layoutProfile` | string | Primary logical profile used for settings/mapping display |
| `compatibleLayouts` | string array | Non-empty; includes `layoutProfile`; V2 MVP accepts registered radial profiles only |
| `renderStrategy` | enum | `full-state-frame`, `layered-state`, or `procedural` |
| `canvas` | object | Reference canvas contract |
| `referenceScale` | object | Logical size and presentation anchor |
| `states` | object | State declarations and asset bindings |
| `layers` | array | Ordered declarative layer definitions |
| `dynamicAnchors` | array | May be empty; constrained runtime content placement |
| `styles` | object | Color/typography/outline/shadow roles |
| `glyphs` | object | Input-family glyph policies |
| `fallback` | object | Failure and missing-content policy |
| `assetHashes` | object | SHA-256 for every runtime-consumed asset |
| `capabilities` | object | Explicit feature usage/negotiation |
| `transitions` | object | V2 MVP requires `mode: direct` |

Optional top-level fields are `visualRegions`, `elementOwnership`, `masks`, and `metadata`. Unknown top-level fields MUST be rejected unless prefixed by an approved extension namespace; silent typo acceptance is unsafe in a production manifest.

### 6.2 `canvas`

```json
{
  "width": 1254,
  "height": 1254,
  "mode": "RGBA",
  "colorSpace": "sRGB",
  "alphaMode": "straight-on-disk"
}
```

`width` and `height` MUST be positive integers within compiler/runtime limits. V2 as a package model permits rectangular canvases; the initial radial adapter MAY require a square canvas until anchor-aware rectangular window placement is implemented. That adapter limitation is a capability check, not a reason to encode circular art assumptions in the schema.

### 6.3 `referenceScale`

```json
{
  "logicalWidth": 280,
  "logicalHeight": 280,
  "fit": "contain",
  "anchor": { "x": 627, "y": 627 }
}
```

V2 MVP supports `fit: contain` with uniform scaling. The anchor MUST be finite and inside/on the Reference canvas. Artwork MAY be asymmetric around it.

### 6.4 `states`

`states` is an object keyed by stable state name. Each value contains:

| Field | Required | Meaning |
| --- | --- | --- |
| `slotId` | yes for radial MVP | `0` for idle or logical slot ID |
| `assets` | yes | map of state-aware layer ID -> local PNG path |
| `visibleLayers` | no | explicit additional visibility selection; default derives from layer kind/state binding |
| `metadata` | no | author/review metadata ignored by composer |

For each compatible radial layout with N slots, required keys are `idle`, `selected-1`, ..., `selected-N`. State names and `slotId` MUST agree. Duplicate slot IDs are invalid.

Full-frame production packages MUST bind the required full-art layer in every required state. Layered packages MUST supply all state bindings marked required by their layer definitions. Optional animation/pressed states are outside the V2 MVP validator and SHOULD be rejected until a minor protocol revision defines their semantics.

### 6.5 `layers`

Each layer definition contains:

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | string | Unique stable layer ID |
| `z` | integer | Composition order, ascending |
| `kind` | enum | `staticAsset`, `stateAsset`, `dynamicGlyph`, `dynamicText`, `runtimeDebug` |
| `owner` | enum | `STATIC`, `STATE_ASSET`, or `DYNAMIC`; must agree with kind |
| `composite` | enum | V2 MVP: `sourceOver`; Z0 MAY use `sourceCopy` from transparent clear |
| `required` | boolean | Whether every applicable state/package must bind/provide it |
| `asset` | string | Required only for `staticAsset` |
| `anchorGroup` | string | Required for dynamic kinds; selects anchors by group |
| `enabled` | boolean | `runtimeDebug` MUST be false in production |

Valid owner/kind pairs:

| Kind | Owner |
| --- | --- |
| `staticAsset` | `STATIC` |
| `stateAsset` | `STATE_ASSET` |
| `dynamicGlyph`, `dynamicText`, `runtimeDebug` | `DYNAMIC` |

Recommended Z bands are 0 full/base, 10 decoration, 20 state overlay, 25 safe underlay, 30 glyph, 40 text, 45 foreground occluder, and 50 debug. Z is authoritative; names are descriptive only.

The V2 MVP compositor MUST NOT accept arbitrary blend modes, shader programs, filters, blur radii, CSS, script, or runtime expressions.

## 7. Render strategies and hybrid behavior

### 7.1 Full State Frame

The package has one required `stateAsset` artwork layer, conventionally at Z0. Each required state binds a complete final frame.

Use it when selection changes global lighting, geometry, characters, paths, shadows, or composition. Runtime state switching is a cache lookup.

### 7.2 Layered State

Shared art is supplied by `staticAsset` layers and selected states bind one or more `stateAsset` layers. Use it only when the shared pixels are intentionally identical and layering does not create seams.

The compiler SHOULD compare declared shared/static regions across authored states and report drift.

### 7.3 Procedural

`procedural` identifies the dominant authoring backend. For V2 MVP, the approved route is offline procedure -> compiled RGBA/state/layer assets -> ordinary V2 runtime package. A future tightly bounded runtime primitive set would require separate protocol capability design.

### 7.4 Hybrid

All strategies MAY include dynamic glyph/text layers and static decorations/underlays/occluders. `renderStrategy` selects validation defaults and review layout; it does not make other declared layer kinds illegal.

## 8. Logical slot to visual semantic mapping

The logical mapping is fixed:

```text
0 -> idle
1 -> selected-1
...
N -> selected-N
```

An optional `visualRegions` record contains:

```json
{
  "id": "north-tower",
  "slotId": 1,
  "label": "Northern tower",
  "polygon": [[552, 120], [708, 120], [760, 430], [510, 430]]
}
```

Rules:

- `id` and `slotId` are unique.
- Polygon points MUST be finite and within the Reference canvas.
- Regions MAY overlap and MAY be noncongruent.
- Regions are author/debug metadata. Runtime selection MUST NOT test the polygon or bitmap content.

## 9. Dynamic Anchor System

### 9.1 Supported content keys

The radial adapter initially exposes only stable keys:

- `slot.<N>.inputGlyph`;
- `slot.<N>.actionLabel`;
- `slot.<N>.mappingText`;
- `center.prompt`;
- `theme.stateText` (optional product-approved state projection).

A manifest MUST NOT introduce executable expressions or arbitrary object paths. Unsupported content keys invalidate the package or are rejected as an unsupported capability before activation.

### 9.2 Anchor fields

| Field | Type | Constraint |
| --- | --- | --- |
| `id` | string | unique |
| `group` | string | matches dynamic layer `anchorGroup` |
| `content` | string | supported adapter key |
| `slotId` | integer/null | must match content key where applicable |
| `x`, `y`, `width`, `height` | number | finite; rectangle within canvas |
| `horizontalAlign` | enum | `left`, `center`, `right` |
| `verticalAlign` | enum | `top`, `middle`, `bottom` |
| `typographyRole` | string/null | required for text |
| `glyphRole` | string/null | required for glyph |
| `maxLines` | integer | 1..compiler limit |
| `overflow` | enum | `ellipsis`, `shrink`, `clip`, `hide` |
| `minScale` | number/null | required for `shrink`, range (0,1] |
| `rotationDegrees` | number | finite; theme-declared rotation |
| `visibleIn` | string array | state selectors: `idle`, `selected`, exact state, or `all` |
| `safeSurface` | string/null | optional mask/safe-surface ID |

Text layout MUST be deterministic for the resolved font role and physical target. Shrinking occurs at cache-build time, not per selection. Themes SHOULD define generous safe regions for localization and long shortcuts.

### 9.3 Style roles

`styles` contains four constrained maps:

- `colors`: named sRGB RGBA colors;
- `typography`: font-family preference list, reference-space size, weight, line height, color role, optional outline/shadow role;
- `outlines`: color role and finite width;
- `shadows`: color role and finite offset/blur/opacity within implementation limits.

No arbitrary gradients, margins, selectors, inheritance, animation, filters, or scripts are supported for dynamic content. Static artwork owns complex visual material.

The runtime MAY substitute an installed fallback font. The resolved font identity MUST participate in the dynamic cache key and review report.

## 10. Glyph and icon system

`glyphs` is a map of glyph-role ID to input-family policies. Anchors refer to the role ID through `glyphRole`. It declares rendering policy, not user mappings. Example:

```json
{
  "mapping-input": {
    "keyboard": {
      "mode": "text-keycap",
      "typographyRole": "keycap",
      "fallback": "plain-text"
    },
    "ds4": {
      "mode": "asset-map",
      "assets": { "cross": "glyphs/ds4-cross.png" },
      "fallback": "runtime-symbol"
    },
    "generic": {
      "mode": "runtime-symbol",
      "symbolSet": "leftpad-basic",
      "fallback": "plain-text"
    }
  }
}
```

Rules:

1. Keyboard shortcuts are formatted by existing action/mapping logic and drawn as text/keycaps.
2. DS4 IDs continue to come from the existing action catalog. The theme may skin known icons with pre-rendered assets.
3. A package does not contain one full-state frame per user mapping.
4. Missing optional glyph art uses the declared fallback. It does not invalidate logical action execution.
5. Raster glyph assets MUST be validated, hashed, decoded, and cached at activation.
6. Runtime-symbol sets are closed/versioned sets implemented by the runtime, not arbitrary vector path data from the manifest in V2 MVP.

## 11. Baked Art Ownership Contract

`elementOwnership` is a list of semantic element records:

```json
{
  "element": "slot.actionLabel",
  "owner": "DYNAMIC",
  "layer": "slot-labels",
  "content": "slot.*.actionLabel"
}
```

Compiler checks:

- every `element` ID appears once;
- owner agrees with referenced layer kind;
- DYNAMIC entries identify a supported content/anchor group;
- STATIC entries reference static layers only;
- STATE_ASSET entries reference state-aware layers;
- the same content key/pattern is not also declared STATIC/STATE_ASSET;
- required ownership categories from the decomposition spec are present.

Limits: a compiler cannot generally detect that arbitrary pixels visually depict a close glyph, label, or icon. Optional forbidden-baked-region masks, optical character/icon checks, and difference images MAY emit warnings, but human review is the semantic authority.

## 12. Masks, underlays, and occlusion

The preferred model is precompiled ordinary alpha:

1. `masks` may name compiler input/output alpha assets and safe surfaces.
2. The compiler applies complex clipping to state/static assets before publication where possible.
3. An opaque or alpha **safe underlay** is a normal static layer, commonly Z25.
4. Dynamic icons/text are normal cached layers at Z30/Z40.
5. A **foreground occluder** is a normal static layer at Z45.
6. Runtime composition uses `sourceOver`; no shader or stencil subsystem is required.

Mask definitions include `id`, `asset`, `purpose` (`safeSurface`, `compileClip`, `debugRegion`), and `appliesTo`. A production runtime MAY ignore `compileClip` mask files after the compiler bakes their effect, but all runtime-consumed assets remain hashed.

Safe-surface coverage is mechanically checkable: an anchor rectangle that declares a safe surface MUST be contained by/appropriately covered by that surface mask according to compiler rules. Visual contrast still requires review.

## 13. Transitions and animation

V2 MVP:

```json
{ "mode": "direct" }
```

The renderer swaps cached states immediately. Future crossfade or frame-sequence support requires a protocol revision with memory and cancellation behavior. Regardless of visual transition:

- logical selected slot changes immediately;
- confirm/cancel executes immediately;
- no animation waits on business logic;
- a new state may interrupt an in-progress visual transition;
- direct switching remains the fallback.

## 14. Integrity and asset hashes

`assetHashes` maps every runtime-consumed relative asset path to an uppercase/lowercase-insensitive 64-hex SHA-256 string. The compiler MUST emit exactly one hash per referenced asset. Unreferenced hash entries SHOULD be rejected or warned as package debris.

Runtime validation order SHOULD be:

1. path/schema/capability checks;
2. file presence and file-type limits;
3. SHA-256 verification;
4. decode and image-contract checks;
5. cache construction.

V1 packages retain their existing optional `verification.json` behavior through the V1 adapter; V2 hashes are required.

## 15. Capabilities and feature negotiation

`capabilities` states what the package actually requires, for example:

```json
{
  "fullStateFrame": true,
  "layeredState": true,
  "proceduralRuntime": false,
  "dynamicText": true,
  "dynamicGlyph": true,
  "visualRegions": true,
  "masks": true,
  "directTransitionsOnly": true
}
```

The loader MUST reject a package requiring an unsupported capability before it mutates the active session. Capabilities are not permissions to supply script or code.

## 16. Validation and fallback behavior

### 16.1 Compiler/validator errors

The following are fatal for a production package:

- invalid/duplicate JSON keys or unknown required fields;
- invalid ID/version/surface/layout compatibility;
- missing `idle` or any `selected-N` state;
- missing required state-layer binding;
- duplicate state slot ID;
- asset path traversal, missing asset, hash mismatch, decode failure;
- dimension/mode/alpha contract failure;
- unsupported layer kind/composite/capability/transition;
- out-of-bounds anchor/region or missing style/glyph reference;
- duplicate/incompatible declared ownership;
- estimated decoded memory above the configured hard limit.

Warnings include duplicate Z order without a declared deterministic need, overly tight text safe regions, missing optional semantic regions, suspicious unreferenced files, high-but-allowed memory, and automatic visual-diff anomalies.

### 16.2 Runtime fallback

| Failure | Behavior |
| --- | --- |
| Candidate invalid/unsupported | retain active theme; log candidate reason |
| Candidate selected state missing/corrupt during load | reject entire candidate transaction |
| Candidate decode/cache build fails | dispose candidate only; retain active |
| Startup has no active theme | load V1 `radial-v5` fallback |
| Defensive unexpected state lookup | render cached idle art; keep logical slot/action unchanged; log |
| Optional glyph missing | use declared runtime symbol/text fallback |
| Default pack also fails | hide overlay safely; input/action pipeline continues |

The manifest may declare the policy names, but it cannot redirect fallback to arbitrary external paths. Runtime policy remains authoritative.

## 17. Loading transaction and concurrency

The candidate session is isolated from the active session. A complete load is:

```text
manifest parse
-> normalized render plan
-> integrity verification
-> sequential asset decode/scale
-> required state/layer cache construction
-> dynamic content cache construction
-> bundle invariant verification
-> UI-thread atomic active reference swap
-> previous session disposal
```

The active session remains usable until the swap. Candidate exceptions MUST NOT partially modify active caches. DPI rebuilds SHOULD use the same candidate-bundle pattern within the active theme.

Theme switching MUST NOT reset menu open source, selected logical slot, mappings, receiver/service state, or persisted business settings other than the separately authorized visual theme choice.

## 18. Performance contract

At theme activation, all required state art and static/glyph assets are decoded and scaled to the current physical target. Source masters SHOULD be processed sequentially and released. Dynamic content is rendered once per cache key.

The selection hot path permits:

- state-key lookup;
- bitmap/layer reference lookup;
- ordinary alpha composition into a reused/scratch target, or lookup of a precomposed final-state cache;
- `UpdateLayeredWindow`.

It prohibits:

- filesystem access and hashing;
- PNG decode;
- master resize/downsample;
- Gaussian blur/material generation;
- font discovery;
- schema/layout parsing;
- rebuilding labels or glyphs when mappings have not changed.

Compiler reports MUST include physical-cache estimates for radial-6 and radial-8 at 100%, 150%, and 200% DPI, plus estimated peak during atomic switching.

## 19. Legacy V1 adapter

Version dispatch uses the manifest's integer `version`:

- V1: current `manifest.json` parser, exact existing asset rules, normalized internally to a compatibility render plan.
- V2: `theme.json` and this protocol.

Discovery MAY allow both filenames but MUST reject a directory containing ambiguous conflicting V1/V2 identities. Duplicate IDs across package directories remain invalid. The V1 adapter MUST preserve current pixels, layout profile, selected rotation, cache behavior, and fallback.

No V1 package needs a new manifest, hash file, renamed asset, or regeneration.

## 20. Shared use beyond radial

The reusable pieces are package identity/versioning, reference/canvas spaces, state assets, layer ordering, dynamic anchors/styles/glyph policies, ownership, hashes, compiler, and review artifacts.

Future surfaces such as controller visualization or popup selector SHOULD add a surface adapter that defines:

- allowed logical state keys/content keys;
- positioning and logical-size rules;
- interaction authority outside the theme;
- allowed layer/dynamic capabilities.

They SHOULD NOT add radial slot angles to the generic package core, and V2 SHOULD NOT grow into a DOM, CSS clone, or arbitrary scripting host.

## 21. Acceptance gates for V2 implementation

1. V1 production packs load and render unchanged.
2. Logical radial-6/radial-8 selection/action tests remain unchanged.
3. A full-frame reference theme switches all required states without disk/decode/build work on selection.
4. A layered radically different theme uses the same runtime kinds without theme-specific branches.
5. Theme and DPI rebuilds are atomic.
6. Missing/corrupt/unsupported themes retain the last valid session or fall back to V1 `radial-v5` on startup.
7. Dynamic content rebuild counts change only on theme/mapping/DPI/style/font changes.
8. Runtime captures at 100/150/200% are pixel-aligned with their physical cache and pass human fidelity review.
9. No input, mapping, service, persistence, confirm/cancel, MOVE, or DS4 behavior is modified by theme code.
