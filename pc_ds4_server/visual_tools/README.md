# LeftPad Visual Pack Tools

`validate_visual_pack.py` is an offline developer tool that checks an AI- or
designer-produced Visual Pack against a documented Toolchain profile. It
reports Toolchain validity separately from Runtime compatibility; it does not
generate assets, load themes into the Runtime, or modify an existing pack.

## Requirements

- Python 3.10 or newer
- Pillow available in the selected Python environment

The validator uses the Python standard library plus Pillow. It does not install
dependencies or access the network. If Pillow is unavailable, validation stops
with an explicit dependency error.

## Run

From `pc_ds4_server/visual_tools`:

```powershell
python .\validate_visual_pack.py <VisualPackDirectory>
```

Example using the production V5 pack:

```powershell
python .\validate_visual_pack.py `
  C:\leftpad\pc_ds4_server\PcDs4Server\Assets\UIVisualPacks\radial-v5
```

The process exits with code `0` for a compatible pack and code `1` for a
validation failure.

## Comparison Tool

`compare_visual_pack.py` creates a standardized, opaque review sheet for human
approval. It validates the Visual Pack before opening or writing comparison
output. A failed manifest, missing asset, or other Validator failure therefore
cannot produce a misleading review image.

From `pc_ds4_server/visual_tools`:

```powershell
python .\compare_visual_pack.py `
  <ReferenceImage> `
  <VisualPackDirectory>
```

The default output is `<VisualPackDirectory>/comparison.png`. Use `--output`
when the source pack must remain untouched:

```powershell
python .\compare_visual_pack.py `
  C:\reviews\reference.png `
  C:\leftpad\pc_ds4_server\PcDs4Server\Assets\UIVisualPacks\radial-v5 `
  --output C:\reviews\radial-v5-comparison.png
```

The generated RGB PNG uses the fixed `#080B10` review background and contains:

1. the complete reference image using contain fit with no crop;
2. the manifest-resolved Base asset over the review background;
3. a Slot 2 Selected preview rotated 60 degrees clockwise around `wheelCenter`;
4. a six- or eight-thumbnail review of every slot for rotation, alignment,
   halo, and clipping inspection.

The header includes only Theme ID, Theme Name, Layout Profile, and Version.
The tool does not calculate similarity scores, judge visual quality, modify
colors, or alter source assets. Final approval remains a human decision.

## Generator Wrapper

`generate_visual_pack.py` packages existing designer or AI-exported files into
the standard Visual Pack structure. It does not draw, recolor, redesign, or
install a theme. Source assets are copied byte-for-byte and their SHA-256 values
are written to `verification.json`.

Expected workspace:

```text
workspace/
  theme-config.json
  reference.png
  assets/
    radial-base.png
    radial-selected-card.png
    radial-layout.json
```

Run from `pc_ds4_server/visual_tools`:

```powershell
python .\generate_visual_pack.py C:\reviews\workspace\theme-config.json
```

The default output is `workspace/output`. Available options:

- `--output <directory>` selects another output directory.
- `--force` replaces an existing output only after a complete staging build
  succeeds.
- `--skip-comparison` omits `comparison.png`.
- `--skip-validation` explicitly bypasses the default Validator gate. This is
  never enabled by default; Comparison Tool validation still applies when a
  comparison is requested.

The normal flow is:

```text
Config
  ↓
Generate manifest + copy byte-identical assets + write SHA-256 verification
  ↓
Validate
  ↓
Compare (when a reference exists)
  ↓
Human Approval
```

If the configured reference is absent, generation succeeds with a warning and
does not create `comparison.png`. Existing output is rejected unless `--force`
is supplied. See `examples/radial-6-theme-config.json` for the supported config
shape.

## UI Theme Package V2 contract (Phase 0)

V2 is currently a schema, documentation, and independent validation contract;
it is not connected to runtime discovery or rendering. The normative files are:

- `schemas/ui-theme-v2.schema.json` — closed Draft 2020-12 manifest schema;
- `THEME_RUNTIME_PROTOCOL_V2.md` — state, layer, anchor, glyph, ownership,
  fallback, capability, path, hash, and V1/V2 compatibility semantics;
- `REFERENCE_THEME_DECOMPOSITION_SPEC.md` — reference-to-package authoring rules;
- `examples/reference-theme-v2.example.json` — non-production manifest fixture;
- `validate_theme_v2.py` — schema/semantic validator with optional asset hashing.

Manifest-only validation does not require example PNG files:

```powershell
python validate_theme_v2.py examples/reference-theme-v2.example.json
```

Use `--check-assets` for a production candidate. Capability-context validation
is enabled by repeating `--supported-capability`; otherwise the tool validates
portable package coherence without claiming current V2 runtime support. V1
validation and generation commands below remain unchanged.

## Universal Radial Protocol V3 contract (Phase 0)

V3 is a new, independent contract for Universal radial settings and semantic
theme ownership. It does not alter the V1 or V2 validators or Runtime paths.
The normative Phase 0 files are:

- UNIVERSAL_RADIAL_PROTOCOL_V3.md — CRU, product setting, migration,
  semantic ownership, geometry, sampling, compiler, fidelity, and
  compatibility requirements;
- schemas/ui-theme-v3.schema.json — closed Draft 2020-12 package shape;
- examples/reference-theme-v3.example.json — non-production radial-8
  manifest-only example;
- validate_theme_v3.py — independent schema/semantic/package validator;
- universal_radial_settings_migration.py — isolated, non-production prototype
  of the frozen Base/Scale and legacy-alpha migration formulas;
- tests/test_validate_theme_v3.py — real temporary radial-6/radial-8 package
  fixtures, rejection gates, and isolated settings-migration formula tests.

Manifest-only contract validation:

```powershell
python .\validate_theme_v3.py .\examples\reference-theme-v3.example.json
```

A compiler-produced package candidate must also pass file containment, PNG
metadata/dimensions, and SHA-256 verification:

```powershell
python .\validate_theme_v3.py C:\reviews\universal-v3\manifest.json --check-assets
```

V3 authoring is reference artwork to reviewed decomposition to semantic assets
and explicit masks to a validated package. Runtime decomposition and
image-analysis ownership guessing are outside the contract.

## Universal Radial V3 Phase 3 selected-emphasis compiler

`build_selected_emphasis_companion.py` is the offline authority compiler for
the internal Phase 3 `SELECTED_EMPHASIS` migration checkpoint. It reads only
the three frozen formal themes (`radial-v5`, `radial-8-minimal-v1`, and
`dark-fantasy-radial8-v1`) and writes companions beneath
`../visual_prototypes/universal_radial_v3/phase3-selected-emphasis/`.

```powershell
python .\build_selected_emphasis_companion.py
```

For V1, `render_selected_emphasis_v1.ps1` uses the same System.Drawing scale,
slot rotation, and SourceOver composition as the legacy renderer. For V2, the
compiler consumes the frozen idle and per-slot production state art directly.
The compiler emits:

- a reviewable base PNG and one full-selected PNG per slot;
- deterministic zlib-compressed exact BGRA `Format32bppPArgb` sidecars,
  because a straight-alpha PNG cannot losslessly represent every composed
  antialiasing value;
- explicit binary 0/255 masks calculated once from exact PArgb endpoints;
- SHA-256 identities, endpoint/intermediate raw-pixel hashes, mask coverage,
  bounds, and connected-component diagnostics;
- one review sheet per theme with base, selected, mask overlay, strengths
  255/128/0, checkerboard, and difference heatmap panels.

Runtime does not compare base and selected art. It verifies and consumes the
compiler-owned source, PArgb, and mask assets. Re-running the compiler is
byte-deterministic and does not modify any V1/V2 production asset or manifest.

## Supported Profiles

The current Toolchain accepts:

| Profile | Slots | Angles | Toolchain status | Runtime |
| --- | ---: | --- | --- | --- |
| `radial-6` | 6 | `0, 60, 120, 180, 240, 300` | stable | integrated |
| `radial-8` | 8 | `0, 45, 90, 135, 180, 225, 270, 315` | stable | integrated |

Both profiles use manifest version `1`, `canonical-transform`, and a
`1254x1254 RGBA` master. Profile and slot count are a strict pair:
`radial-6 + 6` and `radial-8 + 8` are valid; crossed combinations fail.

`radial-8` is supported by both the offline Toolchain and the current Runtime.
Validator output reports `Runtime Capability Gap: NONE` and
`Runtime Compatible: YES`. See
`../visual_prototypes/layout_profiles/RADIAL_8_LAYOUT_PROFILE.md`.

## Validation rules

The validator checks:

- required manifest fields and supported values;
- duplicate JSON fields and invalid JSON/types;
- manifest-resolved local filenames instead of assuming fixed asset names;
- profile-specific radial canvas, center, geometry fields, slots, angles, and
  anchors;
- Base and Selected files are valid `1254x1254` RGBA PNG images;
- transparent canvas background with no baked full-canvas black/checkerboard;
- no RGB residue in pixels whose alpha is zero;
- optional SHA-256 records in `verification.json`, when present.

Missing `verification.json`, or missing hash records within it, produces a
warning rather than a failure. A recorded hash mismatch is a failure. The JSON
schemas in `schemas/` document the accepted manifest and layout structures;
the validator implements the checks directly and does not require `jsonschema`.

## Tests

From `pc_ds4_server/visual_tools`:

```powershell
python -m unittest discover -s tests -v
```

Tests create temporary fixtures and never modify Runtime production assets.

## Example output

Valid pack:

```text
================================
VISUAL PACK VALID
================================

ID:
radial-v5

Profile:
radial-6

Runtime Compatible:
YES
```

Invalid pack:

```text
================================
VISUAL PACK INVALID
================================

Reason:
selected asset missing: selected.png

Runtime Compatible:
NO
```
