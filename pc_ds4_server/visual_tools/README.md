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

## Supported Profiles

The current Toolchain accepts:

| Profile | Slots | Angles | Toolchain status | Runtime |
| --- | ---: | --- | --- | --- |
| `radial-6` | 6 | `0, 60, 120, 180, 240, 300` | stable | integrated |
| `radial-8` | 8 | `0, 45, 90, 135, 180, 225, 270, 315` | experimental | **not integrated** |

Both profiles use manifest version `1`, `canonical-transform`, and a
`1254x1254 RGBA` master. Profile and slot count are a strict pair:
`radial-6 + 6` and `radial-8 + 8` are valid; crossed combinations fail.

`radial-8` passing Validator, Generator, or Comparison means the offline
Toolchain contract is valid. It does not mean the current Runtime can load the
pack. Validator output explicitly reports `Runtime Capability Gap: EXISTS` and
`Runtime Compatible: NO`. See
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
