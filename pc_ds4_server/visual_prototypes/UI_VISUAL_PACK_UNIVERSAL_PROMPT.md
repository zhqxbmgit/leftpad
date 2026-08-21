# LeftPad UI Visual Pack Universal Prompt

## 1. Usage

Use this vendor-neutral prompt with `UI_VISUAL_PACK_GENERATION_SPEC.md` and the available files under `layout_profiles/`.

1. Replace the required placeholders.
2. Optionally provide a preferred Layout Profile.
3. Give the implementation agent read access to the specification, profiles, reference, and existing approved packs.
4. Give it a new writable review directory.
5. Treat the task as review-only until explicit Human Approval.

The agent must classify Interaction Topology before generating assets. A visual-generation task is not authorization to modify runtime selection or action logic.

## 2. Placeholders

| Placeholder | Required | Meaning | Example |
| --- | --- | --- | --- |
| `{{REFERENCE_IMAGE}}` | Yes | Full path to the visual reference | `C:\leftpad\references\new-ui.png` |
| `{{THEME_ID}}` | Yes | Stable machine-readable theme ID | `tactical-hex-v1` |
| `{{THEME_NAME}}` | Yes | Human-readable theme name | `Tactical Hex` |
| `{{OUTPUT_DIRECTORY}}` | Yes | New design/review output directory | `C:\leftpad\pc_ds4_server\visual_prototypes\themes\tactical-hex-v1` |
| `{{PREFERRED_LAYOUT_PROFILE}}` | No | Profile ID requested by the user; leave blank for automatic classification | `radial-6` |

Do not point `{{OUTPUT_DIRECTORY}}` at an approved theme unless replacement is separately authorized.

## 3. Copyable universal prompt

```text
Read UI_VISUAL_PACK_GENERATION_SPEC.md completely and treat it as the generic source of truth.

Inspect the available Layout Profiles under layout_profiles/ before choosing geometry or topology.

Reference image:
{{REFERENCE_IMAGE}}

Theme ID:
{{THEME_ID}}

Theme Name:
{{THEME_NAME}}

Preferred Layout Profile:
{{PREFERRED_LAYOUT_PROFILE}}

Output directory:
{{OUTPUT_DIRECTORY}}

Task:

Create a new LeftPad UI Visual Pack based on the reference image. This is a review-only visual-generation task. Do not perform runtime integration.

Phase 1 — reference and topology classification:

1. Inspect the reference's overall silhouette, layout topology, slot geometry, ordering, spacing, fixed structures, dynamic-content locations, and selection implications.
2. Distinguish Interaction Topology from Visual Appearance.
3. Classify the reference as either:
   - Visual-only variation of an existing profile; or
   - New Layout Profile required.
4. Inspect every relevant profile in layout_profiles/.
5. If {{PREFERRED_LAYOUT_PROFILE}} is non-empty, evaluate it but do not force it when the topology is incompatible.
6. Reuse an existing profile when slot IDs, ordering, positions, and Selection Model remain compatible.
7. If shapes change but Interaction Topology remains compatible, preserve the existing Selection Model and create only a new Visual Pack/layout implementation.
8. If Interaction Topology changes, do not modify runtime selection logic. Propose a new Layout Profile and produce a Runtime Capability Gap report, then stop for approval before runtime work.

Runtime Capability Gap report requirements when applicable:

- observed reference topology;
- proposed Layout Profile ID and layout type;
- required Selection Model;
- current runtime capability status;
- whether review-only assets can be generated safely first;
- separate runtime work that would be required;
- unresolved interaction questions and risks.

Phase 2 — visual analysis:

Analyze and document:

- overall silhouette;
- slot geometry and congruence;
- spacing and corner geometry;
- material and fill hierarchy;
- border hierarchy;
- shadow, highlight, reflection, glow, and bloom;
- selected-state distribution;
- center or other fixed structures;
- dynamic-content anchors and safe regions;
- visual hierarchy and contrast.

Preserve the reference's visual language. Do not copy weapon icons, shield icons, health items, numbers, cooldowns, labels, action names, controller prompts, mappings, or business-specific text unless explicitly requested as fixed decoration.

Phase 3 — generation:

- Follow UI_VISUAL_PACK_GENERATION_SPEC.md and the selected Layout Profile exactly.
- Do not modify stable input, selection, action, Keyboard, DS4, confirm/cancel, mapping, DoubleTap, MOVE, pulse, output-mode, Android, Diana, or TCP behavior.
- Do not modify existing approved themes or frozen assets.
- Use a deterministic offline raster generator.
- Prefer Python, Pillow, and the standard library.
- Use NumPy only if already available and useful.
- Do not force network installation of large dependencies.
- Prefer 4× supersampling; use the selected profile's master canvas and high-quality LANCZOS downsampling.
- Bake expensive Gaussian Blur, halo, bloom, shadow, and material compositing into offline assets.
- Keep dynamic business content out of static PNG assets.
- Prefer selectionAssetMode = canonical-transform when slot geometry is congruent.
- Use per-slot selected assets only when geometry genuinely differs and document why canonical transformation is invalid.
- Generate manifest and layout metadata without action mappings or input settings.
- Keep alpha-zero RGB clean and preserve declared transparent regions.

Production Visual Pack outputs:

- manifest.json;
- Base Layer asset;
- Selected asset set required by selectionAssetMode;
- layout metadata.

Design/review outputs:

- deterministic generator;
- Normal/Base Preview;
- representative Selected Preview;
- All Slots / All States Review;
- Reference Comparison with consistent background and scale;
- machine-readable verification.json;
- README or equivalent source metadata.

Verification:

- canvas size and mode;
- transparent outside region;
- asset bounding boxes;
- alpha-zero RGB residue;
- slot transform consistency;
- selected-state alignment;
- non-target areas unchanged;
- Base unchanged between state previews;
- dynamic-content safe regions;
- clipping;
- deterministic regeneration;
- SHA-256 fingerprints after freeze;
- every profile-specific invariant.

Approval boundary:

- Script PASS, Geometry PASS, Verification PASS, a generated Comparison, or AI-reported similarity are not Human Approval.
- Stop after review assets and verification are ready.
- Do not freeze production assets, integrate runtime, implement a Selection Engine, create a Theme Manager, or commit production assets without explicit authorization.
- Wait for explicit Human Visual Approval.

Final report format:

## A. Reference Analysis

## B. Layout Classification

Existing Profile:
or
New Profile Required:

## C. Interaction Topology

Visual-only change: Yes/No
Runtime Selection Change Required: Yes/No

## D. Geometry

## E. Material

## F. Assets

## G. Layout

## H. Manifest

## I. Verification

## J. Comparison

## K. Runtime Capability Gap

Use "None" when there is no gap.

## L. Git State

## M. Human Approval Gate

Stop after the report. Do not continue to runtime work or another visual revision unless explicitly requested.
```

## 4. Decision guide

| Observation | Classification | Required action |
| --- | --- | --- |
| Same slot IDs, order, positions, and Selection Model; only material changes | Visual-only change | Reuse profile; create new Visual Pack |
| Same topology; congruent slot silhouette changes | Visual-only change | Reuse or visually revise profile; preserve Selection Model |
| Same topology; non-congruent slots | Visual-only may still be possible | Profile may declare `per-slot`; document why |
| Different slot count or navigation geometry | Interaction Topology change | Propose new profile and Runtime Capability Gap |
| Preferred profile conflicts with reference topology | Interaction Topology mismatch | Do not force profile; explain mismatch |
| Runtime lacks required Selection Model | Runtime Capability Gap | Stop before runtime implementation |

## 5. Example 1 — new six-slot radial reference

Input summary:

```text
Reference: a new six-direction radial HUD
Preferred Layout Profile: radial-6
```

Expected classification:

```text
Existing Profile: radial-6
Visual-only change: Yes
Runtime Selection Change Required: No
Runtime Capability Gap: None
```

Action:

- read `layout_profiles/RADIAL_6_LAYOUT_PROFILE.md`;
- preserve six directions, slot order, `AngleSelection`, and approved geometry unless a visual deviation is explicitly reviewed;
- generate a new Visual Pack without changing `RadialSelectionEngine`;
- use the profile's `canonical-transform` strategy when cards remain congruent.

## 6. Example 2 — six independent hex cards in radial directions

Input summary:

```text
Reference: six congruent hex cards at Top, Upper Right, Lower Right,
Bottom, Lower Left, and Upper Left
Preferred Layout Profile: radial-6
```

Expected classification:

```text
Existing Profile: radial-6
Visual-only change: Yes
Runtime Selection Change Required: No
Runtime Capability Gap: None
```

Reasoning:

The card silhouette changed from wedge-like cards to hexagons, but Slot IDs, directional ordering, and six-sector `AngleSelection` remain compatible.

Action:

- preserve the existing Selection Model;
- create a new visual/layout implementation using six radial anchors;
- prefer one canonical hex Selected template plus per-slot rotation/translation transforms;
- do not modify or replace the Selection Engine.

## 7. Example 3 — 3×2 grid reference

Input summary:

```text
Reference: six buttons arranged as a 3×2 grid
Preferred Layout Profile: blank
```

Expected classification:

```text
Existing Profile: None compatible
New Profile Required: grid-3x2
Visual-only change: No
Runtime Selection Change Required: Potentially Yes
```

Required Runtime Capability Gap:

```text
Reference topology: 3 columns × 2 rows
Required Selection Model: GridSelection or another explicitly approved model
Current runtime capability: inspect and report; do not assume
Visual Pack first: review-only assets may be generated after profile approval
Runtime work: separate Selection Model and composer-capability task may be required
```

Action:

- propose `grid-3x2` with slot ordering, cell geometry, translations, anchors, and safe regions;
- do not force `radial-6` or pretend the grid is six angle sectors;
- do not implement `GridSelection` during visual generation;
- stop for approval after the profile proposal and Runtime Capability Gap report.

## 8. Condensed invocation example

```text
Reference image:
C:\leftpad\references\new-ui.png

Theme ID:
tactical-hex-v1

Theme Name:
Tactical Hex

Preferred Layout Profile:
radial-6

Output directory:
C:\leftpad\pc_ds4_server\visual_prototypes\themes\tactical-hex-v1

Read UI_VISUAL_PACK_GENERATION_SPEC.md and layout_profiles/. Classify Interaction Topology before generating anything. Reuse a compatible profile for visual-only changes. If topology changes, propose a new profile, report the Runtime Capability Gap, and do not modify runtime selection logic. Follow the offline 4× supersampling workflow, generate production and review artifacts, verify them, and stop for Human Approval before freeze or Runtime Integration.
```

The full copyable prompt remains the authoritative execution template.
