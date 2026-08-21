# Approved radial V5 visual baseline

This directory is the approved, reproducible design-master pipeline for the
six-slot radial menu. It is intentionally not connected to the runtime yet.

## Rebuild

Run `build_radial_raster_v5.py` with Python, Pillow, and NumPy. The generator
renders at 5016 x 5016 (4x supersampling), downsamples with LANCZOS, and writes
the review, layout, and verification artifacts in this directory.

The comparison sheet also reads the design reference one directory above:
`ChatGPT Image 2026年8月19日 12_55_27.png`. That reference is not a production
runtime asset and its Git inclusion is intentionally left for a separate
decision.

Use `build_radial_raster_v5.py --verify-only` to validate the frozen files
against the SHA-256 values recorded in `radial-raster-v5-verification.json`.

## Freeze constraints

- Canvas: 1254 x 1254 RGBA
- Geometry: six slots, 450/198 radii, 16 px parallel gap, 10 px corners
- Runtime integration: none
- Approved version: V5; do not iterate to V6 without explicit review
