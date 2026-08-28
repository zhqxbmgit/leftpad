# REFERENCE THEME DECOMPOSITION SPEC

Status: required authoring/review template for proposed UI Theme Package V2.

This document is completed before artwork export or runtime implementation. Its purpose is to convert an arbitrary reference image into an explicit asset/state/dynamic ownership plan. It is not a request for the runtime to recreate the image procedurally.

## 1. Theme identity and reference provenance

Record:

- proposed theme ID and display name;
- target surface (`radial-overlay` for the first implementation);
- target logical Layout Profile(s);
- reference file name, dimensions, color space, and SHA-256 when locally available;
- reference license/provenance and any usage restrictions;
- reviewer/author and date;
- whether the reference is a visual target, loose inspiration, or exact product mockup.

Do not copy business values from a reference merely because they are visible. Separate visual language from runtime data.

## 2. Canvas and coordinate spaces

Answer:

1. What is the Reference-Space width and height?
2. Is the intended runtime art full-bleed, transparent, or mixed?
3. What Reference-Space point maps to the runtime input/session anchor?
4. What logical width/height does 100% application scale represent?
5. Does the initial surface adapter support the aspect ratio?
6. Which DPI/runtime captures are mandatory (normally 100%, 150%, 200%)?
7. Is any crop permitted? If yes, define it; otherwise use contain fit.

Example:

```text
Reference: 1254×1254 RGBA sRGB
Logical: 280×280 at 96 DPI and ScalePercent 100
Presentation anchor: reference (627,627) -> input screen anchor
Physical targets: 280, 420, 560 at 96, 144, 192 DPI
Fit: uniform contain; no crop
```

## 3. Static background and state-independent art

List every state-independent visual element, for example:

- environment/sky/architecture;
- background texture;
- fixed frame/decoration;
- state-independent foreground object;
- theme branding approved as fixed decoration.

For each element, state whether it belongs in:

- the complete full-state frames (duplicated intentionally for fidelity);
- a shared STATIC layer;
- a foreground occluder layer;
- no package at all.

Do not factor art into shared layers merely to save files if the separation creates visible seams, color-space mismatch, or state-transition drift.

## 4. Interactive semantic regions

For each logical slot, record a visual semantic binding. The logical angle comes from `LayoutDefinition`; this section does not redefine it.

| Logical slot | Logical direction/profile fact | Visual region ID | Visual description | Debug polygon needed? |
| ---: | --- | --- | --- | --- |
| 1 | example: top / 0° | `north-tower` | tall tower at top-left of illustration | yes/no |

Rules:

- visual positions MAY be uneven, asymmetric, non-radial, overlapping, or perspective-based;
- a slot MAY map to a building, path, character, card, portal, or abstract feature;
- polygons/labels are authoring/debug aids only;
- transparent pixels and visible shapes MUST NOT define hit testing.

## 5. Full-state versus layered-state decision

Answer the following for every selected state:

1. Does global lighting change?
2. Does any non-target area intentionally change?
3. Does geometry/architecture/character pose/path routing change?
4. Are shadows or reflections state-dependent?
5. Are state pixels transform-congruent across slots?
6. Can shared pixels be proven identical without harming fidelity?

Choose:

- **Full State Frame** when any state is a complete scene re-render or global change;
- **Layered State** when a genuinely shared background plus isolated overlays is faithful;
- **Procedural** only when the art is intentionally primitive/geometric and the existing/offline backend can express it;
- **Hybrid** when full/layered art still needs dynamic labels/glyphs, safe underlays, or occluders.

Document the reason. File size alone is not a sufficient reason to choose layered assets.

## 6. Required state matrix

For radial MVP, complete this matrix:

| State | Logical slot ID | Required | Full artwork asset | State overlay assets | Global changes | Dynamic visibility changes |
| --- | ---: | --- | --- | --- | --- | --- |
| `idle` | 0 | yes | | | | |
| `selected-1` | 1 | yes | | | | |

Continue through the target Layout Profile's final slot.

MVP validity requires `idle` plus every `selected-N`. Optional pressed/confirm/cancel/opening/closing animation is deferred. If an optional future state is proposed, explain why direct switching remains a complete fallback.

## 7. Dynamic text

Inventory all text that may vary by mapping, input family, locale, service state, settings, or runtime state.

Examples:

- action name;
- mapping text;
- keyboard shortcut;
- center prompt;
- status message;
- changing number.

For each item record:

| Element | Content key | States visible | Anchor ID | Max expected content | Localization risk | Overflow policy |
| --- | --- | --- | --- | --- | --- | --- |

Every text anchor must specify x, y, width, height, horizontal/vertical alignment, typography role, max lines, overflow (`ellipsis`, `shrink`, `clip`, or `hide`), minimum scale if shrinking, rotation, and state visibility.

**Do not bake any text that may need to change into static or state art.**

## 8. Dynamic glyphs and icons

Inventory:

- keyboard keys and shortcuts;
- DS4 face buttons, shoulders, triggers, D-pad, sticks, and system actions;
- generic action symbols;
- runtime window/control glyphs if the surface contains them.

For each input family choose:

- text/keycap rendering;
- closed runtime-symbol set;
- pre-rendered theme icon map;
- ordered fallback.

Do not require a new full-state frame for each user mapping. A pre-rendered theme icon map skins reusable standard IDs; keyboard shortcuts remain dynamic text.

Record worst-case strings such as `Ctrl+Alt+Shift+Win+K` and verify they fit or shrink predictably.

## 9. Safe text/glyph regions

Mark every region intended to host dynamic content. Record:

- anchor rectangle;
- background contrast at every state;
- minimum padding;
- maximum lines/string length;
- whether an opaque/alpha underlay is required;
- whether a foreground object intentionally occludes part of it;
- whether the region rotates or remains screen-aligned.

Safe regions should work for both unset and long mappings. If only one short English sample fits, the decomposition is incomplete.

## 10. Occlusion, masks, and depth order

Document the intended layer stack from back to front. Use the standard bands unless a reviewed reason requires otherwise:

```text
Z0  full artwork/base
Z10 theme decoration
Z20 selected-state art
Z25 safe dynamic underlay
Z30 dynamic glyph
Z40 dynamic label
Z45 foreground occluder
Z50 debug (production disabled)
```

For every mask, record:

- mask ID and source asset;
- purpose: compile clip, safe surface, debug region, or residue cover;
- layers/anchors it applies to;
- whether the compiler bakes it into output assets;
- whether runtime needs the mask file after compilation.

Prefer pre-baked PNG alpha and ordinary composition. Do not require runtime shaders for a reference-specific mask.

## 11. Runtime-only elements

Explicitly list elements that must always be runtime-owned:

- mapping/action label;
- keyboard/DS4 glyph when input can change;
- changing status/number;
- user name or localized prompt;
- pointer/debug overlay;
- command/control glyph whose behavior/state changes.

For each, specify content key, anchor/group, role, fallback, and cache invalidation triggers.

## 12. Baked-art elements

List every deliberately baked element and explain why it cannot change independently. Suitable examples include background buildings, fixed texture, decorative stars, and an approved static logo.

Unsuitable default baked elements include:

- action/mapping names;
- keyboard shortcuts;
- DS4 prompt icons;
- connection state;
- changing numbers;
- selected illumination when it differs by state but is baked only into Base;
- window close/minimize glyphs when runtime also draws them.

## 13. Baked Art Ownership Contract

Complete one row per named semantic element:

| Element ID | Description | Owner (`STATIC`/`DYNAMIC`/`STATE_ASSET`) | Layer ID | States/assets or content key | Verification method |
| --- | --- | --- | --- | --- | --- |

Rules:

1. Each Element ID appears exactly once.
2. `STATIC` references only static layers.
3. `DYNAMIC` references a supported content key and anchor group.
4. `STATE_ASSET` identifies every state asset that may own it.
5. The same glyph/text/control must not appear in both pixels and runtime content.
6. When full-state frames duplicate a STATIC environment for fidelity, semantic ownership remains STATE_ASSET for those frames unless the element is actually supplied by one shared layer. Do not claim two owners merely because pixels repeat.

The compiler can validate declarations and masks, not understand every pixel. Human review must check for undeclared baked residue.

## 14. Dreamscape-derived leakage checklist

Before approval, inspect at 100%, 150%, and 200% for:

- baked icon beneath a dynamic SVG/icon;
- baked pill beneath a dynamic status pill;
- baked window glyph beneath shared-shell glyph;
- wide inpaint rectangle with a visible boundary;
- dynamic control whose background is translucent enough to reveal old text;
- state art and dynamic layer both owning the same label;
- static art reloaded/replaced on each state rather than kept stable where intended;
- masks that cover legitimate surrounding sky/architecture;
- foreground decoration unintentionally placed behind dynamic text.

Dreamscape's asset builders removed measured dynamic rectangles by interpolation. That is a recovery technique for a composed reference, not an ownership model. V2 authoring should produce clean source layers whenever possible.

## 15. Selected-state description

For every selected slot, describe:

- local changes;
- global changes;
- state color/light center of gravity;
- shadow/reflection changes;
- path/architecture/character changes;
- which dynamic anchors become visible/hidden;
- expected visual emphasis without relying on a radial wedge.

The review must be able to distinguish an intentional global change from accidental drift.

## 16. Runtime boundaries

List what runtime does and does not do for this theme.

Runtime may:

- load, verify, decode, scale, cache, state-switch, alpha-compose, render constrained dynamic text/glyphs, and present;
- use the existing logical selected slot and mappings;
- fall back without changing input/business state.

Runtime may not:

- invent illustration/material;
- sample pixels to choose a slot;
- run theme-specific blur/shader/material code;
- execute scripts from a package;
- wait for animation before confirm/cancel;
- regenerate artwork for each mapping.

Any requested behavior outside this boundary must be reviewed as a separate generic capability, not hidden in a theme.

## 17. Asset export checklist

- [ ] Required state/layer filenames are normalized and local.
- [ ] Dimensions/mode/color space match the manifest.
- [ ] Alpha is genuine; no checkerboard/black pseudo-transparency.
- [ ] RGB is cleaned in alpha-zero pixels.
- [ ] No dynamic text/glyph/control is baked.
- [ ] State coverage is complete for every compatible layout.
- [ ] Shared layers are pixel-stable where claimed.
- [ ] Masks/underlays/occluders have explicit layer order.
- [ ] Glyph assets have runtime text/symbol fallback.
- [ ] Source assets and production exports are distinguishable.
- [ ] The compiler, not manual copying, creates hashes/build report/review sheets.

## 18. Reference Fidelity Review plan

Define required artifacts:

1. reference contain-fit view;
2. idle production frame;
3. every selected state;
4. state contact sheet;
5. individual layer sheet;
6. anchor/visual-region/ownership debug overlay;
7. static-only or masked comparison where appropriate;
8. runtime screenshots at required DPI;
9. 50% overlay and difference view;
10. memory and hot-path instrumentation report.

Automatic diff is advisory. Do not demand pixel-perfect equality for dynamic text, glyph fallback, font rasterization, or platform compositing. Human approval is the final fidelity authority.

## 19. Completion gate

The decomposition is complete only when:

- every requested question above has an answer;
- every logical slot has one semantic/state mapping;
- every visual element has one owner;
- every dynamic element has an anchor and fallback;
- state coverage and layer order are unambiguous;
- the chosen render strategy explains all global/local changes;
- runtime capability gaps are explicitly identified;
- a reviewer can predict every runtime-composed pixel category without seeing implementation code.
