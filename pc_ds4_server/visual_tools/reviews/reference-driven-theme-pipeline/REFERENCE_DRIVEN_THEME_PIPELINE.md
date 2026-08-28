# REFERENCE-DRIVEN THEME PIPELINE DESIGN

## A. Baseline

### Repository baseline

| Item | Audited value |
| --- | --- |
| Project | `C:\leftpad` |
| Branch | `feature/dreamscape-hidden-page-suspension` |
| HEAD | `e5ac2718b841f051772f010416af444fae1c95f2` |
| Commit | `Suspend hidden Dreamscape page polling` |
| Stable test baseline supplied for this review | `1012 passed, 0 failed, 0 skipped` |
| Review scope | Local source/document audit and architecture design only |

The stable test baseline was not rerun because this turn is design-only and does not change production code or tests. The repository already contained protected untracked assets and review directories; they are inputs/parallel work and are not modified by this review.

### Current runtime

The production radial overlay is a WinForms layered window rendered through GDI+ and presented by `UpdateLayeredWindow`. Its current composition is fixed to:

```text
Scaled Base (SourceCopy)
  + rotated Selected overlay for Slot N (SourceOver)
  + one cached Dynamic Content bitmap (SourceOver)
  -> 32-bpp PArgb composition
  -> UpdateLayeredWindow
```

Logical selection is already independent of bitmap alpha. `RadialSelectionEngine` receives a `LayoutDefinition`, cursor delta, and physical dead-zone radius, then selects the nearest registered slot angle. Slot `0` is the dead-zone/idle result. A visual gap, transparent pixel, card boundary, or semantic region does not participate in hit testing.

### Audit of the three existing visual systems

| Responsibility | Radial Runtime / Visual Pack | `visual_tools` | Dreamscape |
| --- | --- | --- | --- |
| Visual appearance | `radial-base*.png`, one canonical selected PNG, hard-coded dynamic text styling | Designer/AI-exported assets; historical frozen Pillow generator for V5 materials | Full-page `static-art.png`, HTML/CSS controls, SVG/DOM decoration, shared shell |
| Geometry | Runtime requires a 1254×1254 canvas, center 627,627, registered slot angles and per-slot anchors | Validator hard-codes profile geometry; Layout Profile documents detailed geometry | Fixed 1672×941 reference canvas; absolute CSS coordinates; shared shell metrics |
| Interaction | `RadialSelectionEngine`, controller, trigger recognizers, action resolution/execution | None | Native C# business state and allow-listed command handlers; DOM only projects/requests state |
| Dynamic text | `RadialDynamicContentCache`; currently one fitted primary mapping string per slot at `GlyphAnchor` | Verifies anchors but does not compile a style/anchor system | DOM text/select/output elements populated from C# WebView2 messages |
| State switching | Base plus a rotated selected overlay selected by slot ID | Comparison assumes Slot 2 rotation and all-slot rotations | JS applies state payloads and page activation; CSS data attributes/classes change visuals |
| DPI / scaling | Logical canvas -> monitor physical pixels; masters scale directly to target cache; `OnDpiChanged` rebuilds | 1254 master validation and offline 4× authoring guidance | Shared shell uses contain-fit transform for 1672×941; WebView2/host metrics and runtime captures verify scale |

### Current contract details that matter to V2

- `LayoutProfileRegistry` currently registers both `radial-6` and `radial-8` as runtime-session supported.
- Production contains `radial-v5` and `radial-8-minimal-v1`; integration tests exercise eight-slot selection, actions, persistence, dynamic content, and atomic theme switching.
- The offline toolchain documentation and Python `PROFILE_CONTRACTS` still label `radial-8` experimental/not integrated. This is a contract-documentation drift, not a runtime limitation at this HEAD, and must be corrected in Phase 0 before V2 validation reuses those flags.
- `generate_visual_pack.py` is a transactional packager, not an art generator. It copies already-authored assets byte-for-byte, creates the V1 manifest and hash report, runs validation, and optionally creates a review sheet.
- The historical V5 `build_radial_raster_v5.py` is the procedural authoring backend. It uses masks, polygons, gradients, Gaussian blur, NumPy/Pillow compositing, 4× supersampling, and downsampling to create one very specific radial visual family.
- Current C# layout parsing uses canvas, wheel center, slot count/angles, glyph/label anchors, and optional center text anchor. Much of the richer `geometry` metadata is validated offline but is not a runtime drawing authority.

## B. Why Current Pipeline Falls Short

The failure is an expression-model mismatch, not simply insufficient polish.

### Hard limitations

1. **Manifest V1 exposes exactly one Base asset and one Selected asset.** It cannot declare idle/full selected frames, independent per-slot state art, multiple layer roles, masks, occluders, or dynamic style roles.
2. **`canonical-transform` is the only accepted selection asset mode.** One Slot 1 selected image is rotated around the wheel center for every slot. This requires transform-congruent selection art.
3. **Every accepted asset is forced to 1254×1254 RGBA with center 627,627.** Both Python validation and C# loading enforce it; the Python validator additionally rejects art that reaches the canvas border.
4. **The state compositor has only three fixed positions.** Base, selected overlay, and dynamic content cannot express foreground occlusion, safe underlays, decorations, separate glyph/text layers, or a full-state replacement.
5. **Selected state is additive/local by construction.** It cannot replace the whole scene, alter global lighting, move a character, change a building, reroute a path, or intentionally change a non-target region.
6. **Slot-to-art mapping is implicit rotation.** There is no explicit Logical Slot ID -> Visual Semantic Region contract for a tower, bridge, staircase, asymmetric card, or other non-radial object.
7. **Dynamic styling is hard-coded in C#.** Font family selection, sizing heuristics, color, bounds, single-line ellipsis, and bold style are renderer decisions rather than theme declarations.
8. **The current glyph model is actually a text model.** DS4 actions, keyboard keys, shortcuts, and unset state all become a single string. There is no icon catalog or per-input-family render policy.
9. **Production validity requires complete rotational symmetry.** Uneven visual positions, distinct slot silhouettes, irregular occlusion, and noncongruent per-slot art cannot be represented by the runtime contract.
10. **Manifest version handling accepts only V1.** A new asset model cannot be introduced without a versioned loader/adapter boundary.

### Soft limitations

1. The layered-window/GDI+ host can already alpha-composite arbitrary premultiplied RGBA images. The host technology is not intrinsically radial; its current data model is.
2. Non-circular and asymmetric art can fit inside the existing square transparent canvas, but V1 still forces its selected effect to be a rotated overlay and forces every state to share the Base composition.
3. Global selected-state changes are possible if supplied as full precomposed frames; the current loader simply has no way to declare or cache them.
4. Per-slot anchors already exist, so uneven dynamic text placement is feasible. The current cache ignores `LabelAnchor` and draws only one primary string at `GlyphAnchor`.
5. The current runtime builds replacement sessions before a theme install and atomically swaps sessions. That good pattern can be generalized, although DPI/dynamic refresh should become a single immutable render-bundle transaction.
6. GDI+ can draw text and small runtime symbols, but an unrestricted runtime material/effect engine would undermine latency and visual reproducibility. Dynamic rendering should stay deliberately small.
7. A square radial surface does not require circular-looking art. The first V2 radial adapter can preserve square logical bounds while allowing an arbitrary composition inside them; rectangular/general surfaces can follow through a shared package model plus surface-specific adapters.

### Tooling limitations

1. `generate_visual_pack.py` packages V1's three assets only (`base`, `selected`, `layout`). It cannot compile a state graph or layer bindings.
2. `validate_visual_pack.py` hard-codes version 1, `canonical-transform`, 1254×1254, two PNG roles, fixed center, and exact uniform radial angles.
3. `compare_visual_pack.py` hard-codes a Base preview, Slot 2 rotation, and rotated all-slot thumbnails. It cannot review full-state changes, masks, dynamic safe regions, or different layer strategies.
4. The JSON schema describes packaging inputs, not the complete runtime package protocol. It has no ownership, anchors, styles, glyph policies, hashes in the manifest, capabilities, or fallback policy.
5. The V5 procedural generator can express polygons, circles/arcs, masks, continuous gradients, borders, Gaussian-blurred halos, and deterministic compositing. Independent illustration, architecture, characters, painterly materials, perspective changes, and arbitrary reference semantics are possible only by treating them as external artwork, not by adding more numeric parameters.
6. Dreamscape's reference conversion uses hand-measured `DYNAMIC_RECTS` and interpolates over baked text/glyph regions. This works for a known 1672×941 page but is fragile: wide removal rectangles created visible interpolation boundaries, and missed/duplicated icons or pills required opaque underlays and scoped masks.
7. Automated difference metrics are not semantic fidelity judgments. They cannot decide whether a font/platform-rendering difference is acceptable or whether an undeclared baked glyph remains in a painting.
8. Offline toolchain and runtime currently disagree about radial-8 support. V2 must derive compatibility from one authoritative registry or generated contract.

### Expression-capability answers

| Question | V1 answer | Required V2 answer |
| --- | --- | --- |
| Must art be centered on a radial center? | Asset and selected transform: yes | No; only logical selection retains its anchor/angles |
| Must a slot look like a sector? | Not literally, but it must be transform-congruent | No |
| Is selected only a local Base overlay? | Yes | No; full frame or arbitrary declared layers |
| Must all states share composition? | Yes | No |
| Can global light/shadow change? | Not without contaminating a rotated overlay | Yes, through full-state frames |
| Can an entire scene recompose? | No | Yes |
| Can art use irregular occlusion? | Not declaratively | Yes, via pre-baked layer/mask/occluder assets |
| Can it contain independent illustration/buildings/characters? | Only as unchanging Base decoration | Yes, including state-specific versions |
| Can the visual be completely non-circular? | Partially, inside a square, but state logic remains rotational art | Yes |
| Can visual slot positions be uneven? | Anchors can, selected art cannot | Yes, through semantic bindings and per-state art |
| Can dynamic text be independent of the art layout? | One limited anchor per slot | Yes, through typed dynamic anchors |

## C. Architecture Decision

### Runtime

Keep the native `UpdateLayeredWindow` overlay and replace the V1-only renderer internals with a versioned, asset-first Theme Runtime plus a radial surface adapter. Runtime responsibilities are limited to validation orchestration, decode, scale, cache construction, state lookup, small dynamic content rendering, ordinary alpha composition, presentation, and fallback.

Runtime must not synthesize reference art, rerun blur/material generation, infer hit regions from pixels, or contain theme-specific drawing code.

### Asset-first decision

The highest-fidelity path is **Full State Frame**. If a reference needs global state changes, every required final art state is authored outside the runtime and compiled into the package. Layered assets are an optimization/authoring option when states genuinely share art. Procedural output remains a supported source of assets, not the default visual language.

### WebView2 decision

Do **not** introduce WebView2 into the radial overlay runtime.

| Criterion | Native layered window | WebView2 overlay |
| --- | --- | --- |
| Selection feedback latency | Direct cache lookup/composition/present | Adds browser process, DOM/style/layout, bridge scheduling |
| Per-pixel alpha | Native and already proven | Transparent WebView/window composition is more fragile |
| Fullscreen/no-activation behavior | Existing click-through/no-activate/tool-window flags | Requires additional window/focus/input integration |
| DPI | Existing logical->physical contract and diagnostics | CSS pixels, device scale, zoom, host bounds, and capture normalization add another scale model |
| Cache control | Explicit immutable bitmaps | Browser/image/DOM caches are less explicit |
| State switching | One state-key lookup and bitmap composition | DOM mutation/style recalculation/paint pipeline |
| Complexity/failure surface | Existing production path | Runtime availability, user-data folder, navigation, bridge, page lifecycle |
| Dynamic forms/accessibility | Limited | Strong; this is why Dreamscape is appropriate for settings/log pages |

Dreamscape demonstrates a successful reference-led document UI, not a reason to replace a latency-sensitive transparent overlay. Reuse its decomposition and ownership lessons, not its host technology.

### Logical/visual separation

The frozen direction is:

```text
Cursor / joystick input
  -> existing LayoutDefinition + dead zone + RadialSelectionEngine
  -> Logical Slot ID (0..N)
  -> fixed state mapping (0=idle, N=selected-N)
  -> theme's declared Visual Semantic Region and assets
  -> composition/presentation only
```

PNG alpha, `VisualRegion` polygons, masks, occlusion, and dynamic anchor rectangles are never hit-test authorities.

## D. Theme Runtime Protocol V2

The package is a **UI Theme Package V2** with a `radial-overlay` surface adapter. This naming keeps reusable package/compiler concepts available to future overlays and popups without pretending that one renderer is a universal UI engine.

### Manifest

The normative proposal is in `THEME_RUNTIME_PROTOCOL_V2.md`; `EXAMPLE_THEME_MANIFEST.json` is a complete non-production example. Required top-level areas are:

- identity: `id`, `displayName`, `version`, `packageRevision`;
- surface compatibility: `surface`, `layoutProfile`, `compatibleLayouts`;
- dominant authoring strategy: `renderStrategy`;
- coordinate/DPI contract: `canvas`, `referenceScale`;
- state coverage and state-to-layer asset binding: `states`;
- ordered declarative composition: `layers`;
- dynamic content placement: `dynamicAnchors`;
- constrained roles: `styles`, `glyphs`;
- optional author/debug semantics: `visualRegions`;
- baked/dynamic ownership declarations: `elementOwnership`;
- ordinary-alpha masking/occlusion metadata: `masks`;
- failure policy: `fallback`;
- integrity: `assetHashes`;
- feature negotiation: `capabilities`;
- MVP transition behavior: `transitions.mode = "direct"`.

`renderStrategy` is the dominant authoring/caching strategy, not a prohibition on hybrid layers.

### Render strategies

1. **`full-state-frame`**: `idle` and every `selected-N` bind a complete final artwork frame at Z0. This is the recommended high-fidelity path.
2. **`layered-state`**: shared static layers are loaded once; each state binds only its changed layer assets. The runtime still receives cache-ready declarations and does ordinary composition.
3. **`procedural`**: a compiler/authoring backend produces package assets or a small explicitly supported primitive description. For V2 MVP, procedural generation should occur offline and compile to the same bitmap/layer package, avoiding a new hot-path drawing engine.

### Hybrid layer order

| Z band | Role |
| ---: | --- |
| 0 | Full artwork or shared Base |
| 10 | Theme decoration |
| 20 | Selection-specific art |
| 25 | Opaque/alpha safe dynamic underlay |
| 30 | Dynamic icons/glyphs |
| 40 | Dynamic labels/text |
| 45 | Foreground occluders that intentionally cover dynamic content |
| 50 | Runtime debug; disabled in production |

Layer IDs and integer Z values are explicit. Duplicate Z values are resolved only by manifest order and should produce a compiler warning; production themes should use unique order where visual dependency exists.

### States

MVP required states are exactly:

```text
idle (slotId 0)
selected-1 ... selected-N (slotId 1 ... N)
```

Optional future names include `pressed-N`, `confirm`, `cancel`, `opening`, and `closing`, but they are not required and must not delay logic. V2 MVP supports direct switching only.

For a valid production full-frame package, every selected state required by each compatible layout must exist. A missing `selected-7` is a compiler/validator error. At runtime, a missing/corrupt state causes the candidate theme transaction to fail and preserves the old active theme. As a final defensive composer invariant, an unexpected state lookup returns cached `idle` art and logs an error, while the logical selected slot remains unchanged.

### Dynamic anchors

Every anchor declares:

- stable `id` and supported runtime `content` key;
- optional `slotId`;
- reference-space `x`, `y`, `width`, `height`;
- horizontal and vertical alignment;
- `typographyRole` or `glyphRole`;
- `maxLines`;
- overflow policy: `ellipsis`, `shrink`, `clip`, or `hide`;
- minimum shrink scale where applicable;
- rotation in degrees;
- state visibility selector;
- optional safe-surface/mask reference.

Themes select constrained typography, color, outline, and shadow roles. They do not receive arbitrary C# or CSS. This keeps the renderer small and validator-friendly.

### Glyphs

The radial adapter converts each existing `RadialSlotMapping` into a stable dynamic payload. The theme chooses a policy per input family:

- keyboard key/shortcut: text or keycap text, never one full-art regeneration per mapping;
- DS4: pre-rendered theme icon asset map, runtime symbol, or text fallback;
- generic action: known runtime symbol or text label fallback.

Unknown/missing icon IDs fall back to text inside the same anchor. A missing optional glyph must not invalidate selection or actions.

### Visual semantic regions

`visualRegions` optionally binds `slotId` to a named semantic region and debug polygon, for example Slot 1 -> `north-tower`. It exists for authoring, review overlays, and alignment diagnostics. Runtime selection never queries it.

## E. Reference Decomposition

Every new theme begins with `REFERENCE_THEME_DECOMPOSITION_SPEC.md`. It records canvas, static art, semantic regions, render strategy, dynamic text/glyphs, occlusion, masks, selected changes, safe text regions, runtime-only elements, and baked elements before art production begins.

### Static

State-independent illustration, architecture, texture, and decoration may be STATIC. Shared static art should be factored only when doing so does not reduce fidelity or create seam/occlusion problems.

### Dynamic

Action names, mapping strings, keyboard shortcuts, DS4 glyphs, connection status, window controls, changing numbers, and runtime prompts are DYNAMIC unless the product explicitly freezes them as decoration.

### State assets

Selection illumination, changed architecture, rerouted paths, character poses, global color/light changes, and any other state-dependent authored art are STATE_ASSET.

### Baked Art Ownership Contract

Every named visual element has exactly one owner:

- `STATIC`: one state-independent art layer;
- `DYNAMIC`: runtime text/icon/control layer;
- `STATE_ASSET`: one or more state-bound authored assets.

The compiler can reject duplicate declared element IDs, incompatible owner/layer kinds, a dynamic content key declared as baked, or the same element assigned to multiple owners. It cannot reliably recognize an undeclared glyph inside arbitrary pixels. Reference decomposition, optional forbidden-region masks, comparison sheets, and human review remain the authority for semantic leakage.

Dreamscape's baked icon/pill/window-glyph incidents demonstrate why this must be an explicit authoring contract rather than an assumption.

### Occlusion

Prefer authored PNG alpha and precompiled masks:

- safe dynamic underlay at Z25 to cover unavoidable baked residue or guarantee contrast;
- dynamic glyph/text at Z30/Z40;
- foreground occluder at Z45 for scene-integrated depth;
- compiler-applied alpha masks when clipping is required.

The runtime does not add shader pipelines. It composites precompiled 32-bpp PArgb layers with normal alpha.

## F. Runtime Model

### Load transaction

```text
Discover candidate
  -> parse manifest version
  -> choose V1 adapter or V2 loader
  -> validate paths/schema/layout compatibility/state coverage/capabilities
  -> verify declared SHA-256
  -> decode source assets
  -> scale directly into final physical target caches
  -> build dynamic layer caches
  -> validate every render bundle
  -> atomically swap active immutable session
  -> dispose previous session
```

The previous session is not released before the candidate is complete.

### Cache

Cache keys include package identity/revision, active layout, logical size/scale, monitor DPI/physical target size, mapping snapshot, typography/glyph policy, and relevant theme state. Separate cache responsibilities:

- state artwork cache: all required final physical states/layers;
- dynamic content cache: mapping/theme/DPI-dependent text and icons;
- final composition scratch or optional composed-state cache.

Dynamic content rebuild triggers are mapping change, theme change, DPI/target-size change, locale/font resolution change, or a dynamic style change. Selection changes do not rebuild it.

### Selection hot path

```text
selected slot already computed by existing logic
  -> state key lookup
  -> cached state bitmap/layer references
  -> cached dynamic bitmap references
  -> alpha composition (or cached final state)
  -> UpdateLayeredWindow
```

No disk I/O, PNG decode, hash calculation, master resize, Gaussian blur, material generation, font discovery, or layout parsing is allowed on this path.

### Atomic swap and refresh

Theme changes and DPI changes should both create a complete immutable render bundle before swapping it. This strengthens the current design: current theme switches are session-atomic, while an existing session refresh calls asset and dynamic cache rebuilds sequentially. V2 should never expose a new artwork size with old dynamic content if the second rebuild fails.

### Fallback

- Invalid/missing/unsupported candidate while an active theme exists: retain the active theme and its business/session state.
- Startup failure without an active theme: try `radial-v5` through the V1 adapter.
- Default failure: hide the visual overlay safely, log once, but leave input/action processing intact.
- Missing selected state after successful validation should be impossible; defensive lookup uses idle art without changing the selected slot.
- Decode/hash/version/capability failures occur before swap.

## G. DPI / Performance

### Asset spaces

1. **Reference Space**: package canvas coordinates used by authored frames, layers, masks, regions, and anchors.
2. **Logical Space**: DPI-independent overlay size. Existing 280 logical pixels at 100% remains the radial baseline and is multiplied by the existing `ScalePercent` semantics.
3. **Physical Space**: final monitor backing size used by the layered window and `UpdateLayeredWindow`.

The required direction is always Reference -> final Physical cache. Do not downsample a master to a low-resolution logical bitmap and then upscale it to physical pixels.

For the radial V2 MVP, a theme may use arbitrary non-circular art within a square transparent reference canvas. `referenceScale.anchor` maps the logical radial input anchor to a reference-space point. This allows asymmetric composition without moving the logical selection origin. General rectangular surfaces can reuse the package protocol later through their own surface adapter.

### Raw RGBA cache estimates for a 280-logical-pixel radial surface

All values below are binary MiB and count final state frames only (`width × height × 4 × stateCount`). PNG file size is irrelevant to decoded memory.

| DPI | Physical size | One RGBA frame | radial-6: 7 frames | radial-8: 9 frames |
| ---: | ---: | ---: | ---: | ---: |
| 100% / 96 | 280×280 | 0.30 MiB | 2.09 MiB | 2.69 MiB |
| 150% / 144 | 420×420 | 0.67 MiB | 4.71 MiB | 6.06 MiB |
| 200% / 192 | 560×560 | 1.20 MiB | 8.37 MiB | 10.77 MiB |

A 1254×1254 decoded RGBA master is approximately 6.00 MiB. Holding all 7/9 masters concurrently would cost approximately 42/54 MiB before final caches. The recommended loader decodes and scales states sequentially, retains only final physical caches plus compressed package files, and releases source decodes after bundle construction. At 200% DPI, add roughly 1.20 MiB for a full-canvas dynamic cache and 1.20 MiB for a composition scratch buffer. Theme switching temporarily holds old and new bundles; budget approximately 26 MiB for two radial-8 200% bundles plus scratch, excluding short-lived sequential decode buffers.

If a future theme uses a larger logical surface, the compiler must emit a budget estimate and the loader must enforce configurable decoded-state and peak-switch budgets. A 1254 master is an authoring resolution, not a requirement to retain 54 MiB of decoded masters.

### Latency and preload

All required MVP states are preloaded when the theme activates. Direct state switching is the only V2 MVP transition. Optional future crossfade/frame sequences must never delay confirm/cancel or selection; logic completes immediately and animation is best-effort visual follow-through.

## H. Legacy Compatibility

### `radial-v5`

Continues to load unchanged through a V1 adapter. The adapter maps:

- Base -> Z0 shared Base;
- canonical selected asset + slot transforms -> cached Z20 selected layers;
- current dynamic content -> a compatibility dynamic layer;
- V1 fallback and layout semantics -> existing behavior.

### `radial-8-minimal-v1`

Also continues unchanged through the same V1 adapter. Runtime HEAD already supports radial-8; Phase 0 must update the offline compatibility declarations so the compiler does not incorrectly reject it.

### Manifest version

- `version: 1`: exact legacy parser/adapter; no new fields required and no asset regeneration.
- `version: 2`: V2 schema, state/layer/anchor/integrity contract.
- unsupported versions: candidate rejected before decode; active session retained.

Do not silently interpret malformed V2 as V1 and do not migrate files in place.

## I. Authoring Pipeline

```text
Reference Image
  -> completed Decomposition Spec and ownership table
  -> choose Full Frame / Layered / Procedural dominant strategy
  -> produce source artwork in any suitable art tool
  -> export state/layer assets and manifest source
  -> Theme Compiler engineering pass
  -> production package + hashes + review artifacts
  -> automated contract validation
  -> runtime screenshot capture at required DPI/layout states
  -> reference fidelity review
  -> human approval and freeze
```

Artwork may come from a human artist, image generation, Photoshop, Blender, Illustrator, Figma, or the existing Pillow generator. The compiler does not create the art.

## J. Theme Compiler

### Inputs

- V2 authoring manifest;
- decomposition spec/ownership table;
- source state frames/layers;
- optional compiler-only masks and safe-region overlays;
- optional reference image and approved fonts/icon assets;
- target protocol/schema version.

### Outputs

- normalized production `theme.json`;
- copied/normalized cache-ready RGBA assets;
- SHA-256 map;
- validation report and memory estimate;
- state contact sheet;
- layer/anchor/semantic-region debug sheet;
- reference/runtime comparison sheet template;
- deterministic build metadata.

### Validation

Validate JSON uniqueness/schema, local normalized paths/no traversal, IDs, layout compatibility, required state coverage, layer order/kinds, dimensions/mode/alpha, anchor bounds and style references, visual-region slot bindings, ownership uniqueness, masks/safe surfaces, glyph fallbacks, capabilities, hash coverage, decoded memory budgets, and legacy/runtime compatibility.

Semantic pixel ownership and subjective fidelity remain human-reviewed. Automatic pixel diff, MAE/RMSE/SSIM, and masked static comparisons are supporting evidence only; pixel-perfect matching is not required where dynamic text or platform rasterization differs.

## K. First Fidelity Theme

Recommended ID: `radial-reference-monument-v1`.

### Target difficulty

- radial-6 logical topology;
- no visible wheel or sector UI;
- an asymmetric illustrated architectural scene;
- six semantic landmarks at visually uneven positions;
- Slot 1 represented by a tall northern tower;
- dynamic action labels and keyboard/DS4 glyphs integrated into safe surfaces;
- at least one foreground occluder;
- every selected state changes global scene lighting/path emphasis, not just a local glow.

### Required capabilities

Full-state frames, explicit semantic bindings, dynamic anchors, theme typography/colors, glyph fallback, underlay/occluder layers, atomic preload, direct switching, 100/150/200% DPI caches, and radial-6 stable action behavior.

### Acceptance

- all seven required states validate and preload;
- no dynamic label/glyph is baked into state art;
- logical slot tests remain byte-for-byte behaviorally equivalent;
- visual comparison includes reference, idle, every selected state, anchor/region overlay, and runtime captures at 100/150/200%;
- human approval is final authority;
- no theme-specific runtime code.

## L. Second Theme Validation

Recommended concept: `radial-reference-hud-v1`, a radically different radial-8 technology/HUD theme using shared background + per-slot layered overlays, runtime vector-like DS4 symbols, compact labels, and no illustrated architecture.

Allowed changes are a new package and compiler inputs only. Fixes to generic schema/compiler bugs are allowed before final acceptance, but no theme-ID checks, bespoke renderer branch, new selection algorithm, or core runtime layer kind may be added merely to make Theme B work.

Success criterion: the same installed V2 runtime loads Theme A and Theme B; only package data changes; radial-6/radial-8 logical behavior and mappings remain unchanged. If Theme B needs a core runtime rewrite, the protocol is not yet sufficiently general and Phase 6 fails.

## M. Existing Components

### Keep

- `LayoutDefinition`, `LayoutProfileRegistry`, `RadialSlotDefinition` as logical topology authority.
- `RadialSelectionEngine`, dead zone, slot IDs, and the 0->idle mapping.
- `RadialMenuController`, trigger sources/recognizers, completion handling, action resolver/executors.
- `RadialMappingsByProfile`, settings persistence, DS4/keyboard action catalogs.
- `RadialDpiScaling`, monitor DPI discovery, physical dead-zone semantics, `OnDpiChanged` trigger.
- no-activate click-through layered window and `UpdateLayeredWindow` presentation.
- catalog discovery, build-before-install, retain-active fallback pattern.
- current dynamic-content invalidation concept.
- V1 production packs and the existing procedural V5 generator.
- validator/comparison/human-approval concepts.

### Demote

- `canonical-transform` from the only production asset model to one supported legacy/layered optimization.
- the V5 Pillow generator from primary route to `visual_tools/generators/procedural/` authoring backend.
- numeric radial material parameters from universal theme vocabulary to one procedural backend's input.
- pixel-diff scores from acceptance result to review aid.

### Replace or generalize

- V1-only manifest parser with a versioned V1 adapter + V2 package loader.
- fixed `RadialVisualPackCache` with immutable state/layer render bundles.
- fixed three-layer `DrawComposition` with a constrained declarative layer composer.
- hard-coded `RadialDynamicContentCache` style/anchor logic with anchor/style/glyph-aware caches.
- V1-only generator/validator/compare assumptions with compiler, validators, and review modules.

Suggested future tool layout:

```text
visual_tools/
  generators/
    procedural/
  compiler/
  validators/
  review/
  schemas/
```

### Do not touch for Theme Runtime V2

- MOVE handling and `VirtualJoystickController`;
- `ActivationRadius`, `JoystickRadius`, and `VisualRadius`;
- radial angle selection and dead-zone result;
- DoubleTap recognizers/timing;
- confirm/cancel semantics;
- DS4 and keyboard action execution;
- `MappingsByProfile` and mapping persistence;
- service/output business state;
- settings persistence semantics;
- logical-to-physical DPI semantics.

### Runtime files likely to change in a future implementation

| File/area | Why | Main risk | Compatibility strategy |
| --- | --- | --- | --- |
| `RadialVisualPack.cs` | Introduce version discrimination or isolate V1 types | Breaking frozen manifests | Keep V1 parser/tests unchanged behind adapter |
| `RadialVisualPackCatalog.cs` | Discover V1 and V2 package entries/capabilities | Duplicate IDs/version ambiguity | One normalized catalog entry; reject ambiguous IDs |
| `RadialVisualPackRuntime.cs` | Build normalized immutable V1/V2 sessions and atomic bundles | disposing active cache too early | Candidate-first construction and swap tests |
| `RadialMenuOverlay.cs` | Use generic layer composer/state key; possibly anchor-aware placement | latency, alpha, DPI regression | Preserve window flags, physical target, and ULW call |
| `RadialDynamicContent.cs` | Anchor/style/glyph roles and better invalidation keys | per-frame GDI work/font differences | pre-render on theme/mapping/DPI change only |
| `RadialLayoutDefinition.cs` | Ideally no selection changes; may expose adapter-safe metadata | accidental coupling of visual and logic | keep angles/slot IDs authoritative; visual metadata elsewhere |
| project asset-copy rules | Include V2 package directory content | missing deployed assets | wildcard package copy tests |
| new package/loader/compiler-facing classes | Represent states/layers/styles/hashes | over-generalization | implement only V2 MVP kinds |
| radial visual-pack tests | Add V1 regression and V2 state/transaction/DPI tests | false confidence from unit-only rendering | add runtime capture gates separately |

`RadialMenuController`, `RadialSelectionEngine`, action execution, trigger recognition, and settings store should not require semantic changes.

## N. Implementation Phases

Detailed file/test/rollback planning is in `MIGRATION_PLAN.md`.

### Phase 0: Protocol / schemas / docs

- **Goal:** freeze V2 vocabulary, schema, state coverage, ownership, fallback, budgets, and correct radial-8 compatibility drift.
- **Expected files:** new protocol/schema/decomposition docs and examples under `visual_tools`; no runtime code.
- **Tests:** schema fixtures, valid/invalid manifest corpus, documentation consistency check.
- **Rollback point:** remove new unreferenced V2 docs/schemas.
- **Risk:** premature abstraction or ambiguous state/layer binding.

### Phase 1: Legacy-compatible runtime abstraction

- **Goal:** normalize V1 into an internal render plan without changing pixels or behavior.
- **Expected files:** V1 adapter/render-plan types; small catalog/runtime/overlay refactor; V1 regression tests.
- **Tests:** all current radial tests, frozen asset hashes, pixel composition comparison, failure retention.
- **Rollback point:** legacy renderer remains selectable behind an internal feature flag during development.
- **Risk:** changing disposal, DPI, settings, or selection while supposedly refactoring.

### Phase 2: Full State Frame support

- **Goal:** parse/preload/switch `idle` + `selected-N` frames.
- **Expected files:** V2 loader, state cache builder, composer, schema fixtures, runtime tests.
- **Tests:** state coverage, corrupt/missing state, atomic swap, 6/8 states, 100/150/200% DPI, hot-path no-decode instrumentation.
- **Rollback point:** disable V2 discovery; V1 remains unchanged.
- **Risk:** peak decode memory and partial refresh.

### Phase 3: Dynamic Anchors / Glyphs

- **Goal:** add constrained theme-driven text/icon placement and cache invalidation.
- **Expected files:** anchor/style/glyph models, dynamic renderer/cache, icon fixtures.
- **Tests:** ellipsis/shrink/rotation/visibility, keyboard shortcuts, DS4 asset/runtime/text fallback, mapping/DPI/theme invalidation, no selection rebuild.
- **Rollback point:** V2 full art remains usable without dynamic capabilities.
- **Risk:** font variability, overflow, too much runtime styling.

### Phase 4: Theme Compiler / Validator

- **Goal:** transactional engineering compiler and V2 review sheets.
- **Expected files:** `compiler/`, `validators/`, `review/`, `schemas/`; upgraded wrapper entry points.
- **Tests:** deterministic outputs, path traversal, hashes, alpha/dimensions, ownership conflicts, state completeness, budget report, review snapshots.
- **Rollback point:** compiler output is not installed until a package passes independent runtime loader tests.
- **Risk:** compiler/runtime schema divergence.

### Phase 5: First high-fidelity reference theme

- **Goal:** validate Theme A's full-frame fidelity and dynamic integration.
- **Expected files:** new untracked review workspace first; production pack only after separate approval.
- **Tests:** package/compiler gates, runtime captures, DPI/performance, existing input suite.
- **Rollback point:** remove/disable only the new package; runtime and V1 packs remain.
- **Risk:** art ownership leakage or excessive decoded memory.

### Phase 6: Second radically different theme validation

- **Goal:** prove the protocol with a radial-8 layered HUD theme.
- **Expected files:** Theme B package/reviews; no theme-specific runtime code.
- **Tests:** cross-theme switching, 6/8 mappings, fallback, performance, protocol conformance diff.
- **Rollback point:** reject Theme B and return to protocol review without affecting Theme A/V1.
- **Risk:** discovering hidden assumptions in anchor/layer/glyph abstractions.

## O. Risk

### Runtime risk

The largest risk is accidentally coupling the new package state to logical selection or altering the DPI/presentation hot path. Mitigation: make selection output an input to the composer, keep V1 pixel/behavior regression fixtures, and instrument hot-path decode/build counts.

### Asset risk

Full-state art can silently bake dynamic content or drift between states. Mitigation: ownership table, anchor-safe masks, state contact sheets, static-region comparisons, and human review.

### Migration risk

A “clean rewrite” could invalidate V1 or require mass asset migration. Mitigation: explicit version dispatch and adapter; no in-place manifest reinterpretation.

### Performance risk

Naively retaining all 1254 masters or composing expensive text/materials per selection can increase memory and latency. Mitigation: sequential decode to final physical caches, release masters, pre-render dynamic content, direct switching, memory budget gate.

### Tooling risk

Independent C# and Python compatibility tables can drift, as radial-8 already demonstrates. Mitigation: one versioned schema/profile registry source or generated fixtures consumed by both toolchain and tests.

## P. Final Recommendation

| Decision | Recommendation |
| --- | --- |
| Recommended Runtime | Existing native `UpdateLayeredWindow` host with a versioned asset-first Theme Runtime and radial surface adapter |
| Recommended Asset Model | Full State Frame as fidelity-first default; Layered State for genuine sharing; Procedural as offline authoring backend; hybrids allowed |
| Recommended Manifest | UI Theme Package V2 described in `THEME_RUNTIME_PROTOCOL_V2.md`, with explicit state/layer binding, anchors, roles, ownership, hashes, capabilities, and fallback |
| Recommended Authoring Workflow | Reference -> Decomposition/Ownership -> external artwork -> compiler -> validator/review -> runtime capture -> human approval -> freeze |
| Recommended Compiler | Offline transactional Theme Compiler that engineers supplied art, never invents it |
| Recommended Fallback | Retain last valid active session; startup fallback to unchanged `radial-v5`; never change selection/input/business state |
| Recommended first implementation phase | Complete and approve Phase 0, then make Phase 1 the first coding phase: V1-compatible internal render-plan abstraction with zero visual/behavior change |

## Q. Git

Expected final repository state for this design turn:

| Item | Required value |
| --- | --- |
| HEAD | `e5ac2718b841f051772f010416af444fae1c95f2` |
| Tracked diff | `0` |
| Staging | `0` |
| Protected pre-existing untracked files | preserved |
| New review files | only the files under `pc_ds4_server/visual_tools/reviews/reference-driven-theme-pipeline/` |

Review files:

- `REFERENCE_DRIVEN_THEME_PIPELINE.md`
- `THEME_RUNTIME_PROTOCOL_V2.md`
- `REFERENCE_THEME_DECOMPOSITION_SPEC.md`
- `MIGRATION_PLAN.md`
- `EXAMPLE_THEME_MANIFEST.json`

No source changes. No test changes. No visual-pack changes. No asset regeneration. No stage, commit, push, reset, restore, stash, or clean.
