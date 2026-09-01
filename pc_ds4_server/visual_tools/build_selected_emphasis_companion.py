#!/usr/bin/env python3
"""Build the Universal Radial V3 Phase 3 SELECTED_EMPHASIS companion.

This is an offline authoring compiler.  It is the only place where frozen base
and full-selected artwork are compared to establish initial semantic support.
Runtime code loads the emitted masks and hashes; it does not infer ownership.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import tempfile
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

import numpy as np
from PIL import Image, ImageDraw, ImageFont


MASTER_SIZE = 1254
SCRIPT_ROOT = Path(__file__).resolve().parent
SERVER_ROOT = SCRIPT_ROOT.parent
ASSET_ROOT = SERVER_ROOT / "PcDs4Server" / "Assets"
DEFAULT_OUTPUT = (
    SERVER_ROOT
    / "visual_prototypes"
    / "universal_radial_v3"
    / "phase3-selected-emphasis"
)
V1_RENDERER = SCRIPT_ROOT / "render_selected_emphasis_v1.ps1"
PARGB_EXPORTER = SCRIPT_ROOT / "export_selected_emphasis_pargb.ps1"


@dataclass(frozen=True)
class ThemeAuthority:
    theme_id: str
    protocol_version: int
    package_revision: int
    slot_count: int
    kind: str
    root: Path
    review_slots: tuple[int, ...]
    color_mode: str


THEMES = {
    "radial-v5": ThemeAuthority(
        "radial-v5", 1, 1, 6, "v1-canonical",
        ASSET_ROOT / "UIVisualPacks" / "radial-v5", (1, 3, 4, 6), "RGBA"
    ),
    "radial-8-minimal-v1": ThemeAuthority(
        "radial-8-minimal-v1", 1, 1, 8, "v1-canonical",
        ASSET_ROOT / "UIVisualPacks" / "radial-8-minimal-v1", (1, 3, 5, 8), "RGBA"
    ),
}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def sha256_file(path: Path) -> str:
    return sha256_bytes(path.read_bytes())


def json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def save_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(json_bytes(value))


def save_png(path: Path, image: Image.Image) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=False, compress_level=9)


def rgba_array(image: Image.Image) -> np.ndarray:
    return np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()


def to_pargb(rgba: np.ndarray) -> np.ndarray:
    values = rgba.astype(np.uint16)
    alpha = values[..., 3:4]
    rgb = (values[..., :3] * alpha + 127) // 255
    return np.concatenate((rgb, alpha), axis=2).astype(np.uint8)


def pargb_to_rgba(pargb: np.ndarray) -> np.ndarray:
    values = pargb.astype(np.uint32)
    alpha = values[..., 3:4]
    divisor = np.maximum(alpha, 1)
    rgb = np.minimum(255, (values[..., :3] * 255 + divisor // 2) // divisor)
    rgb[alpha[..., 0] == 0] = 0
    return np.concatenate((rgb, alpha), axis=2).astype(np.uint8)


def difference_mask(base_rgba: np.ndarray, selected_rgba: np.ndarray) -> np.ndarray:
    return difference_mask_pargb(to_pargb(base_rgba), to_pargb(selected_rgba))


def difference_mask_pargb(base: np.ndarray, selected: np.ndarray) -> np.ndarray:
    changed = np.any(base != selected, axis=2)
    return np.where(changed, 255, 0).astype(np.uint8)


def interpolate_pargb(
    base_rgba: np.ndarray,
    selected_rgba: np.ndarray,
    mask: np.ndarray,
    strength: int,
) -> np.ndarray:
    if not 0 <= strength <= 255:
        raise ValueError("strength must be in [0, 255]")
    if base_rgba.shape != selected_rgba.shape or mask.shape != base_rgba.shape[:2]:
        raise ValueError("base, selected, and mask geometry must match")
    if not np.all((mask == 0) | (mask == 255)):
        raise ValueError("Phase 3 masks are binary 0/255 authority")
    return interpolate_pargb_arrays(
        to_pargb(base_rgba), to_pargb(selected_rgba), mask, strength
    )


def interpolate_pargb_arrays(
    base_pargb: np.ndarray,
    selected_pargb: np.ndarray,
    mask: np.ndarray,
    strength: int,
) -> np.ndarray:
    if not 0 <= strength <= 255:
        raise ValueError("strength must be in [0, 255]")
    if base_pargb.shape != selected_pargb.shape or mask.shape != base_pargb.shape[:2]:
        raise ValueError("base, selected, and mask geometry must match")
    if not np.all((mask == 0) | (mask == 255)):
        raise ValueError("Phase 3 masks are binary 0/255 authority")
    base = base_pargb.astype(np.uint32)
    selected = selected_pargb.astype(np.uint32)
    result = base.copy()
    owned = mask == 255
    blended = (base * (255 - strength) + selected * strength + 127) // 255
    result[owned] = blended[owned]
    return result.astype(np.uint8)


def raw_pargb_sha(rgba: np.ndarray) -> str:
    # Runtime lockbits expose BGRA PArgb.  Record that exact channel convention.
    pargb = to_pargb(rgba)
    bgra = pargb[..., [2, 1, 0, 3]]
    return sha256_bytes(bgra.tobytes(order="C"))


def raw_pargb_array_sha(pargb: np.ndarray) -> str:
    bgra = pargb[..., [2, 1, 0, 3]]
    return sha256_bytes(bgra.tobytes(order="C"))


def load_pargb(path: Path) -> np.ndarray:
    data = path.read_bytes()
    if len(data) < 12 or data[:4] != b"PARG":
        raise ValueError(f"invalid PArgb sidecar: {path}")
    width = int.from_bytes(data[4:8], "little")
    height = int.from_bytes(data[8:12], "little")
    if width != MASTER_SIZE or height != MASTER_SIZE or len(data) != 12 + width * height * 4:
        raise ValueError(f"invalid PArgb sidecar geometry: {path}")
    bgra = np.frombuffer(data, dtype=np.uint8, offset=12).reshape((height, width, 4))
    return bgra[..., [2, 1, 0, 3]].copy()


def save_pargbz(path: Path, source: Path) -> None:
    raw = source.read_bytes()
    if len(raw) < 12 or raw[:4] != b"PARG":
        raise ValueError(f"invalid PArgb sidecar: {source}")
    width = int.from_bytes(raw[4:8], "little")
    height = int.from_bytes(raw[8:12], "little")
    if len(raw) != 12 + width * height * 4:
        raise ValueError(f"invalid PArgb sidecar geometry: {source}")
    payload = b"PARGZ" + raw[4:12] + zlib.compress(raw[12:], level=9)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


class _RunUnion:
    def __init__(self) -> None:
        self.parent: list[int] = []

    def add(self) -> int:
        node = len(self.parent)
        self.parent.append(node)
        return node

    def find(self, node: int) -> int:
        while self.parent[node] != node:
            self.parent[node] = self.parent[self.parent[node]]
            node = self.parent[node]
        return node

    def union(self, left: int, right: int) -> None:
        a, b = self.find(left), self.find(right)
        if a != b:
            self.parent[max(a, b)] = min(a, b)


def connected_component_summary(mask: np.ndarray) -> dict[str, Any]:
    """Return deterministic 8-connected diagnostics using row runs."""
    union = _RunUnion()
    runs: list[tuple[int, int, int, int]] = []  # y, start, end inclusive, node
    previous: list[tuple[int, int, int]] = []
    for y, row in enumerate(mask == 255):
        padded = np.pad(row.astype(np.int8), (1, 1))
        changes = np.diff(padded)
        starts = np.flatnonzero(changes == 1)
        ends = np.flatnonzero(changes == -1) - 1
        current: list[tuple[int, int, int]] = []
        prior_index = 0
        for start, end in zip(starts.tolist(), ends.tolist(), strict=True):
            node = union.add()
            while prior_index < len(previous) and previous[prior_index][1] < start - 1:
                prior_index += 1
            candidate = prior_index
            while candidate < len(previous) and previous[candidate][0] <= end + 1:
                union.union(node, previous[candidate][2])
                candidate += 1
            current.append((start, end, node))
            runs.append((y, start, end, node))
        previous = current

    components: dict[int, dict[str, int]] = {}
    for y, start, end, node in runs:
        root = union.find(node)
        item = components.setdefault(
            root,
            {"pixelCount": 0, "left": start, "top": y, "right": end, "bottom": y},
        )
        item["pixelCount"] += end - start + 1
        item["left"] = min(item["left"], start)
        item["top"] = min(item["top"], y)
        item["right"] = max(item["right"], end)
        item["bottom"] = max(item["bottom"], y)
    ordered = sorted(
        components.values(),
        key=lambda item: (-item["pixelCount"], item["top"], item["left"]),
    )
    return {
        "connectivity": 8,
        "count": len(ordered),
        "largest": ordered[:8],
    }


def mask_bounds(mask: np.ndarray) -> dict[str, int] | None:
    ys, xs = np.nonzero(mask == 255)
    if len(xs) == 0:
        return None
    return {
        "left": int(xs.min()),
        "top": int(ys.min()),
        "right": int(xs.max()),
        "bottom": int(ys.max()),
        "width": int(xs.max() - xs.min() + 1),
        "height": int(ys.max() - ys.min() + 1),
    }


def _read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def _run_powershell(command: list[str], label: str) -> None:
    completed = subprocess.run(
        command,
        cwd=SERVER_ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(
            f"{label} failed:\n" + (completed.stdout or "") + (completed.stderr or "")
        )


def _render_v1_endpoints(
    authority: ThemeAuthority,
    temporary: Path,
) -> tuple[Path, list[Path], Path, list[Path], dict[str, Any]]:
    manifest = _read_json(authority.root / "manifest.json")
    base = authority.root / manifest["base"]
    selected = authority.root / manifest["selected"]
    layout = authority.root / manifest["layout"]
    command = [
        "pwsh", "-NoProfile", "-File", str(V1_RENDERER),
        "-BasePath", str(base),
        "-SelectedPath", str(selected),
        "-LayoutPath", str(layout),
        "-OutputDirectory", str(temporary),
    ]
    _run_powershell(command, "V1 endpoint renderer")
    return (
        temporary / "base-static.png",
        [temporary / f"selected-{slot}-source.png" for slot in range(1, authority.slot_count + 1)],
        temporary / "base-static.pargb",
        [temporary / f"selected-{slot}-source.pargb" for slot in range(1, authority.slot_count + 1)],
        {
            "kind": "V1 base + canonical selected transform",
            "base": {"path": str(base.relative_to(SERVER_ROOT)).replace("\\", "/"), "sha256": sha256_file(base)},
            "canonicalSelected": {
                "path": str(selected.relative_to(SERVER_ROOT)).replace("\\", "/"),
                "sha256": sha256_file(selected),
            },
            "layout": {"path": str(layout.relative_to(SERVER_ROOT)).replace("\\", "/"), "sha256": sha256_file(layout)},
            "renderer": "render_selected_emphasis_v1.ps1/System.Drawing exact legacy transform",
            "dynamicContentExcluded": True,
        },
    )


def _dark_endpoints(
    authority: ThemeAuthority,
    temporary: Path,
) -> tuple[Path, list[Path], Path, list[Path], dict[str, Any]]:
    manifest_path = authority.root / "manifest.json"
    manifest = _read_json(manifest_path)
    if manifest["protocolVersion"] != 2 or manifest["packageRevision"] != 1:
        raise ValueError("Dark Fantasy frozen manifest identity changed")
    state_layer = next(layer for layer in manifest["layers"] if layer["id"] == "stateFrame")
    if state_layer["kind"] != "stateAsset" or state_layer["ownership"] != "STATE_ASSET":
        raise ValueError("Dark Fantasy static state ownership changed")
    dynamic_layers = [layer for layer in manifest["layers"] if layer["kind"].startswith("dynamic")]
    if not dynamic_layers or any(layer["ownership"] != "DYNAMIC" for layer in dynamic_layers):
        raise ValueError("Dark Fantasy dynamic ownership is not isolated from state artwork")
    base = authority.root / manifest["states"]["idle"]["assets"]["stateFrame"]
    selected = [
        authority.root / manifest["states"][f"selected-{slot}"]["assets"]["stateFrame"]
        for slot in range(1, authority.slot_count + 1)
    ]
    base_pargb = temporary / "base-static.pargb"
    selected_pargb = [
        temporary / f"selected-{slot}-source.pargb"
        for slot in range(1, authority.slot_count + 1)
    ]
    jobs_path = temporary / "pargb-export-jobs.json"
    jobs = [{"input": str(base), "output": str(base_pargb)}] + [
        {"input": str(source), "output": str(target)}
        for source, target in zip(selected, selected_pargb, strict=True)
    ]
    jobs_path.write_bytes(json_bytes(jobs))
    _run_powershell(
        ["pwsh", "-NoProfile", "-File", str(PARGB_EXPORTER), "-JobsPath", str(jobs_path)],
        "Dark Fantasy PArgb exporter",
    )
    return base, selected, base_pargb, selected_pargb, {
        "kind": "V2 frozen full-state static artwork",
        "manifest": {
            "path": str(manifest_path.relative_to(SERVER_ROOT)).replace("\\", "/"),
            "sha256": sha256_file(manifest_path),
        },
        "idle": {"path": str(base.relative_to(SERVER_ROOT)).replace("\\", "/"), "sha256": sha256_file(base)},
        "selected": [
            {"slotId": index + 1, "path": str(path.relative_to(SERVER_ROOT)).replace("\\", "/"), "sha256": sha256_file(path)}
            for index, path in enumerate(selected)
        ],
        "dynamicContentExcluded": True,
        "dynamicLayerCount": len(dynamic_layers),
    }


def _checkerboard(size: tuple[int, int], cell: int = 16) -> Image.Image:
    width, height = size
    y, x = np.indices((height, width))
    choice = ((x // cell) + (y // cell)) % 2
    pixels = np.where(choice[..., None] == 0, (50, 54, 62), (94, 99, 108)).astype(np.uint8)
    return Image.fromarray(pixels, "RGB")


def _composite_checker(image: Image.Image) -> Image.Image:
    checker = _checkerboard(image.size).convert("RGBA")
    return Image.alpha_composite(checker, image.convert("RGBA")).convert("RGB")


def _fit(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    copy = image.copy()
    copy.thumbnail(size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", size, "#151820")
    x = (size[0] - copy.width) // 2
    y = (size[1] - copy.height) // 2
    if copy.mode == "RGBA":
        background = _checkerboard(copy.size).convert("RGBA")
        background.alpha_composite(copy)
        copy = background.convert("RGB")
    else:
        copy = copy.convert("RGB")
    canvas.paste(copy, (x, y))
    return canvas


def _review_sheet(
    authority: ThemeAuthority,
    base: Image.Image,
    selected_images: list[Image.Image],
    masks: list[np.ndarray],
    base_pargb: np.ndarray,
    selected_pargb: list[np.ndarray],
) -> Image.Image:
    columns = (
        "BASE", "FULL SELECTED", "MASK OVERLAY", "HIGHLIGHT 255",
        "HIGHLIGHT 128", "HIGHLIGHT 0", "CHECKERBOARD", "DIFF HEATMAP",
    )
    tile = (228, 228)
    label_height = 34
    margin = 18
    header = 74
    row_height = tile[1] + label_height + 12
    sheet = Image.new(
        "RGB",
        (margin * 2 + len(columns) * tile[0], header + len(authority.review_slots) * row_height + margin),
        "#0B0E14",
    )
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=16)
    small = ImageFont.load_default(size=13)
    draw.text((margin, 14), f"{authority.theme_id} - SELECTED_EMPHASIS REVIEW", fill="#F0D59B", font=font)
    draw.text((margin, 40), "PArgb base to full-selected; binary explicit mask; strengths 255 / 128 / 0", fill="#B8C0CC", font=small)
    for row, slot in enumerate(authority.review_slots):
        selected = selected_images[slot - 1]
        mask = masks[slot - 1]
        half_pargb = interpolate_pargb_arrays(
            base_pargb, selected_pargb[slot - 1], mask, 128
        )
        half = Image.fromarray(pargb_to_rgba(half_pargb), "RGBA")
        mask_rgba = np.zeros_like(pargb_to_rgba(base_pargb))
        mask_rgba[..., 0] = 255
        mask_rgba[..., 3] = np.where(mask == 255, 150, 0).astype(np.uint8)
        overlay = Image.alpha_composite(base.convert("RGBA"), Image.fromarray(mask_rgba, "RGBA"))
        delta = np.max(
            np.abs(base_pargb.astype(np.int16) - selected_pargb[slot - 1].astype(np.int16)),
            axis=2,
        )
        heat = np.zeros_like(pargb_to_rgba(base_pargb))
        heat[..., 0] = np.minimum(255, delta * 3).astype(np.uint8)
        heat[..., 1] = np.minimum(255, delta).astype(np.uint8)
        heat[..., 3] = np.where(delta > 0, 255, 0).astype(np.uint8)
        panels = (
            base, selected, overlay, selected, half, base,
            _composite_checker(half), Image.fromarray(heat, "RGBA"),
        )
        y = header + row * row_height
        for column, (title, panel) in enumerate(zip(columns, panels, strict=True)):
            x = margin + column * tile[0]
            sheet.paste(_fit(panel, tile), (x, y))
            draw.text((x + 5, y + tile[1] + 7), f"SLOT {slot}  {title}", fill="#D7DCE5", font=small)
    return sheet


def _compile_theme(authority: ThemeAuthority, output_root: Path) -> dict[str, Any]:
    with tempfile.TemporaryDirectory(prefix=f"{authority.theme_id}-phase3-") as temporary_name:
        temporary = Path(temporary_name)
        if authority.kind == "v1-canonical":
            (base_source, selected_sources, base_pargb_source,
             selected_pargb_sources, source_authority) = _render_v1_endpoints(authority, temporary)
        else:
            (base_source, selected_sources, base_pargb_source,
             selected_pargb_sources, source_authority) = _dark_endpoints(authority, temporary)

        base_image = Image.open(base_source).convert("RGBA")
        if base_image.size != (MASTER_SIZE, MASTER_SIZE):
            raise ValueError(f"{authority.theme_id}: invalid base geometry {base_image.size}")
        selected_images = [Image.open(path).convert("RGBA") for path in selected_sources]
        if any(image.size != base_image.size for image in selected_images):
            raise ValueError(f"{authority.theme_id}: selected geometry mismatch")

        theme_root = output_root / authority.theme_id
        identity_path = theme_root / "identity" / "base-static.png"
        identity_pargb_path = theme_root / "identity" / "base-static.pargbz"
        save_png(identity_path, base_image)
        save_pargbz(identity_pargb_path, base_pargb_source)
        base_pargb = load_pargb(base_pargb_source)
        selected_pargb_values = [load_pargb(path) for path in selected_pargb_sources]
        slots: list[dict[str, Any]] = []
        manifest_slots: list[dict[str, Any]] = []
        masks: list[np.ndarray] = []

        for slot, selected_image in enumerate(selected_images, start=1):
            selected_rgba = rgba_array(selected_image)
            selected_pargb = selected_pargb_values[slot - 1]
            mask = difference_mask_pargb(base_pargb, selected_pargb)
            masks.append(mask)
            source_path = theme_root / "selected" / f"selected-{slot}-source.png"
            source_pargb_path = theme_root / "selected" / f"selected-{slot}-source.pargbz"
            mask_path = theme_root / "selected" / f"selected-{slot}-mask.png"
            save_png(source_path, selected_image)
            save_pargbz(source_pargb_path, selected_pargb_sources[slot - 1])
            save_png(mask_path, Image.fromarray(mask, "L"))

            changed = np.any(base_pargb != selected_pargb, axis=2)
            mask_owned = mask == 255
            missing = int(np.count_nonzero(changed & ~mask_owned))
            extra = int(np.count_nonzero(mask_owned & ~changed))
            changed_count = int(np.count_nonzero(changed))
            mask_count = int(np.count_nonzero(mask_owned))
            if missing != 0:
                raise AssertionError(f"{authority.theme_id} slot {slot}: mask missed changed pixels")

            strength_hashes: dict[str, str] = {}
            for strength in (0, 64, 128, 192, 255):
                result = interpolate_pargb_arrays(base_pargb, selected_pargb, mask, strength)
                bgra = result[..., [2, 1, 0, 3]]
                strength_hashes[str(strength)] = sha256_bytes(bgra.tobytes(order="C"))
                if np.any(result[..., :3] > result[..., 3:4]):
                    raise AssertionError(f"{authority.theme_id} slot {slot}: PArgb invariant failed")
            if strength_hashes["0"] != raw_pargb_array_sha(base_pargb):
                raise AssertionError("strength 0 endpoint drift")
            if strength_hashes["255"] != raw_pargb_array_sha(selected_pargb):
                raise AssertionError("strength 255 endpoint drift")

            slots.append({
                "slotId": slot,
                "changedPixelCount": changed_count,
                "maskPixelCount": mask_count,
                "maskCoveragePercent": round(mask_count * 100.0 / mask.size, 8),
                "missingChangedPixelCount": missing,
                "extraMaskPixelCount": extra,
                "bounds": mask_bounds(mask),
                "connectedComponents": connected_component_summary(mask),
                "rawPArgbSha256ByStrength": strength_hashes,
            })
            manifest_slots.append({
                "slotId": slot,
                "source": {
                    "path": f"selected/selected-{slot}-source.png",
                    "sha256": sha256_file(source_path),
                    "pargbPath": f"selected/selected-{slot}-source.pargbz",
                    "pargbSha256": sha256_file(source_pargb_path),
                },
                "mask": {
                    "path": f"selected/selected-{slot}-mask.png",
                    "sha256": sha256_file(mask_path),
                },
            })

        report = {
            "compiler": {
                "name": "build_selected_emphasis_companion.py",
                "semanticRole": "SELECTED_EMPHASIS",
                "maskAuthority": "offline binary PArgb difference support",
                "runtimeInference": False,
                "interpolation": "channel=(base*(255-strength)+selected*strength+127)//255",
                "maskValues": [0, 255],
                "dependencies": {"Pillow": Image.__version__, "NumPy": np.__version__},
            },
            "themeId": authority.theme_id,
            "sourceAuthority": source_authority,
            "referenceCanvas": {"width": MASTER_SIZE, "height": MASTER_SIZE, "colorMode": authority.color_mode},
            "baseRawPArgbSha256": raw_pargb_array_sha(base_pargb),
            "slots": slots,
            "coverageGate": {
                "missingChangedPixelCount": sum(slot["missingChangedPixelCount"] for slot in slots),
                "passed": all(slot["missingChangedPixelCount"] == 0 for slot in slots),
            },
        }
        save_json(theme_root / "report.json", report)
        manifest = {
            "schemaVersion": 1,
            "themeId": authority.theme_id,
            "sourceProtocolVersion": authority.protocol_version,
            "sourcePackageRevision": authority.package_revision,
            "slotCount": authority.slot_count,
            "referenceCanvas": {"width": MASTER_SIZE, "height": MASTER_SIZE, "colorMode": authority.color_mode},
            "baseStatic": {
                "path": "identity/base-static.png",
                "sha256": sha256_file(identity_path),
                "pargbPath": "identity/base-static.pargbz",
                "pargbSha256": sha256_file(identity_pargb_path),
            },
            "selected": manifest_slots,
        }
        save_json(theme_root / "manifest.json", manifest)
        review = _review_sheet(
            authority, base_image, selected_images, masks, base_pargb, selected_pargb_values
        )
        save_png(theme_root / "review" / f"{authority.theme_id}-selected-emphasis-review.png", review)

        return {
            "themeId": authority.theme_id,
            "manifestSha256": sha256_file(theme_root / "manifest.json"),
            "reportSha256": sha256_file(theme_root / "report.json"),
            "reviewSha256": sha256_file(
                theme_root / "review" / f"{authority.theme_id}-selected-emphasis-review.png"
            ),
            "slotCount": authority.slot_count,
        }


def compile_companions(theme_ids: Iterable[str], output_root: Path) -> list[dict[str, Any]]:
    output_root.mkdir(parents=True, exist_ok=True)
    results = [_compile_theme(THEMES[theme_id], output_root) for theme_id in theme_ids]
    save_json(output_root / "build-report.json", {
        "schemaVersion": 1,
        "phase": "Universal Radial V3 Phase 3 SELECTED_EMPHASIS",
        "themes": results,
    })
    return results


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--theme",
        action="append",
        choices=sorted(THEMES),
        help="compile one theme; repeat for more (default: all formal themes)",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    theme_ids = args.theme or list(THEMES)
    results = compile_companions(theme_ids, args.output.resolve())
    for result in results:
        print(
            f"{result['themeId']}: slots={result['slotCount']} "
            f"manifest={result['manifestSha256']}"
        )
    print(f"output={args.output.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
