#!/usr/bin/env python3
"""Deterministic package, alpha, registration, and locality checks for the spike."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image


SPIKE = Path(__file__).resolve().parent
PACKAGE = SPIKE / "package"
REFERENCE = SPIKE.parents[3] / "reference_inputs" / "reference-dark-fantasy-radial8.png"
REPORT = SPIKE / "working" / "verification-report.json"
STATE_NAMES = ["idle", *[f"selected-{slot}" for slot in range(1, 9)]]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main() -> None:
    manifest = json.loads((PACKAGE / "manifest.json").read_text(encoding="utf-8"))
    assert list(manifest["states"]) == STATE_NAMES
    assert manifest["dynamicAnchors"] == [] and manifest["masks"] == []
    assert manifest["capabilities"]["required"] == ["fullStateFrame", "instantTransitions"]
    assert manifest["layers"] == [{
        "id": "stateFrame", "kind": "stateAsset", "zIndex": 0,
        "bounds": {"x": 0, "y": 0, "width": 1254, "height": 1254},
        "visibleStates": ["all"], "ownership": "STATE_ASSET", "required": True,
    }]

    reference = np.asarray(Image.open(REFERENCE).convert("RGB"), dtype=np.uint8)
    images: dict[str, np.ndarray] = {}
    states: dict[str, object] = {}
    for name in STATE_NAMES:
        path = PACKAGE / "states" / f"{name}.png"
        with Image.open(path) as source:
            source.verify()
        image = Image.open(path)
        assert image.mode == "RGBA" and image.size == (1254, 1254)
        array = np.asarray(image, dtype=np.uint8)
        images[name] = array
        alpha = array[..., 3]
        transparent = alpha == 0
        assert np.all(array[..., :3][transparent] == 0)
        expected = manifest["assetHashes"][f"states/{name}.png"]
        assert sha256(path) == expected
        states[name] = {
            "mode": image.mode,
            "size": list(image.size),
            "sha256": expected,
            "alphaMinMax": [int(alpha.min()), int(alpha.max())],
            "transparentPixels": int(transparent.sum()),
            "partialAlphaPixels": int(((alpha > 0) & (alpha < 255)).sum()),
            "alphaZeroRgbResiduePixels": int(np.any(array[..., :3][transparent] != 0, axis=1).sum()),
            "contentBounds": list(image.getbbox() or ()),
        }

    idle = images["idle"]
    locality: dict[str, object] = {}
    for name in STATE_NAMES[1:]:
        candidate = images[name]
        changed = np.any(candidate != idle, axis=2)
        yy, xx = np.nonzero(changed)
        locality[name] = {
            "changedPixelsVsIdle": int(changed.sum()),
            "changedBoundsVsIdle": [int(xx.min()), int(yy.min()), int(xx.max() + 1), int(yy.max() + 1)],
            "unchangedPixelsVsIdle": int((~changed).sum()),
        }

    selected1 = images["selected-1"]
    opaque = selected1[..., 3] == 255
    selected1_error = np.abs(selected1[..., :3].astype(np.int16) - reference.astype(np.int16))
    selected1_opaque_mae = float(selected1_error[opaque].mean())
    assert selected1_opaque_mae == 0.0

    runtime: dict[str, object] = {}
    for name in ("idle", "selected-1", "selected-3", "selected-5", "selected-8"):
        path = SPIKE / "review" / f"runtime-200pct-{name}.png"
        image = Image.open(path).convert("RGBA")
        assert image.size == (840, 840)
        alpha = np.asarray(image, dtype=np.uint8)[..., 3]
        assert alpha[0, 0] == 0 and alpha[-1, -1] == 0
        runtime[name] = {
            "size": list(image.size),
            "cornerAlpha": [int(alpha[0, 0]), int(alpha[0, -1]), int(alpha[-1, 0]), int(alpha[-1, -1])],
            "sha256": sha256(path),
        }

    report = {
        "result": "PASS",
        "stateCount": len(STATE_NAMES),
        "layerCount": len(manifest["layers"]),
        "staticLayerCount": sum(layer["kind"] == "staticAsset" for layer in manifest["layers"]),
        "assetHashClosure": "exact",
        "selected1OpaqueRgbMaeVsReference": selected1_opaque_mae,
        "states": states,
        "locality": locality,
        "runtime200Percent": runtime,
    }
    REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
