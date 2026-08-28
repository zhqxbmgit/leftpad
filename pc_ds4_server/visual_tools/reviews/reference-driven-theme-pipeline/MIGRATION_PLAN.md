# THEME RUNTIME V2 MIGRATION PLAN

Status: implementation plan only. No files named below are changed by this review.

## 1. Migration outcome

Add reference-driven UI Theme Package V2 without changing the behavior or assets of V1 `radial-v5` and `radial-8-minimal-v1`. The native layered-window host, logical selection, actions, mappings, persistence, and DPI semantics remain stable.

The migration is additive and reversible:

```text
V1 manifest -> V1 adapter -> normalized render plan
V2 theme.json -> V2 loader -> normalized render plan
                                  -> immutable physical render bundle
                                  -> radial composer / UpdateLayeredWindow
```

No V1 package is rewritten or upgraded in place.

## 2. Frozen behavior boundary

The following are non-goals and regression boundaries for every phase:

- MOVE input and `VirtualJoystickController`;
- `ActivationRadius`, `JoystickRadius`, `VisualRadius`;
- double-tap recognition and timing;
- `RadialSelectionEngine` angle/tie/dead-zone behavior;
- logical slot IDs and slot 0 cancellation;
- confirm/cancel and trigger-source matching;
- DS4/keyboard resolution and action execution;
- `MappingsByProfile`, mapping profile isolation, and persistence;
- receiver service/output state;
- existing settings persistence semantics;
- logical dead-zone -> physical DPI conversion;
- current no-activate/click-through/topmost layered window behavior.

If a phase requires changing one of these, stop and split it into a separate, explicitly authorized behavior project.

## 3. Existing components and change map

### Files expected to remain semantically unchanged

| File/area | Reason |
| --- | --- |
| `RadialSelectionEngine.cs` | Logical hit testing is already correct and pixel-independent |
| `RadialMenuController.cs` | Should continue to produce slot IDs and pass them to the overlay |
| `RadialTriggerRecognizers.cs` | Input timing is not visual |
| `RadialActionResolver.cs` / executors / completion handler | Business action behavior is not visual |
| `VirtualJoystickController.cs` / `VirtualJoystickOverlay.cs` | MOVE geometry/visualization is outside the theme pipeline |
| `RadialMenuSettingsStore.cs` | Existing persistence format must remain stable |
| `Ds4Service.cs` | Service/input pipeline must not learn theme concepts |

Test additions may reference these files, but implementation should not modify their semantics.

### Existing files likely to be edited later

| File | Expected future change | Risk | Required compatibility measure |
| --- | --- | --- | --- |
| `RadialVisualPack.cs` | Separate V1 types/parser/cache from normalized runtime interfaces; retain V1 constants | V1 load/hash/pixel regression | Existing V1 tests and frozen production assets remain authoritative |
| `RadialVisualPackCatalog.cs` | Discover V1 and V2, normalize catalog metadata/capabilities | duplicate ID and ambiguous directory | reject collisions; deterministic default order |
| `RadialVisualPackRuntime.cs` | Candidate render-bundle build, V1/V2 dispatch, atomic install/refresh | active-session disposal or partial refresh | construct all candidate caches before swap; fault-injection tests |
| `RadialMenuOverlay.cs` | state-key mapping and constrained layer composition | latency/alpha/DPI/window regression | preserve window styles, physical target, placement, and `UpdateLayeredWindow` contract |
| `RadialDynamicContent.cs` | V1 compatibility renderer plus V2 anchor/style/glyph cache | per-selection redraw or font drift | cache-key tests and resolved-font diagnostics |
| `RadialLayoutDefinition.cs` | Prefer no semantic edits; at most expose stable adapter metadata | visual metadata becoming hit-test authority | keep slot ID/angle/profile immutable and visual regions elsewhere |
| `PcDs4Server.csproj` | Ensure all V2 package files copy to output | deployed file omission | output-directory manifest/asset coverage tests |
| radial visual tests | Add V1/V2 compatibility, cache, fallback, and render-plan fixtures | insufficient runtime confidence | combine unit, pixel fixture, and opt-in capture gates |

### Suggested new runtime files (names provisional)

| File | Responsibility |
| --- | --- |
| `UiThemePackageManifest.cs` | Strict V2 manifest DTOs/enums and normalized validation errors |
| `UiThemePackageLoader.cs` | Path/schema/hash/decode orchestration for V2 |
| `UiThemeRenderPlan.cs` | Version-neutral immutable state/layer/anchor plan |
| `UiThemeRenderBundle.cs` | Physical-space state/static/dynamic caches and disposal |
| `LegacyRadialVisualPackAdapter.cs` | Exact V1 -> normalized render plan mapping |
| `RadialThemeComposer.cs` | Slot->state mapping and ordered ordinary-alpha composition |
| `ThemeDynamicContentCache.cs` | V2 anchor/style/glyph rendering and invalidation |

Names should be chosen during implementation after the internal seam is proven. Do not expose public general-purpose APIs prematurely.

## 4. Toolchain migration map

Keep the existing entry points available while reorganizing internals:

```text
visual_tools/
  generators/
    procedural/
      build_radial_raster_v5.py (frozen or wrapped, never regenerated implicitly)
  compiler/
    compile_theme.py
  validators/
    validate_v1_visual_pack.py
    validate_v2_theme.py
  review/
    build_theme_review.py
  schemas/
    theme_config.schema.json       (V1 compatibility)
    theme_package_v2.schema.json
```

`generate_visual_pack.py`, `validate_visual_pack.py`, and `compare_visual_pack.py` may remain compatibility wrappers. V1 behavior and CLI output should not silently change when V2 support is introduced.

The compiler consumes finished artwork and performs engineering. It does not generate illustration or decide visual style.

## 5. Phase 0 — Protocol, schemas, and contract alignment

### Goal

Freeze the smallest V2 contract that supports full frames, shared layers, dynamic anchors/glyphs, ownership, hashes, direct switching, and V1 fallback.

### Expected files

- `visual_tools/schemas/theme_package_v2.schema.json`;
- normative protocol and decomposition docs in an approved formal-doc location;
- valid/invalid manifest fixtures;
- a generated/shared Layout Profile compatibility fixture;
- no production runtime changes.

### Work

1. Resolve whether the production filename is `theme.json` and reserve `manifest.json` for V1, or allow both with unambiguous version dispatch. Recommendation: `theme.json` for V2.
2. Define closed enums and strict unknown-field behavior.
3. Freeze state names, layer kinds, dynamic content keys, role limits, and path rules.
4. Align radial-8 status: current C# runtime and tests show integrated support, while Python/docs say not integrated.
5. Decide initial radial adapter constraints (recommended: square 280-logical surface, arbitrary art within it).
6. Freeze compiler/runtime memory limits and review gates.

### Tests

- schema accepts the example and minimal full/layered fixtures;
- missing selected state, duplicate slot, unsupported layer/capability, invalid anchor, path traversal, ownership conflict, and bad hash fail;
- compatibility fixture reports radial-6 and radial-8 consistently in C# and Python;
- docs/example schema consistency check.

### Rollback point

Delete the unreferenced V2 schemas/docs/fixtures. No runtime or pack is affected.

### Primary risk

Encoding speculative generality before the two validation themes are understood. Countermeasure: only include capabilities required by Full Frame, Layered, dynamic text/glyph, masks/occlusion, and atomic fallback.

## 6. Phase 1 — Legacy-compatible runtime abstraction

### Goal

Route V1 through a normalized internal render plan with zero user-visible change. This is the recommended first coding phase.

### Expected files

- V1 adapter and internal render-plan/bundle interfaces;
- focused edits to V1 catalog/runtime/cache/overlay composition seams;
- new regression tests;
- no V2 package discovery enabled by default yet.

### Work

1. Represent the V1 Base as a shared Z0 layer.
2. Represent rotated selected slot caches as Z20 state-layer bindings.
3. Represent current dynamic bitmap as a compatibility dynamic layer.
4. Preserve the current selection-to-cache mapping and current physical output.
5. Make bundle construction immutable and candidate-first, including DPI refresh.
6. Add instrumentation for decode, resize, dynamic build, composition, and install counts.

### Tests

- all existing radial test suites;
- production catalog contains exactly the two released packs;
- frozen hashes remain unchanged;
- radial-6 and radial-8 selected cache count and angles remain unchanged;
- V1 composition fixture/pixel comparison at 280, 420, 560;
- same settings/theme/DPI reuses caches;
- mapping change rebuilds dynamic cache only;
- failed theme and failed DPI bundle build retain the previous complete bundle;
- active session is disposed only after successful swap.

### Rollback point

Keep the old V1 composer behind an internal development-only switch until pixel and behavior equivalence is demonstrated. Remove the new abstraction if equivalence cannot be achieved without touching input logic.

### Primary risk

Calling an architectural refactor “visual-only” while changing disposal order, layout source, or DPI placement. Tests must compare observable pixels, state, and counters.

## 7. Phase 2 — Full State Frame support

### Goal

Load V2 `idle` plus all `selected-N` full frames, cache at final physical size, and switch by state lookup.

### Expected files

- V2 manifest DTO/parser/loader;
- state cache builder and normalized render-plan bindings;
- catalog version dispatch;
- composer support for V2 state assets;
- loader/runtime tests and tiny non-art fixtures.

### Work

1. Strict parse and local-path validation.
2. Layout-profile compatibility/state-coverage validation.
3. Required SHA-256 verification.
4. Sequential decode -> direct physical scale -> source decode release.
5. Bundle invariant checks for size, pixel format, and every required state.
6. Candidate-first theme and DPI transactions.
7. Slot 0/N -> idle/selected-N fixed mapping.
8. Defensive unexpected lookup -> idle art without changing logical slot.

### Tests

- radial-6 7-state and radial-8 9-state fixtures;
- missing `selected-7`, duplicate slot state, corrupt PNG, wrong dimension/mode, hash mismatch, unsupported version/capability;
- startup fallback to V1 `radial-v5`;
- active V1 -> V2 -> V1 and V2 -> invalid switches;
- physical caches 280/420/560 and direct master-to-physical checks;
- no disk/decode/resize on repeated selection changes;
- memory estimate and peak candidate+active bounds;
- input/action/mapping suites unchanged.

### Rollback point

Disable V2 discovery/version dispatch. V1 adapter remains production path.

### Primary risk

Peak memory from decoding all source states. Build sequentially, release masters, and enforce a candidate budget before swap.

## 8. Phase 3 — Dynamic Anchors and Glyphs

### Goal

Render theme-declared dynamic action/mapping text and keyboard/DS4/generic glyphs without baking them into state art.

### Expected files

- anchor/style/glyph models and validators;
- V2 dynamic cache;
- closed runtime-symbol set and test assets;
- content-payload adapter from existing `RadialSlotMapping`;
- render and invalidation tests.

### Work

1. Implement bounded align/max-lines/ellipsis/shrink/clip/hide/rotation behavior.
2. Resolve constrained typography/color/outline/shadow roles.
3. Add keyboard text-keycap, DS4 asset-map/runtime-symbol/text, and generic fallback policies.
4. Add safe-surface and visibility-state checks.
5. Key cache by package/revision, physical target, mappings, resolved font, style/glyph policy, and locale where applicable.
6. Keep selection state outside the dynamic rebuild key unless visibility genuinely changes the cached layer; prebuild variants rather than draw each frame.

### Tests

- representative strings including unset, single key, long shortcut, DS4 cross, D-pad, and unknown ID;
- anchor bounds and every overflow policy;
- rotation and idle/selected visibility;
- missing asset -> symbol/text fallback;
- mapping/theme/DPI/font change rebuilds; selection-only change does not;
- state art contains no declared dynamic element according to compiler fixtures/review masks;
- GDI/PArgb output and DPI capture review.

### Rollback point

V2 full-frame themes without dynamic capability continue to load if their package does not require it. Disable the capability rather than compromising fallback/input behavior.

### Primary risk

Building a CSS clone or performing GDI text work on every selection. Keep roles closed and pre-render variants.

## 9. Phase 4 — Theme Compiler, Validator, and Review

### Goal

Provide a deterministic, transactional engineering pipeline from authored source assets to a production V2 package.

### Expected files

- `compiler/`, `validators/`, `review/`, and V2 schemas;
- compiler CLI and build report;
- deterministic fixture workspaces;
- review-sheet snapshot/golden tests.

### Compiler responsibilities

- validate manifest/decomposition inputs;
- normalize/copy RGBA assets without inventing art;
- apply compiler-only alpha masks if declared;
- validate dimensions/alpha/state coverage/anchors/styles/glyphs/ownership;
- calculate hashes and memory budgets;
- stage complete output then publish atomically;
- generate state, layer, anchor/region, and comparison sheets;
- emit deterministic metadata.

### Tests

- byte-identical deterministic build from fixed inputs;
- failure never replaces an existing output;
- path traversal/symlink escape/case collision;
- hash coverage and mismatch;
- masks, alpha residue, safe surface coverage, ownership conflict;
- complete state contact sheets for 6 and 8 slots;
- review output cannot overwrite an input;
- V1 wrapper regression tests continue to pass.

### Rollback point

Compiler output remains review-only and is not copied into production assets until independent runtime validation succeeds.

### Primary risk

Schema logic diverging among compiler, Python validator, and C# loader. Prefer generated schema fixtures and shared golden manifests over three handwritten compatibility tables.

## 10. Phase 5 — First high-fidelity reference theme

### Goal

Prove that a scene/illustration theme with global selected-state changes and dynamic content requires no theme-specific runtime code.

### Theme

`radial-reference-monument-v1`, radial-6, Full State Frame hybrid.

### Expected files

- initially a new review workspace with decomposition, source exports, compiled candidate, and review artifacts;
- production package only in a separately authorized asset integration turn;
- no core runtime changes after generic Phase 2/3 capability completion except genuine protocol bug fixes reviewed separately.

### Tests/review

- seven required full frames and all hashes;
- ownership and baked leakage review;
- semantic region/anchor overlay;
- keyboard, shortcut, DS4, unset mappings;
- 100/150/200% runtime captures;
- state-switch latency and decode/build counters;
- invalid/corrupt candidate retention;
- all stable radial/input tests.

### Rollback point

Remove/disable only the candidate package. V1 and runtime remain intact.

### Primary risk

An attractive scene that still has baked dynamic glyphs or cannot accommodate long labels. The decomposition/ownership gate precedes final art.

## 11. Phase 6 — Radically different second theme

### Goal

Prove protocol generality with a layered technology/HUD theme on radial-8.

### Theme

`radial-reference-hud-v1`, radial-8, Layered State + dynamic runtime symbols/text.

### Allowed changes

- theme package/source/review files;
- fixes to a demonstrably generic compiler/runtime defect covered by both Theme A and Theme B tests.

### Disallowed changes

- `if (themeId == ...)` branches;
- theme-specific renderers or layer kinds;
- new selection behavior;
- changing radial-8 slot angles/mappings/actions;
- weakening validation solely for Theme B.

### Tests/review

- nine required states/overlays;
- V1, Theme A, and Theme B switch matrix;
- radial-6/radial-8 mappings and execution;
- 100/150/200% cache/memory/latency;
- missing state/glyph/hash fallback;
- protocol-conformance report showing no new core capability.

### Rollback point

Reject Theme B and reopen protocol design. Theme A and V1 remain valid.

### Primary risk

Theme A accidentally shaped the protocol around illustration/full-frame assumptions. Theme B is deliberately chosen to expose that bias.

## 12. Test matrix across phases

| Area | V1 radial-6 | V1 radial-8 | V2 full radial-6 | V2 layered radial-8 |
| --- | --- | --- | --- | --- |
| Discover/load | required | required | required | required |
| State coverage | compatibility transform | compatibility transform | 7 frames | 9 states/overlays |
| Selection | unchanged | unchanged | unchanged | unchanged |
| Mapping/action | unchanged | unchanged | unchanged | unchanged |
| 96/144/192 DPI | required | required | required | required |
| Dynamic cache reuse | required | required | required | required |
| Failed switch retains active | required | required | required | required |
| Startup fallback | V1 default | V1 default | to V1 default | to V1 default |
| Hot path disk/decode/build | zero | zero | zero | zero |
| Human fidelity review | regression | regression | reference authority | reference authority |

## 13. Release and rollback policy

Each phase must be a separately reviewable checkpoint. Do not combine schema, runtime abstraction, compiler, and first art package in one irreversible change.

Recommended release gates:

1. Phase 0 contract approval.
2. Phase 1 V1 pixel/behavior equivalence.
3. Phase 2 V2 loader/full-state hidden/opt-in runtime gate.
4. Phase 3 dynamic capability gate.
5. Phase 4 compiler independent validation.
6. Theme A separate asset approval.
7. Theme B generality proof before declaring V2 stable.

At every runtime gate, disabling V2 discovery must restore the known V1 path without asset migration or settings reset.

## 14. Completion criteria

Migration is complete only when:

- V1 packs run unchanged and require no regeneration;
- radial-6 and radial-8 toolchain/runtime compatibility declarations agree;
- Theme A and Theme B use one generic V2 runtime with package-only visual differences;
- all required states are preloaded and selection hot path performs no decode/build;
- theme/DPI switches are fully atomic;
- dynamic mappings never require full-art regeneration;
- fallback preserves input, logical slot, mappings, and service state;
- Reference Fidelity Review is human-approved at required DPI;
- no theme-specific code or WebView2 radial overlay was introduced.
