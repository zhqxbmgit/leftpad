# LeftPad UI Visual Pack Generation Specification

## 1. Purpose and precedence

This document defines the vendor-neutral, layout-independent process for creating LeftPad UI Visual Packs. It governs visual analysis, offline asset generation, metadata, verification, approval, freezing, and the boundary with runtime code.

This is the generic specification. A concrete layout must also have a Layout Profile. The Layout Profile owns topology and geometry details that do not apply to every UI shape.

When rules conflict, use this precedence:

1. stable interaction and action behavior;
2. this generic specification;
3. the selected Layout Profile;
4. theme-specific visual parameters approved for the reference;
5. implementation convenience.

A lower item must never silently override a higher one.

## 2. Core separation: interaction versus appearance

Interaction / Selection Topology answers **how a slot is selected**. Visual Appearance answers **what the slot looks like**. They are separate contracts.

```text
Stable Action Logic
Keyboard / DS4 / Confirm / Mapping
          │
          ▼
        Slot ID
          │
    ┌─────┴─────┐
    ▼           ▼
Selection     Visual Pack
Model         / Layout
    │           │
    └─────┬─────┘
          ▼
      UI Composer
```

A Visual Pack must not decide business actions. Static PNG assets must not perform hit testing. Visual gaps, transparent pixels, borders, shadows, and silhouettes do not define selectable regions.

Do not derive interaction behavior from:

- bitmap pixels;
- alpha values;
- rendered paths;
- glow or shadow bounds;
- visual-card bounding boxes.

Keyboard, DS4, confirm/cancel, mappings, DoubleTap, MOVE, pulse timing, and output mode remain in stable runtime logic.

## 3. Concept model

### 3.1 Visual Pack

A Visual Pack is a versioned collection of static visual layers plus metadata. It describes appearance, fixed decoration, layout anchors, and selected-state assets. It contains no business mappings.

### 3.2 Layout Profile

A Layout Profile defines a reusable geometry and topology contract. Every profile must describe at least:

- `layoutProfile` ID;
- layout type;
- slot count;
- slot IDs and ordering;
- slot visual anchors;
- per-slot transforms;
- declared Selection Model;
- master design-space canvas;
- geometry and spacing rules;
- dynamic-content anchors and safe regions;
- selected-asset transform strategy;
- compatibility with an existing runtime Selection Model;
- profile-specific verification.

### 3.3 Selection Model

A Selection Model is a capability description for converting user input into a Slot ID. Conceptual examples include:

- `AngleSelection`;
- `DirectionalSelection`;
- `LinearSelection`;
- `GridSelection`;
- `PointerRegionSelection`;
- `CustomSelection`.

A Layout Profile may declare a Selection Model, but visual-generation work must not implement or modify a Selection Engine unless a separate runtime task explicitly authorizes it.

### 3.4 UI Composer

The UI Composer is the compatible runtime composition layer. It loads a Visual Pack, applies profile-defined transforms, uses caches, composites fixed and selected layers, and draws dynamic content.

The target architecture is a shared Visual Pack / UI Composer architecture with reusable transform and composition capabilities. It is not a promise that one fixed radial renderer can render every possible layout.

## 4. Supported layout families

This specification supports, but is not limited to:

- Radial;
- Cross;
- Arc;
- Linear Horizontal;
- Linear Vertical;
- Grid;
- Hex / Honeycomb;
- Floating Cards;
- Ring of independent cards;
- Freeform fixed-slot layouts;
- future explicitly profiled layouts.

Example profile IDs:

```text
radial-6
cross-4
arc-5
linear-horizontal-6
linear-vertical-8
grid-3x2
hex-cluster-6
freeform-fixed-7
```

The generic specification does not prescribe a slot count, angles, radii, or canvas size. Those belong to the Layout Profile.

## 5. Classifying a new reference

Before generating assets, inspect the reference and classify both visual shape and Interaction Topology.

### Case A: Visual Shape Change Only

The existing slot identities, ordering, positions, and Selection Model remain compatible, while the visible shapes change. Examples include replacing wedges with circles, hexagons, floating rectangles, diamonds, or capsules at the same logical positions.

Required response:

- reuse the existing compatible Layout Profile when its contract permits the visual variation, or create a visual-layout revision of that profile;
- preserve the existing Selection Model;
- create only the new Visual Pack and required layout metadata;
- do not modify the Selection Engine.

### Case B: Interaction Topology Change

The reference changes how slots are arranged or navigated, for example a 3×2 grid, horizontal strip, cross, or arc with different navigation semantics.

Required response:

- do not pretend the old Selection Model still matches;
- propose a new Layout Profile;
- identify the Selection Model capability required;
- produce a Runtime Capability Gap report;
- do not implement new runtime selection logic during visual generation;
- stop for human approval before runtime work.

## 6. Runtime Capability Gap

When the reference needs a topology not supported by the current runtime, report:

- observed reference topology;
- proposed Layout Profile ID and layout type;
- required Selection Model;
- whether the current runtime already supports that model;
- whether review-only visual assets can be generated safely first;
- what separate runtime development would be needed;
- risks or unanswered interaction questions.

Do not force the reference into an incompatible profile to avoid reporting a gap. Do not silently create a Theme Manager, Selection Engine, layout parser, or runtime cache implementation.

## 7. Reference visual analysis

Analyze the reference before choosing material parameters. Record:

- overall silhouette;
- layout topology;
- slot geometry and congruence;
- spacing and gap behavior;
- corner geometry;
- material and fill hierarchy;
- border hierarchy;
- shadow and ambient occlusion;
- highlight and reflection behavior;
- glow, bloom, and selected-state energy distribution;
- center or other fixed structures;
- dynamic-content locations and safe regions;
- visual hierarchy and contrast.

Copy the visual language, not business content. Do not bake reference-specific weapon icons, shield icons, health items, numbers, cooldowns, action names, labels, controller prompts, or other business data unless the user explicitly requests them as fixed decoration.

## 8. Approved production workflow

```text
Reference Image
    ↓
Visual Analysis
    ↓
Layout/Profile Resolution
    ↓
Parameterized Geometry
    ↓
Offline Raster Generator
    ↓
4× supersampling
    ↓
Offline Material / Glow / Gaussian Blur
    ↓
High-quality Downsample
    ↓
RGBA Production Assets
    ↓
Layout Metadata
    ↓
Manifest
    ↓
Comparison
    ↓
Automated Verification
    ↓
Human Visual Approval
    ↓
Freeze
    ↓
Runtime Integration
```

Runtime integration is always a separate stage after approval and freeze.

## 9. Offline raster generator

Preferred implementation:

- Python;
- Pillow;
- standard library;
- NumPy only when already installed and useful for continuous gradients or verification.

Do not force network installation of large dependencies for an ordinary Visual Pack.

Use 4× supersampling by default. A Layout Profile defines the master design-space canvas. For example, a 1254×1254 master is rendered internally at 5016×5016. Other profiles may use different master sizes; multiply each dimension by four unless a documented quality or memory constraint justifies another factor.

Render masks, corners, fills, borders, shadows, halos, markers, and fixed decoration at the supersampled size. Downsample with LANCZOS or an equivalently reviewed high-quality filter.

Requirements:

- continuous gradients without visible banding;
- genuine RGBA transparency;
- no baked checkerboard or black pseudo-transparency;
- RGB cleared where final alpha equals zero;
- deterministic output when inputs and environment are unchanged.

## 10. Offline Gaussian Blur and runtime boundary

Offline generation may and should use Gaussian Blur, expensive compositing, supersampled shadows, halo construction, material gradients, and soft lighting when the visual reference requires them.

Normal interaction must not trigger runtime Gaussian Blur or repeated expensive material rendering. Complex halo, bloom, material, shadow, and soft-light work should be baked into assets whenever it is state-independent or transformable.

Runtime responsibilities are primarily:

- load;
- validate;
- scale;
- transform;
- cache;
- composite;
- draw dynamic glyphs and text.

## 11. Visual layer contract

### 11.1 Base Layer

Contains fixed appearance such as:

- background or fixed frame;
- Normal slots;
- center/HUD fixed decoration when the profile has one;
- fixed borders;
- fixed markers;
- other non-dynamic ornament.

It does not contain Selected state or action-specific content.

### 11.2 Dynamic Selection Layer

Contains Selected visual state such as:

- body illumination;
- selected fill or surface change;
- selected border hierarchy;
- localized halo or bloom;
- selected marker only when the profile contract makes it part of the selection layer.

### 11.3 Dynamic Content Layer

Drawn by runtime and not baked into theme PNGs:

- glyph;
- label;
- action name;
- action mapping;
- confirm/cancel prompts;
- changing numbers, cooldowns, or state;
- platform-specific button prompts.

## 12. Selected asset modes

Every Layout Profile declares `selectionAssetMode`.

### Preferred: `canonical-transform`

Use one canonical Selected template plus a profile-defined transform per slot when slot geometry is congruent.

Examples:

- rotation around a shared center;
- translation along a horizontal or vertical axis;
- X/Y translation in a grid;
- translation plus rotation or reflection when verified safe.

Cache each transformed state after theme load or scale change.

### Exception: `per-slot`

Use separate selected assets only when slots genuinely differ in size, shape, masking, or material treatment and cannot be represented by a verified transform.

The profile must document why canonical transformation is invalid. Do not choose `per-slot` merely because it is convenient.

## 13. Visual Pack structure and filename compatibility

Recommended future structure:

```text
ui_visual_packs/
  <theme-id>/
    manifest.json
    base.png
    selected.png
    layout.json
```

Existing product-specific directories such as `radial_assets/` remain valid. This specification does not authorize repository renames.

Design/review artifacts may include:

```text
generator.py
reference.png or a documented reference pointer
comparison.png
normal-preview.png
representative-selected-preview.png
all-states-review.png
verification.json
README.md
```

Filenames are not the Runtime Contract. `manifest.json` supplies the actual file paths. A frozen asset named `radial-base-v5.png` can satisfy the Base Layer contract just as `base.png` can. Do not rename approved assets solely to make them match a preferred convention.

## 14. Manifest model

Recommended future-compatible manifest:

```json
{
  "id": "theme-id",
  "name": "Human Readable Name",
  "version": 1,
  "layoutProfile": "radial-6",
  "slotCount": 6,
  "base": "base.png",
  "selected": "selected.png",
  "layout": "layout.json"
}
```

The manifest may add versioned visual capabilities, but it must not contain:

- keyboard or DS4 mappings;
- DoubleTap settings;
- business actions;
- selection thresholds;
- output mode;
- controller behavior.

## 15. Generic asset contracts

### 15.1 Base asset

- Master RGBA dimensions are defined by the Layout Profile.
- Canvas areas outside the fixed UI are transparent unless the profile explicitly defines an opaque backdrop.
- Fixed visuals are present and dynamic business content is absent.
- State previews must use the exact frozen Base asset without repainting unrelated areas.

### 15.2 Selected asset set

- Follows the profile's `selectionAssetMode`.
- Contains only the selected-state increment or replacement visual defined by the profile.
- Unused areas are transparent.
- Must not alter non-target slots or fixed structures unless explicitly declared by the profile.

### 15.3 Layout metadata

Contains visual/profile data such as:

- profile ID and layout type;
- canvas and coordinate system;
- slot IDs and ordering;
- transforms and anchors;
- dynamic-content safe regions;
- selected-asset mode;
- declared Selection Model compatibility.

It must not contain business actions or input mappings.

## 16. Comparison contract

Before Human Approval, generate:

- Reference view;
- Normal/Base Preview;
- one representative Selected Preview;
- All Slots / All States Review;
- Comparison Sheet.

Use a consistent review background, panel scale, and framing where practical. The comparison must make silhouette, spacing, material, selected-state energy, clipping, and visual hierarchy easy to judge.

## 17. Generic verification

Automated verification must cover at least:

- canvas size and expected mode;
- RGBA or explicitly declared opaque output;
- transparent outside region;
- asset bounding boxes;
- unexpected RGB in alpha-zero pixels;
- slot transform consistency;
- selected-state alignment;
- non-target areas unchanged;
- Base unchanged between state previews;
- dynamic-content safe regions;
- asset and halo clipping;
- generator reproducibility;
- SHA-256 fingerprints after freeze.

Each Layout Profile adds profile-specific checks such as spacing, gap geometry, angle alignment, grid-cell alignment, or unique-slot bounds.

Verification should be machine-readable and should fail generation when a required invariant is false.

## 18. Human Approval gate

None of the following constitutes final approval:

- script PASS;
- geometry PASS;
- verification PASS;
- comparison generated;
- an AI reporting visual similarity.

Explicit Human Visual Approval is required before:

- freezing production assets;
- replacing a stable theme;
- committing production assets;
- Runtime Integration.

## 19. Runtime performance contract

Theme loading follows:

```text
load pack
    ↓
read manifest
    ↓
read layout/profile metadata
    ↓
scale for current display
    ↓
build caches
    ↓
render
```

A theme or display-scale change may rebuild caches once. Normal interaction must not reload master PNGs, repeatedly high-quality-scale master assets, rerun Gaussian Blur, or repeat complex material composition every frame.

For `canonical-transform`, precompute transformed Selected layers for every slot. For `per-slot`, load and scale the declared slot assets once.

## 20. Theme and Visual Pack switching

Long-term architecture:

```text
one shared UI Composer / compatible renderer layer
+ multiple Visual Packs
+ explicit Layout Profiles
+ stable action logic
+ compatible Selection Models
```

Do not create a complete renderer, Selection Engine, or action pipeline for each theme. A completely different layout type may require new generic composition or Selection Model capability, but that capability must be a separately reviewed runtime feature rather than theme-specific hidden logic.

## 21. Deprecated / Avoid Unless Explicitly Requested

- runtime Gaussian Blur;
- repeated runtime material or shadow rendering;
- manually producing multiple assets for slots that are transform-congruent;
- integrating into runtime before comparison and verification;
- repeated whole-image generation that causes geometry drift;
- baking business icons, text, mappings, or prompts into static theme assets;
- using visual hitboxes or alpha as interaction logic;
- freezing without Human Approval;
- rewriting a renderer or Selection Engine for every theme;
- renaming or regenerating a frozen pack only for convention consistency.

Layout-specific deprecated techniques belong in the relevant Layout Profile, not in this generic list.

## 22. Existing repository versus an independent environment

Inside the LeftPad repository:

1. inspect approved Visual Packs and generators;
2. inspect `layout_profiles/`;
3. reuse a compatible profile when Interaction Topology matches;
4. preserve stable themes and runtime code during review;
5. create a new review directory;
6. report any Runtime Capability Gap.

In an independent environment, this specification defines the generic process. Select or author a Layout Profile before fixing concrete geometry.

## 23. Freeze and handoff

After Human Approval:

1. regenerate in isolation;
2. compare output byte-for-byte or explain deterministic metadata differences;
3. record SHA-256 fingerprints;
4. freeze generator, assets, metadata, reviews, and verification;
5. create a focused asset checkpoint when authorized;
6. schedule Runtime Integration as a separate task.

Do not combine unapproved visual experiments or unrelated runtime changes with the frozen asset checkpoint.
