# Imagegen cleanup donor

Mode: built-in imagegen, precise-object-edit.

Input: a read-only inspection copy of Spike01 `states/idle.png`.

Prompt intent:

- Remove only all eight outer baked action icons and labels.
- Remove the central sword, `SWORD STANCE`, `Balanced`, and description.
- Restore adjacent aged blackened metal, bronze/gold edging, engraved runes,
  fine cracks, wear, local highlights, bezel shading, and central plate texture.
- Preserve the exact wheel silhouette, slot and central geometry, dark-metal
  lighting, and top amber selected glow.
- Add no background, environment, text, icons, glyphs, or new elements.
- Avoid flat patches, rectangles, blurred blobs, clone seams, modern UI, and
  generic fantasy redesign.

The built-in result is RGB with a checkerboard baked outside the wheel, so it
is never used as a complete state. `build_spike_assets.py` uses it only as an
RGB texture donor inside fixed semantic cleanup masks. Alpha, silhouette,
non-semantic pixels, and selected-light deltas remain derived from tracked
Spike01 state artwork.
