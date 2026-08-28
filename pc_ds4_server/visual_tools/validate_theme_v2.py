#!/usr/bin/env python3
"""Validate a LeftPad UI Theme Package V2 manifest.

This Phase 0 tool is intentionally independent from V1 discovery and runtime
loading.  It validates the closed JSON shape plus cross-reference, state,
ownership, capability, path, and hash invariants defined by the V2 protocol.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
from dataclasses import dataclass
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Any, Iterable, Mapping, Sequence

try:  # Optional: the repository test environment does not require this extra.
    from jsonschema import Draft202012Validator
except ImportError:  # pragma: no cover - exercised only when dependency exists.
    Draft202012Validator = None  # type: ignore[assignment,misc]


SCHEMA_PATH = Path(__file__).with_name("schemas") / "ui-theme-v2.schema.json"
LAYOUT_SLOT_COUNTS = {"radial-6": 6, "radial-8": 8}
RENDER_STRATEGIES = {"full-state-frame", "layered-state"}
AUTHORING_METHODS = {"external-artwork", "procedural", "mixed"}
LAYER_KINDS = {
    "stateAsset",
    "staticAsset",
    "dynamicText",
    "dynamicGlyph",
    "occlusionAsset",
    "safeSurfaceAsset",
}
ASSET_LAYER_KINDS = {"staticAsset", "occlusionAsset", "safeSurfaceAsset"}
DYNAMIC_LAYER_KINDS = {"dynamicText", "dynamicGlyph"}
CAPABILITIES = {
    "fullStateFrame",
    "layeredState",
    "dynamicAnchors",
    "themeGlyphAssets",
    "visualRegions",
    "maskAssets",
    "occlusionLayers",
    "safeDynamicSurfaces",
    "instantTransitions",
    "futureStates",
}
FUTURE_STATE_RE = re.compile(r"^(pressed-[1-8]|confirm|cancel|opening|closing)$")
SELECTED_STATE_RE = re.compile(r"^selected-([1-8])$")
PRESSED_STATE_RE = re.compile(r"^pressed-([1-8])$")
IDENTIFIER_RE = re.compile(r"^[A-Za-z][A-Za-z0-9._-]*$")
PATH_SEGMENT_RE = re.compile(r"^[A-Za-z0-9._-]+$")
HASH_RE = re.compile(r"^[A-Fa-f0-9]{64}$")
GLYPH_FAMILIES = ("keyboard", "keyboardShortcut", "ds4", "genericAction")


class ThemeV2ValidationError(ValueError):
    """Raised when a V2 theme manifest violates the frozen contract."""


class DuplicateJsonKeyError(ValueError):
    """Raised when a JSON object repeats a field name."""


@dataclass(frozen=True)
class ThemeV2ValidationReport:
    theme_id: str
    layout_profile: str
    compatible_layouts: tuple[str, ...]
    render_strategy: str
    state_count: int
    layer_count: int
    anchor_count: int
    asset_count: int
    assets_verified: bool


def _unique_json_object(pairs: Iterable[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateJsonKeyError(f"duplicate JSON field: {key}")
        result[key] = value
    return result


def _reject_json_constant(value: str) -> None:
    raise ValueError(f"non-finite JSON number: {value}")


def _load_json(path: Path, label: str) -> dict[str, Any]:
    try:
        text = path.read_text(encoding="utf-8-sig")
    except OSError as exception:
        raise ThemeV2ValidationError(f"unable to read {label}: {exception}") from exception
    try:
        value = json.loads(
            text,
            object_pairs_hook=_unique_json_object,
            parse_constant=_reject_json_constant,
        )
    except (json.JSONDecodeError, DuplicateJsonKeyError, ValueError) as exception:
        raise ThemeV2ValidationError(f"invalid {label}: {exception}") from exception
    if not isinstance(value, dict):
        raise ThemeV2ValidationError(f"invalid {label}: root must be an object")
    return value


def load_theme_v2_manifest(path: Path | str) -> dict[str, Any]:
    """Load a V2 manifest while rejecting duplicate keys and non-finite JSON."""

    return _load_json(Path(path), "V2 theme manifest")


def _fail(message: str) -> None:
    raise ThemeV2ValidationError(message)


def _object(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        _fail(f"{label} must be an object")
    return value


def _array(value: Any, label: str, *, nonempty: bool = False) -> list[Any]:
    if not isinstance(value, list):
        _fail(f"{label} must be an array")
    if nonempty and not value:
        _fail(f"{label} must not be empty")
    return value


def _string(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value:
        _fail(f"{label} must be a non-empty string")
    return value


def _number(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        _fail(f"{label} must be a number")
    number = float(value)
    if not math.isfinite(number):
        _fail(f"{label} must be finite")
    return number


def _integer(value: Any, label: str) -> int:
    if type(value) is not int:
        _fail(f"{label} must be an integer")
    return value


def _closed(
    value: Mapping[str, Any],
    label: str,
    required: Iterable[str],
    optional: Iterable[str] = (),
) -> None:
    required_set = set(required)
    allowed = required_set | set(optional)
    missing = sorted(required_set - value.keys())
    unknown = sorted(value.keys() - allowed)
    if missing:
        _fail(f"{label} missing required field(s): {', '.join(missing)}")
    if unknown:
        _fail(f"{label} contains unknown field(s): {', '.join(unknown)}")


def _identifier(value: Any, label: str) -> str:
    result = _string(value, label)
    if len(result) > 96 or IDENTIFIER_RE.fullmatch(result) is None:
        _fail(f"{label} is not a valid identifier: {result}")
    return result


def _unique_strings(value: Any, label: str, *, nonempty: bool = False) -> list[str]:
    items = _array(value, label, nonempty=nonempty)
    strings = [_string(item, f"{label}[{index}]") for index, item in enumerate(items)]
    if len(strings) != len(set(strings)):
        _fail(f"{label} must contain unique values")
    return strings


def _bounds(value: Any, label: str) -> dict[str, float]:
    obj = _object(value, label)
    _closed(obj, label, ("x", "y", "width", "height"))
    bounds = {key: _number(obj[key], f"{label}.{key}") for key in obj}
    if bounds["width"] <= 0 or bounds["height"] <= 0:
        _fail(f"{label} width and height must be greater than zero")
    return bounds


def _assert_inside(bounds: Mapping[str, float], width: float, height: float, label: str) -> None:
    if (
        bounds["x"] < 0
        or bounds["y"] < 0
        or bounds["x"] + bounds["width"] > width
        or bounds["y"] + bounds["height"] > height
    ):
        _fail(f"{label} lies outside referenceCanvas")


def _validate_closed_shape(document: dict[str, Any]) -> None:
    """Dependency-free shape validation mirroring the closed formal schema."""

    required = (
        "protocolVersion", "packageRevision", "id", "name", "surface", "renderStrategy", "layoutProfile",
        "compatibleLayouts", "referenceCanvas", "referenceScale", "states",
        "layers", "dynamicAnchors", "styles", "glyphs", "elementOwnership",
        "masks", "fallback", "capabilities", "transitions", "assetHashes",
    )
    _closed(document, "manifest", required, ("description", "exampleOnly", "authoring", "visualRegions"))
    if document["protocolVersion"] != 2:
        _fail("manifest.protocolVersion must equal 2")
    if _integer(document["packageRevision"], "manifest.packageRevision") < 1:
        _fail("manifest.packageRevision must be at least 1")
    _identifier(document["id"], "manifest.id")
    _string(document["name"], "manifest.name")
    if "description" in document and not isinstance(document["description"], str):
        _fail("manifest.description must be a string")
    if "exampleOnly" in document and type(document["exampleOnly"]) is not bool:
        _fail("manifest.exampleOnly must be a boolean")
    if document["surface"] != "radial-overlay":
        _fail(f"unsupported surface: {document['surface']}")
    if document["renderStrategy"] not in RENDER_STRATEGIES:
        _fail(f"unsupported renderStrategy: {document['renderStrategy']}")
    if "authoring" in document:
        authoring = _object(document["authoring"], "manifest.authoring")
        _closed(authoring, "manifest.authoring", ("method",))
        if authoring["method"] not in AUTHORING_METHODS:
            _fail(f"unsupported authoring method: {authoring['method']}")
    if document["layoutProfile"] not in LAYOUT_SLOT_COUNTS:
        _fail(f"unsupported layoutProfile: {document['layoutProfile']}")
    compatible = _unique_strings(document["compatibleLayouts"], "manifest.compatibleLayouts", nonempty=True)
    unknown_layouts = sorted(set(compatible) - LAYOUT_SLOT_COUNTS.keys())
    if unknown_layouts:
        _fail(f"unsupported compatible layout(s): {', '.join(unknown_layouts)}")

    canvas = _object(document["referenceCanvas"], "manifest.referenceCanvas")
    _closed(canvas, "manifest.referenceCanvas", ("width", "height", "colorSpace", "alphaMode"))
    if _integer(canvas["width"], "manifest.referenceCanvas.width") <= 0:
        _fail("manifest.referenceCanvas.width must be greater than zero")
    if _integer(canvas["height"], "manifest.referenceCanvas.height") <= 0:
        _fail("manifest.referenceCanvas.height must be greater than zero")
    if canvas["colorSpace"] != "sRGB" or canvas["alphaMode"] != "straight":
        _fail("referenceCanvas requires sRGB with straight alpha")

    scale = _object(document["referenceScale"], "manifest.referenceScale")
    _closed(scale, "manifest.referenceScale", ("logicalWidth", "logicalHeight", "fit", "origin"))
    if _number(scale["logicalWidth"], "manifest.referenceScale.logicalWidth") <= 0:
        _fail("manifest.referenceScale.logicalWidth must be greater than zero")
    if _number(scale["logicalHeight"], "manifest.referenceScale.logicalHeight") <= 0:
        _fail("manifest.referenceScale.logicalHeight must be greater than zero")
    if scale["fit"] != "contain":
        _fail("manifest.referenceScale.fit must equal contain")
    origin = _object(scale["origin"], "manifest.referenceScale.origin")
    _closed(origin, "manifest.referenceScale.origin", ("x", "y"))
    _number(origin["x"], "manifest.referenceScale.origin.x")
    _number(origin["y"], "manifest.referenceScale.origin.y")

    states = _object(document["states"], "manifest.states")
    if not states:
        _fail("manifest.states must not be empty")
    for name, raw_state in states.items():
        _string(name, "manifest.states key")
        state = _object(raw_state, f"states.{name}")
        _closed(state, f"states.{name}", ("slotId", "assets"))
        if state["slotId"] is not None:
            slot = _integer(state["slotId"], f"states.{name}.slotId")
            if not 1 <= slot <= 8:
                _fail(f"states.{name}.slotId must be between 1 and 8")
        assets = _object(state["assets"], f"states.{name}.assets")
        for layer_id, path in assets.items():
            _identifier(layer_id, f"states.{name}.assets key")
            _string(path, f"states.{name}.assets.{layer_id}")

    layers = _array(document["layers"], "manifest.layers", nonempty=True)
    for index, raw_layer in enumerate(layers):
        label = f"layers[{index}]"
        layer = _object(raw_layer, label)
        _closed(layer, label, ("id", "kind", "zIndex", "bounds", "visibleStates", "ownership", "required"), ("asset",))
        _identifier(layer["id"], f"{label}.id")
        if layer["kind"] not in LAYER_KINDS:
            _fail(f"unsupported layer kind: {layer['kind']}")
        _integer(layer["zIndex"], f"{label}.zIndex")
        _bounds(layer["bounds"], f"{label}.bounds")
        _unique_strings(layer["visibleStates"], f"{label}.visibleStates", nonempty=True)
        if layer["ownership"] not in {"STATIC", "DYNAMIC", "STATE_ASSET"}:
            _fail(f"{label}.ownership has an unsupported value")
        if type(layer["required"]) is not bool:
            _fail(f"{label}.required must be a boolean")
        if layer["kind"] in ASSET_LAYER_KINDS and "asset" not in layer:
            _fail(f"{label}.asset is required for {layer['kind']}")
        if layer["kind"] in ({"stateAsset"} | DYNAMIC_LAYER_KINDS) and "asset" in layer:
            _fail(f"{label}.asset is not allowed for {layer['kind']}")
        if "asset" in layer:
            _string(layer["asset"], f"{label}.asset")
        expected_ownership = (
            "STATE_ASSET" if layer["kind"] == "stateAsset"
            else "DYNAMIC" if layer["kind"] in DYNAMIC_LAYER_KINDS
            else "STATIC"
        )
        if layer["ownership"] != expected_ownership:
            _fail(f"{label}.ownership must be {expected_ownership} for {layer['kind']}")

    anchors = _array(document["dynamicAnchors"], "manifest.dynamicAnchors")
    for index, raw_anchor in enumerate(anchors):
        label = f"dynamicAnchors[{index}]"
        anchor = _object(raw_anchor, label)
        _closed(
            anchor, label,
            ("id", "layerId", "role", "bounds", "horizontalAlignment", "verticalAlignment",
             "overflowPolicy", "minimumScale", "rotation", "visibleStates", "styleRole"),
            ("maxLines", "glyphRole", "safeSurface"),
        )
        _identifier(anchor["id"], f"{label}.id")
        _identifier(anchor["layerId"], f"{label}.layerId")
        if anchor["role"] not in {"text", "glyph"}:
            _fail(f"{label}.role must be text or glyph")
        _bounds(anchor["bounds"], f"{label}.bounds")
        if anchor["horizontalAlignment"] not in {"left", "center", "right"}:
            _fail(f"{label}.horizontalAlignment is unsupported")
        if anchor["verticalAlignment"] not in {"top", "center", "bottom"}:
            _fail(f"{label}.verticalAlignment is unsupported")
        if anchor["overflowPolicy"] not in {"ellipsis", "shrink", "clip", "hide"}:
            _fail(f"{label}.overflowPolicy is unsupported")
        minimum_scale = _number(anchor["minimumScale"], f"{label}.minimumScale")
        if not 0 < minimum_scale <= 1:
            _fail(f"{label}.minimumScale must be in (0, 1]")
        rotation = _number(anchor["rotation"], f"{label}.rotation")
        if not -360 <= rotation <= 360:
            _fail(f"{label}.rotation must be between -360 and 360")
        _unique_strings(anchor["visibleStates"], f"{label}.visibleStates", nonempty=True)
        _identifier(anchor["styleRole"], f"{label}.styleRole")
        if anchor["role"] == "glyph" and "glyphRole" not in anchor:
            _fail(f"{label}.glyphRole is required for glyph anchors")
        for field in ("glyphRole", "safeSurface"):
            if field in anchor:
                _identifier(anchor[field], f"{label}.{field}")
        if "maxLines" in anchor and not 1 <= _integer(anchor["maxLines"], f"{label}.maxLines") <= 8:
            _fail(f"{label}.maxLines must be between 1 and 8")

    _validate_style_shape(document["styles"])
    _validate_glyph_shape(document["glyphs"])
    _validate_ownership_shape(document["elementOwnership"])
    _validate_mask_shape(document["masks"])
    _validate_region_shape(document.get("visualRegions", []))

    fallback = _object(document["fallback"], "manifest.fallback")
    _closed(fallback, "manifest.fallback", ("onInvalidCandidate", "onUnsupportedVersion", "startupThemeId"))
    if fallback["onInvalidCandidate"] != "retain-active" or fallback["onUnsupportedVersion"] != "retain-active":
        _fail("fallback policies must retain-active")
    _identifier(fallback["startupThemeId"], "manifest.fallback.startupThemeId")

    capabilities = _object(document["capabilities"], "manifest.capabilities")
    _closed(capabilities, "manifest.capabilities", ("required", "optional"))
    for field in ("required", "optional"):
        values = _unique_strings(capabilities[field], f"manifest.capabilities.{field}")
        unknown = sorted(set(values) - CAPABILITIES)
        if unknown:
            _fail(f"unknown capability in {field}: {', '.join(unknown)}")

    transitions = _object(document["transitions"], "manifest.transitions")
    _closed(transitions, "manifest.transitions", ("mode",))
    if transitions["mode"] != "instant":
        _fail("unsupported transition mode; Phase 0 permits only instant")

    hashes = _object(document["assetHashes"], "manifest.assetHashes")
    for path, digest in hashes.items():
        _string(path, "manifest.assetHashes key")
        if not isinstance(digest, str) or HASH_RE.fullmatch(digest) is None:
            _fail(f"assetHashes[{path!r}] must be a 64-character SHA-256 hex digest")


def _validate_style_shape(raw_styles: Any) -> None:
    styles = _object(raw_styles, "manifest.styles")
    _closed(styles, "manifest.styles", ("fontRoles", "colorRoles", "outlineRoles", "shadowRoles", "dynamicRoles"))
    for field in ("fontRoles", "colorRoles", "outlineRoles", "shadowRoles", "dynamicRoles"):
        _object(styles[field], f"styles.{field}")
    if not styles["fontRoles"] or not styles["colorRoles"] or not styles["dynamicRoles"]:
        _fail("fontRoles, colorRoles, and dynamicRoles must not be empty")
    for role, family in styles["fontRoles"].items():
        _identifier(role, "styles.fontRoles key")
        if family not in {"ui", "display", "monospace", "symbol"}:
            _fail(f"styles.fontRoles.{role} has unsupported font family role")
    for role, color in styles["colorRoles"].items():
        _identifier(role, "styles.colorRoles key")
        if not isinstance(color, str) or re.fullmatch(r"#[A-Fa-f0-9]{8}", color) is None:
            _fail(f"styles.colorRoles.{role} must be #RRGGBBAA")
    for collection, fields in (("outlineRoles", ("colorRole", "width")), ("shadowRoles", ("colorRole", "offsetX", "offsetY", "blur"))):
        for role, raw_value in styles[collection].items():
            _identifier(role, f"styles.{collection} key")
            value = _object(raw_value, f"styles.{collection}.{role}")
            _closed(value, f"styles.{collection}.{role}", fields)
            _identifier(value["colorRole"], f"styles.{collection}.{role}.colorRole")
            for field in fields[1:]:
                _number(value[field], f"styles.{collection}.{role}.{field}")
    for role, raw_value in styles["dynamicRoles"].items():
        _identifier(role, "styles.dynamicRoles key")
        value = _object(raw_value, f"styles.dynamicRoles.{role}")
        _closed(value, f"styles.dynamicRoles.{role}", ("fontRole", "colorRole", "size"), ("outlineRole", "shadowRole"))
        for field in ("fontRole", "colorRole", "outlineRole", "shadowRole"):
            if field in value:
                _identifier(value[field], f"styles.dynamicRoles.{role}.{field}")
        if _number(value["size"], f"styles.dynamicRoles.{role}.size") <= 0:
            _fail(f"styles.dynamicRoles.{role}.size must be greater than zero")


def _validate_glyph_shape(raw_glyphs: Any) -> None:
    glyphs = _object(raw_glyphs, "manifest.glyphs")
    _closed(glyphs, "manifest.glyphs", ("roles",))
    roles = _object(glyphs["roles"], "glyphs.roles")
    if not roles:
        _fail("glyphs.roles must not be empty")
    for role, raw_families in roles.items():
        _identifier(role, "glyphs.roles key")
        families = _object(raw_families, f"glyphs.roles.{role}")
        _closed(families, f"glyphs.roles.{role}", GLYPH_FAMILIES)
        for family in GLYPH_FAMILIES:
            family_value = _object(families[family], f"glyphs.roles.{role}.{family}")
            _closed(family_value, f"glyphs.roles.{role}.{family}", ("sources",))
            sources = _array(family_value["sources"], f"glyphs.roles.{role}.{family}.sources", nonempty=True)
            source_types: list[str] = []
            for index, raw_source in enumerate(sources):
                label = f"glyphs.roles.{role}.{family}.sources[{index}]"
                source = _object(raw_source, label)
                source_type = source.get("type")
                source_types.append(_string(source_type, f"{label}.type"))
                if source_type == "text":
                    _closed(source, label, ("type", "styleRole"))
                    _identifier(source["styleRole"], f"{label}.styleRole")
                elif source_type == "themeAsset":
                    _closed(source, label, ("type", "assets"))
                    assets = _object(source["assets"], f"{label}.assets")
                    if not assets:
                        _fail(f"{label}.assets must not be empty")
                    for asset_id, path in assets.items():
                        _identifier(asset_id, f"{label}.assets key")
                        _string(path, f"{label}.assets.{asset_id}")
                elif source_type == "runtimeSymbol":
                    _closed(source, label, ("type", "symbolSet"))
                    _identifier(source["symbolSet"], f"{label}.symbolSet")
                else:
                    _fail(f"{label}.type is unsupported: {source_type}")
            if len(source_types) != len(set(source_types)):
                _fail(f"glyphs.roles.{role}.{family}.sources repeats a source type")


def _validate_ownership_shape(raw_ownership: Any) -> None:
    entries = _array(raw_ownership, "manifest.elementOwnership", nonempty=True)
    for index, raw_entry in enumerate(entries):
        label = f"elementOwnership[{index}]"
        entry = _object(raw_entry, label)
        _closed(entry, label, ("element", "owner", "layerId"), ("contentKey",))
        _identifier(entry["element"], f"{label}.element")
        if entry["owner"] not in {"STATIC", "DYNAMIC", "STATE_ASSET"}:
            _fail(f"{label}.owner is unsupported")
        _identifier(entry["layerId"], f"{label}.layerId")
        if entry["owner"] == "DYNAMIC" and "contentKey" not in entry:
            _fail(f"{label}.contentKey is required for DYNAMIC ownership")
        if "contentKey" in entry:
            _identifier(entry["contentKey"], f"{label}.contentKey")


def _validate_mask_shape(raw_masks: Any) -> None:
    masks = _array(raw_masks, "manifest.masks")
    for index, raw_mask in enumerate(masks):
        label = f"masks[{index}]"
        mask = _object(raw_mask, label)
        _closed(mask, label, ("id", "asset", "purpose", "bounds", "appliesTo"))
        _identifier(mask["id"], f"{label}.id")
        _string(mask["asset"], f"{label}.asset")
        if mask["purpose"] not in {"safeSurface", "compileClip", "debugRegion"}:
            _fail(f"{label}.purpose is unsupported")
        _bounds(mask["bounds"], f"{label}.bounds")
        for layer_id in _unique_strings(mask["appliesTo"], f"{label}.appliesTo", nonempty=True):
            _identifier(layer_id, f"{label}.appliesTo")


def _validate_region_shape(raw_regions: Any) -> None:
    regions = _array(raw_regions, "manifest.visualRegions")
    for index, raw_region in enumerate(regions):
        label = f"visualRegions[{index}]"
        region = _object(raw_region, label)
        _closed(region, label, ("id", "slotId", "label", "polygon"))
        _identifier(region["id"], f"{label}.id")
        if not 1 <= _integer(region["slotId"], f"{label}.slotId") <= 8:
            _fail(f"{label}.slotId must be between 1 and 8")
        _string(region["label"], f"{label}.label")
        points = _array(region["polygon"], f"{label}.polygon")
        if len(points) < 3:
            _fail(f"{label}.polygon must contain at least three points")
        for point_index, raw_point in enumerate(points):
            point_label = f"{label}.polygon[{point_index}]"
            point = _object(raw_point, point_label)
            _closed(point, point_label, ("x", "y"))
            _number(point["x"], f"{point_label}.x")
            _number(point["y"], f"{point_label}.y")


def _validate_package_path(path: str, label: str) -> None:
    if "\\" in path or ":" in path or path.startswith(("/", "~")):
        _fail(f"{label} must be a package-relative POSIX path: {path}")
    posix = PurePosixPath(path)
    windows = PureWindowsPath(path)
    if posix.is_absolute() or windows.is_absolute() or not posix.parts:
        _fail(f"{label} must be a package-relative path: {path}")
    if any(part in {"", ".", ".."} for part in posix.parts):
        _fail(f"{label} contains an unsafe path segment: {path}")
    if any(PATH_SEGMENT_RE.fullmatch(part) is None for part in posix.parts):
        _fail(f"{label} contains an unsupported path segment: {path}")


def _ensure_unique(values: Sequence[str], label: str) -> None:
    seen: set[str] = set()
    for value in values:
        if value in seen:
            _fail(f"duplicate {label}: {value}")
        seen.add(value)


def _validate_visible_states(selectors: Iterable[str], states: Mapping[str, Any], label: str) -> None:
    allowed = set(states) | {"all", "selected", "pressed"}
    for selector in selectors:
        if selector not in allowed:
            _fail(f"{label} references unknown state selector: {selector}")


def _collect_asset_paths(document: Mapping[str, Any]) -> set[str]:
    paths: set[str] = set()
    for state in document["states"].values():
        paths.update(state["assets"].values())
    for layer in document["layers"]:
        if "asset" in layer:
            paths.add(layer["asset"])
    for role in document["glyphs"]["roles"].values():
        for family in GLYPH_FAMILIES:
            for source in role[family]["sources"]:
                if source["type"] == "themeAsset":
                    paths.update(source["assets"].values())
    paths.update(mask["asset"] for mask in document["masks"])
    return paths


def _validate_semantics(
    document: dict[str, Any],
    supported_capabilities: Iterable[str] | None,
) -> set[str]:
    layout_profile = document["layoutProfile"]
    compatible_layouts = document["compatibleLayouts"]
    if layout_profile not in compatible_layouts:
        _fail("layoutProfile must be present in compatibleLayouts")
    if document["renderStrategy"] == "full-state-frame" and len(compatible_layouts) != 1:
        _fail("full-state-frame requires exactly one compatible layout")

    maximum_slots = max(LAYOUT_SLOT_COUNTS[profile] for profile in compatible_layouts)
    states = document["states"]
    required_states = {"idle", *(f"selected-{slot}" for slot in range(1, maximum_slots + 1))}
    missing_states = sorted(required_states - states.keys())
    if missing_states:
        _fail(f"missing required state(s): {', '.join(missing_states)}")
    for name, state in states.items():
        selected_match = SELECTED_STATE_RE.fullmatch(name)
        pressed_match = PRESSED_STATE_RE.fullmatch(name)
        if name == "idle":
            expected_slot = None
        elif selected_match:
            expected_slot = int(selected_match.group(1))
        elif pressed_match:
            expected_slot = int(pressed_match.group(1))
        elif FUTURE_STATE_RE.fullmatch(name):
            expected_slot = None
        else:
            _fail(f"unsupported state name: {name}")
        if state["slotId"] != expected_slot:
            _fail(f"state/slot mismatch for {name}: expected {expected_slot}, found {state['slotId']}")
        if expected_slot is not None and expected_slot > maximum_slots:
            _fail(f"state {name} exceeds compatible layout slot count {maximum_slots}")
    future_states = sorted(set(states) - required_states)
    required_capabilities = set(document["capabilities"]["required"])
    optional_capabilities = set(document["capabilities"]["optional"])
    overlap = sorted(required_capabilities & optional_capabilities)
    if overlap:
        _fail(f"capabilities cannot be both required and optional: {', '.join(overlap)}")
    if future_states and "futureStates" not in required_capabilities:
        _fail("future states require the futureStates capability")

    if supported_capabilities is not None:
        supported = set(supported_capabilities)
        unknown_context = sorted(supported - CAPABILITIES)
        if unknown_context:
            _fail(f"validator context contains unknown capability: {', '.join(unknown_context)}")
        unsupported = sorted(required_capabilities - supported)
        if unsupported:
            _fail(f"unsupported required capability: {', '.join(unsupported)}")

    strategy_capability = {
        "full-state-frame": "fullStateFrame",
        "layered-state": "layeredState",
    }.get(document["renderStrategy"])
    if strategy_capability and strategy_capability not in required_capabilities:
        _fail(f"renderStrategy {document['renderStrategy']} requires capability {strategy_capability}")
    if "instantTransitions" not in required_capabilities:
        _fail("instant transition contract requires capability instantTransitions")

    width = float(document["referenceCanvas"]["width"])
    height = float(document["referenceCanvas"]["height"])
    layers = document["layers"]
    layer_ids = [layer["id"] for layer in layers]
    _ensure_unique(layer_ids, "layer id")
    layer_map = {layer["id"]: layer for layer in layers}
    state_layers = {layer["id"] for layer in layers if layer["kind"] == "stateAsset"}
    required_state_layers = {layer["id"] for layer in layers if layer["kind"] == "stateAsset" and layer["required"]}
    for index, layer in enumerate(layers):
        _assert_inside(layer["bounds"], width, height, f"layers[{index}].bounds")
        _validate_visible_states(layer["visibleStates"], states, f"layers[{index}].visibleStates")
    if document["renderStrategy"] == "full-state-frame":
        full_canvas_layers = [
            layer for layer in layers
            if layer["kind"] == "stateAsset"
            and layer["required"]
            and layer["bounds"] == {"x": 0, "y": 0, "width": width, "height": height}
        ]
        if not full_canvas_layers:
            _fail("full-state-frame requires a required full-canvas stateAsset layer")
    for name, state in states.items():
        unknown_bindings = sorted(set(state["assets"]) - state_layers)
        if unknown_bindings:
            _fail(f"states.{name}.assets references unknown/non-state layer: {', '.join(unknown_bindings)}")
        missing_bindings = sorted(required_state_layers - state["assets"].keys())
        if missing_bindings:
            _fail(f"states.{name}.assets missing required layer binding(s): {', '.join(missing_bindings)}")

    anchors = document["dynamicAnchors"]
    anchor_ids = [anchor["id"] for anchor in anchors]
    _ensure_unique(anchor_ids, "anchor id")
    styles = document["styles"]
    dynamic_roles = styles["dynamicRoles"]
    glyph_roles = document["glyphs"]["roles"]
    mask_map = {mask["id"]: mask for mask in document["masks"]}
    for index, anchor in enumerate(anchors):
        _assert_inside(anchor["bounds"], width, height, f"dynamicAnchors[{index}].bounds")
        layer = layer_map.get(anchor["layerId"])
        if layer is None:
            _fail(f"dynamicAnchors[{index}] references unknown layer: {anchor['layerId']}")
        expected_kind = "dynamicText" if anchor["role"] == "text" else "dynamicGlyph"
        if layer["kind"] != expected_kind:
            _fail(f"dynamicAnchors[{index}] role requires layer kind {expected_kind}")
        if anchor["styleRole"] not in dynamic_roles:
            _fail(f"dynamicAnchors[{index}] references unknown styleRole: {anchor['styleRole']}")
        if "glyphRole" in anchor and anchor["glyphRole"] not in glyph_roles:
            _fail(f"dynamicAnchors[{index}] references unknown glyphRole: {anchor['glyphRole']}")
        if "safeSurface" in anchor:
            mask = mask_map.get(anchor["safeSurface"])
            if mask is None or mask["purpose"] != "safeSurface":
                _fail(f"dynamicAnchors[{index}] references unknown/non-safeSurface mask: {anchor['safeSurface']}")
        _validate_visible_states(anchor["visibleStates"], states, f"dynamicAnchors[{index}].visibleStates")
    if anchors and "dynamicAnchors" not in required_capabilities:
        _fail("dynamicAnchors require capability dynamicAnchors")

    for role, value in dynamic_roles.items():
        references = {
            "fontRole": styles["fontRoles"],
            "colorRole": styles["colorRoles"],
            "outlineRole": styles["outlineRoles"],
            "shadowRole": styles["shadowRoles"],
        }
        for field, collection in references.items():
            if field in value and value[field] not in collection:
                _fail(f"styles.dynamicRoles.{role}.{field} references unknown role: {value[field]}")
    for collection in ("outlineRoles", "shadowRoles"):
        for role, value in styles[collection].items():
            if value["colorRole"] not in styles["colorRoles"]:
                _fail(f"styles.{collection}.{role}.colorRole references unknown role: {value['colorRole']}")

    has_theme_glyph_assets = False
    for role_name, role in glyph_roles.items():
        for family in GLYPH_FAMILIES:
            for source in role[family]["sources"]:
                if source["type"] == "text" and source["styleRole"] not in dynamic_roles:
                    _fail(f"glyphs.roles.{role_name}.{family} references unknown styleRole: {source['styleRole']}")
                has_theme_glyph_assets |= source["type"] == "themeAsset"
    if has_theme_glyph_assets and "themeGlyphAssets" not in required_capabilities:
        _fail("themeAsset glyph sources require capability themeGlyphAssets")

    ownership = document["elementOwnership"]
    _ensure_unique([entry["element"] for entry in ownership], "element ownership")
    ownership_map = {entry["element"]: entry for entry in ownership}
    for index, entry in enumerate(ownership):
        layer = layer_map.get(entry["layerId"])
        if layer is None:
            _fail(f"elementOwnership[{index}] references unknown layer: {entry['layerId']}")
        if entry["owner"] != layer["ownership"]:
            _fail(f"elementOwnership[{index}] conflicts with layer ownership")
    missing_owned_layers = sorted(set(layer_map) - {entry["layerId"] for entry in ownership})
    if missing_owned_layers:
        _fail(f"layers missing elementOwnership entries: {', '.join(missing_owned_layers)}")
    for anchor in anchors:
        entry = ownership_map.get(anchor["id"])
        if entry is None or entry["owner"] != "DYNAMIC" or entry["layerId"] != anchor["layerId"]:
            _fail(f"dynamic anchor {anchor['id']} requires matching DYNAMIC elementOwnership")

    masks = document["masks"]
    _ensure_unique([mask["id"] for mask in masks], "mask id")
    for index, mask in enumerate(masks):
        _assert_inside(mask["bounds"], width, height, f"masks[{index}].bounds")
        unknown_layers = sorted(set(mask["appliesTo"]) - layer_map.keys())
        if unknown_layers:
            _fail(f"masks[{index}].appliesTo references unknown layer: {', '.join(unknown_layers)}")
    if masks and "maskAssets" not in required_capabilities:
        _fail("masks require capability maskAssets")
    if any(layer["kind"] == "occlusionAsset" for layer in layers) and "occlusionLayers" not in required_capabilities:
        _fail("occlusionAsset layers require capability occlusionLayers")
    if any(layer["kind"] == "safeSurfaceAsset" for layer in layers) and "safeDynamicSurfaces" not in required_capabilities:
        _fail("safeSurfaceAsset layers require capability safeDynamicSurfaces")

    regions = document.get("visualRegions", [])
    _ensure_unique([region["id"] for region in regions], "visual region id")
    for index, region in enumerate(regions):
        if region["slotId"] > maximum_slots:
            _fail(f"visualRegions[{index}].slotId exceeds compatible layout slot count")
        for point_index, point in enumerate(region["polygon"]):
            if not 0 <= point["x"] <= width or not 0 <= point["y"] <= height:
                _fail(f"visualRegions[{index}].polygon[{point_index}] lies outside referenceCanvas")
    if regions and "visualRegions" not in required_capabilities:
        _fail("visualRegions require capability visualRegions")

    referenced_assets = _collect_asset_paths(document)
    hashes = document["assetHashes"]
    for path in referenced_assets | set(hashes):
        _validate_package_path(path, "asset path")
    missing_hashes = sorted(referenced_assets - hashes.keys())
    orphan_hashes = sorted(hashes.keys() - referenced_assets)
    if missing_hashes:
        _fail(f"missing asset hash record(s): {', '.join(missing_hashes)}")
    if orphan_hashes:
        _fail(f"orphan asset hash record(s): {', '.join(orphan_hashes)}")
    return referenced_assets


def _run_json_schema_if_available(document: dict[str, Any], schema: dict[str, Any]) -> None:
    if Draft202012Validator is None:
        return
    try:
        Draft202012Validator.check_schema(schema)
        errors = sorted(Draft202012Validator(schema).iter_errors(document), key=lambda error: list(error.path))
    except Exception as exception:  # pragma: no cover - dependency-specific error path.
        raise ThemeV2ValidationError(f"unable to evaluate V2 JSON Schema: {exception}") from exception
    if errors:
        first = errors[0]
        location = ".".join(str(part) for part in first.absolute_path) or "manifest"
        _fail(f"JSON Schema violation at {location}: {first.message}")


def validate_theme_v2_document(
    document: dict[str, Any],
    *,
    supported_capabilities: Iterable[str] | None = None,
    schema_path: Path | str = SCHEMA_PATH,
) -> ThemeV2ValidationReport:
    """Validate an already-loaded V2 manifest without reading package assets.

    Passing ``supported_capabilities`` enables runtime-context negotiation.
    Omitting it validates the portable package contract only.
    """

    schema = _load_json(Path(schema_path), "V2 JSON Schema")
    _run_json_schema_if_available(document, schema)
    _validate_closed_shape(document)
    assets = _validate_semantics(document, supported_capabilities)
    return ThemeV2ValidationReport(
        theme_id=document["id"],
        layout_profile=document["layoutProfile"],
        compatible_layouts=tuple(document["compatibleLayouts"]),
        render_strategy=document["renderStrategy"],
        state_count=len(document["states"]),
        layer_count=len(document["layers"]),
        anchor_count=len(document["dynamicAnchors"]),
        asset_count=len(assets),
        assets_verified=False,
    )


def validate_theme_v2(
    manifest_path: Path | str,
    *,
    supported_capabilities: Iterable[str] | None = None,
    check_assets: bool = False,
) -> ThemeV2ValidationReport:
    """Validate a manifest, optionally checking files and SHA-256 content."""

    path = Path(manifest_path).resolve()
    document = load_theme_v2_manifest(path)
    report = validate_theme_v2_document(document, supported_capabilities=supported_capabilities)
    if not check_assets:
        return report

    package_root = path.parent
    for relative_path in sorted(_collect_asset_paths(document)):
        candidate = (package_root / PurePosixPath(relative_path)).resolve()
        try:
            candidate.relative_to(package_root)
        except ValueError as exception:
            raise ThemeV2ValidationError(f"asset escapes package root: {relative_path}") from exception
        if not candidate.is_file():
            _fail(f"referenced asset missing: {relative_path}")
        digest = hashlib.sha256(candidate.read_bytes()).hexdigest().upper()
        expected = document["assetHashes"][relative_path].upper()
        if digest != expected:
            _fail(f"asset hash mismatch: {relative_path}")
    return ThemeV2ValidationReport(**{**report.__dict__, "assets_verified": True})


def format_report(report: ThemeV2ValidationReport) -> str:
    verification = "verified" if report.assets_verified else "manifest-only"
    return "\n".join(
        (
            "UI THEME V2 VALID",
            f"Theme: {report.theme_id}",
            f"Render Strategy: {report.render_strategy}",
            f"Layout: {report.layout_profile}",
            f"Compatible Layouts: {', '.join(report.compatible_layouts)}",
            f"States/Layers/Anchors: {report.state_count}/{report.layer_count}/{report.anchor_count}",
            f"Assets: {report.asset_count} ({verification})",
        )
    )


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path, help="Path to a V2 theme manifest JSON file")
    parser.add_argument(
        "--supported-capability",
        action="append",
        default=None,
        choices=sorted(CAPABILITIES),
        help="Enable runtime-context capability negotiation; may be repeated",
    )
    parser.add_argument(
        "--check-assets",
        action="store_true",
        help="Require package assets to exist beside the manifest and verify SHA-256",
    )
    return parser.parse_args()


def main() -> int:
    args = _parse_args()
    try:
        report = validate_theme_v2(
            args.manifest,
            supported_capabilities=args.supported_capability,
            check_assets=args.check_assets,
        )
    except ThemeV2ValidationError as exception:
        print(f"UI THEME V2 INVALID\n{exception}")
        return 1
    print(format_report(report))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
