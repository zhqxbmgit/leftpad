from __future__ import annotations

import copy
import hashlib
import json
import math
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS_DIRECTORY = Path(__file__).resolve().parents[1]
if str(TOOLS_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIRECTORY))

from validate_theme_v2 import (  # noqa: E402
    SCHEMA_PATH,
    ThemeV2ValidationError,
    load_theme_v2_manifest,
    logical_edge_to_physical,
    reference_to_logical_transform,
    validate_theme_v2,
    validate_theme_v2_document,
)
from validate_visual_pack import PROFILE_CONTRACTS  # noqa: E402


EXAMPLE_PATH = TOOLS_DIRECTORY / "examples" / "reference-theme-v2.example.json"
PROTOCOL_PATH = TOOLS_DIRECTORY / "THEME_RUNTIME_PROTOCOL_V2.md"
DECOMPOSITION_PATH = TOOLS_DIRECTORY / "REFERENCE_THEME_DECOMPOSITION_SPEC.md"


def _example() -> dict[str, object]:
    return json.loads(EXAMPLE_PATH.read_text(encoding="utf-8"))


def _normalized_document(path: Path) -> str:
    return " ".join(path.read_text(encoding="utf-8").split())


def _radial_8() -> dict[str, object]:
    manifest = _example()
    manifest["id"] = "radial-8-v2-fixture"
    manifest["layoutProfile"] = "radial-8"
    manifest["compatibleLayouts"] = ["radial-8"]
    states = manifest["states"]
    hashes = manifest["assetHashes"]
    assert isinstance(states, dict)
    assert isinstance(hashes, dict)
    for slot, digest in ((7, "A" * 64), (8, "B" * 64)):
        path = f"states/selected-{slot}.png"
        states[f"selected-{slot}"] = {"slotId": slot, "assets": {"stateFrame": path}}
        hashes[path] = digest
    return manifest


def _replace_asset_path(manifest: dict[str, object], old: str, new: str) -> None:
    layers = manifest["layers"]
    hashes = manifest["assetHashes"]
    assert isinstance(layers, list)
    assert isinstance(hashes, dict)
    for layer in layers:
        if isinstance(layer, dict) and layer.get("asset") == old:
            layer["asset"] = new
    hashes[new] = hashes.pop(old)


class ThemeV2ValidatorTests(unittest.TestCase):
    def assert_invalid(self, manifest: dict[str, object], expected: str) -> None:
        with self.assertRaisesRegex(ThemeV2ValidationError, expected):
            validate_theme_v2_document(manifest)

    def test_formal_schema_is_draft_2020_12_json_with_resolved_local_refs(self) -> None:
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))

        self.assertEqual("https://json-schema.org/draft/2020-12/schema", schema["$schema"])
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual(
            ["full-state-frame", "layered-state"],
            schema["properties"]["renderStrategy"]["enum"],
        )
        self.assertEqual(
            ["external-artwork", "procedural", "mixed"],
            schema["properties"]["authoring"]["properties"]["method"]["enum"],
        )
        self.assertEqual("straight", schema["$defs"]["canvas"]["properties"]["alphaMode"]["const"])
        self.assertEqual(
            1,
            schema["allOf"][0]["then"]["properties"]["compatibleLayouts"]["maxItems"],
        )
        self.assertIn("placement", schema["required"])
        reference_scale = schema["properties"]["referenceScale"]
        self.assertIn("contentOrigin", reference_scale["required"])
        self.assertNotIn("origin", reference_scale["properties"])
        self.assertEqual(4096, reference_scale["properties"]["logicalWidth"]["maximum"])
        self.assertEqual(4096, reference_scale["properties"]["logicalHeight"]["maximum"])

        def walk(value: object) -> None:
            if isinstance(value, dict):
                reference = value.get("$ref")
                if isinstance(reference, str) and reference.startswith("#/$defs/"):
                    self.assertIn(reference.removeprefix("#/$defs/"), schema["$defs"])
                for nested in value.values():
                    walk(nested)
            elif isinstance(value, list):
                for nested in value:
                    walk(nested)

        walk(schema)

    def test_rotation_range_remains_closed(self) -> None:
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
        rotation = schema["$defs"]["anchor"]["properties"]["rotation"]

        self.assertEqual((-360, 360), (rotation["minimum"], rotation["maximum"]))

    def test_protocol_defines_positive_rotation_as_clockwise(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Positive `rotation` is clockwise", protocol)

    def test_protocol_defines_anchor_center_rotation(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("The rotation center is the geometric center of the anchor bounds", protocol)
        self.assertIn("centerX = anchor.x + anchor.width / 2", protocol)
        self.assertIn("centerY = anchor.y + anchor.height / 2", protocol)

    def test_protocol_aligns_before_rotation(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Alignment occurs before rotation", protocol)

    def test_protocol_evaluates_fit_after_rotation(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Fit is evaluated after rotation", protocol)

    def test_protocol_uses_reference_space_for_fit(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Reference Space continuous geometry is the semantic fit authority", protocol)
        self.assertIn("Physical anti-aliased pixel extents are not", protocol)

    def test_protocol_makes_overflow_decisions_dpi_independent(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "DPI MUST NOT change the chosen scale, ellipsized content, or show/hide decision",
            protocol,
        )

    def test_protocol_hides_exhausted_shrink(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "If `minimumScale` still does not fit, the entire dynamic element is hidden",
            protocol,
        )

    def test_protocol_forbids_shrink_ellipsis_fallback(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("`shrink` MUST NOT fall back to ellipsis", protocol)

    def test_protocol_forbids_shrink_clip_fallback(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("`shrink` MUST NOT fall back to clip", protocol)

    def test_protocol_hide_uses_authored_size_only(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "`hide` tests authored size only and MUST NOT shrink or ellipsize",
            protocol,
        )

    def test_protocol_ellipsis_uses_authored_size_only(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("`ellipsis` uses authored size and MUST NOT shrink", protocol)

    def test_protocol_hides_when_ellipsis_marker_cannot_fit(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "If even the ellipsis marker cannot fit, the entire dynamic element is hidden",
            protocol,
        )

    def test_protocol_clip_permits_rotated_geometry_outside_anchor(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "`clip` is the only policy that permits rotated semantic geometry outside the anchor",
            protocol,
        )

    def test_protocol_max_lines_participates_in_dynamic_layout(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("When present, `maxLines` participates in layout for every policy", protocol)
        self.assertIn("When `maxLines` is absent there is no independent line-count limit", protocol)
        self.assertIn("Each `shrink` candidate is laid out and wrapped again", protocol)

    def test_mapping_overflow_does_not_invalidate_theme(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Mapping-derived overflow MUST NOT invalidate the Theme", protocol)

    def test_empty_content_is_no_draw_not_overflow(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Empty content is nothing to render", protocol)
        self.assertIn("produces no pixels and does not enter fit or overflow processing", protocol)

    def test_schema_describes_global_rotation_and_overflow_semantics(self) -> None:
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
        properties = schema["$defs"]["anchor"]["properties"]
        overflow = properties["overflowPolicy"]
        minimum_scale = properties["minimumScale"]
        rotation = properties["rotation"]
        max_lines = properties["maxLines"]

        self.assertEqual(["ellipsis", "shrink", "clip", "hide"], overflow["enum"])
        self.assertIn("post-rotation fit", overflow["description"])
        self.assertEqual((0, 1), (minimum_scale["exclusiveMinimum"], minimum_scale["maximum"]))
        self.assertIn("dynamic element is hidden", minimum_scale["description"])
        self.assertIn("Clockwise degrees", rotation["description"])
        self.assertIn("geometric center of the anchor bounds", rotation["description"])
        self.assertIn("When absent, there is no independent line-count limit", max_lines["description"])

    def test_decomposition_guides_rotated_overflow_authoring(self) -> None:
        decomposition = _normalized_document(DECOMPOSITION_PATH)

        for required_text in (
            "Authors MUST reserve enough anchor area",
            "`shrink` may make the entire element disappear",
            "Choose `clip` only when cropped output",
            "authors MUST select `ellipsis` or `clip`, not `shrink`",
        ):
            self.assertIn(required_text, decomposition)

    def test_outline_width_is_full_centered_stroke_width(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "`outlineRole.width` is the full width of a centered stroke in Reference Space",
            protocol,
        )

    def test_outline_outward_radius_is_half_width(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("extends `width / 2` inward and `width / 2` outward", protocol)

    def test_zero_outline_width_adds_no_extent(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("A zero width contributes no outline pixels or extra semantic extent", protocol)

    def test_shrink_scales_outline_width(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("fill size and outline width are multiplied by `s`", protocol)

    def test_shadow_offset_is_applied_after_rotation(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Shadow offset is applied after rotation", protocol)

    def test_shadow_offset_uses_final_screen_axes(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("in final Reference screen axes", protocol)

    def test_positive_shadow_offsets_point_right_and_down(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("(`+x` right, `+y` down)", protocol)

    def test_shadow_blur_is_gaussian_sigma(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("`shadowRole.blur` is Gaussian sigma in Reference Space", protocol)

    def test_shadow_support_radius_is_three_sigma(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("`supportRadius = 3 * blur`", protocol)

    def test_zero_blur_is_hard_shadow(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("`blur = 0` is a hard shadow with no blur expansion", protocol)

    def test_shadow_alpha_is_zero_outside_finite_support(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Alpha outside that support MUST be zero", protocol)

    def test_shrink_scales_shadow_offset(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("authored shadow offsets and blur sigma are all multiplied by `s`", protocol)

    def test_shrink_scales_shadow_blur(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("support radius is `3 * blur * s`", protocol)

    def test_fit_includes_visible_outline(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("visible centered-outline geometry", protocol)
        self.assertIn("the entire union MUST fit inside the anchor", protocol)

    def test_fit_includes_visible_shadow(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("visible finite shadow-support geometry", protocol)
        self.assertIn("the entire union MUST fit inside the anchor", protocol)

    def test_fill_fit_with_effect_overflow_is_not_fit(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "If fill fits but a visible outline or shadow exceeds the anchor, the styled element does not fit",
            protocol,
        )

    def test_clip_allows_styled_effect_overflow(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "permits the complete styled geometry to extend outside the anchor before final clipping",
            protocol,
        )

    def test_non_clip_safety_clip_is_not_effect_fallback(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("it MUST NOT be used to crop an ordinary outline or shadow into compliance", protocol)

    def test_shadow_bounds_formula_is_frozen(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        for required_text in (
            "S.left = R.left + dx - radius",
            "S.top = R.top + dy - radius",
            "S.right = R.right + dx + radius",
            "S.bottom = R.bottom + dy + radius",
        ):
            self.assertIn(required_text, protocol)

    def test_effect_geometry_uses_continuous_reference_space(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Styled Semantic Geometry is the union", protocol)
        self.assertIn("continuous Reference-space text or glyph path", protocol)

    def test_effect_semantics_are_dpi_independent(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Styled effect geometry and fit decisions are DPI-independent", protocol)

    def test_outline_follows_element_rotation(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("fill plus outline rotate together around the anchor center", protocol)

    def test_shadow_source_uses_rotated_silhouette(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "raster shadow source silhouette is the rotated fill path plus any visible round-join/round-cap outline",
            protocol,
        )

    def test_shadow_offset_does_not_rotate_with_element(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("MUST NOT rotate with the element", protocol)

    def test_transparent_outline_is_ignored(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "outline whose resolved color alpha is zero is neither drawn nor included in semantic extent",
            protocol,
        )

    def test_transparent_shadow_is_ignored(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "shadow whose resolved color alpha is zero is neither drawn nor included in semantic extent",
            protocol,
        )

    def test_transparent_fill_can_still_have_visible_effects(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("an outline-only or shadow-only dynamic element is valid", protocol)
        self.assertIn("even when the fill itself is transparent", protocol)

    def test_ellipsis_recomputes_styled_bounds(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "Each ellipsis candidate MUST recompute `F`, `O`, `R`, `S`, and Styled Semantic Bounds",
            protocol,
        )

    def test_shrink_recomputes_styled_bounds(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Each shrink candidate MUST repeat layout and recompute `F`", protocol)

    def test_hide_uses_complete_styled_bounds(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("`hide` uses the same `F -> O -> R -> S` authority at authored scale `1.0`", protocol)

    def test_effects_do_not_create_max_lines(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Effects do not create additional logical lines", protocol)
        self.assertIn("`maxLines` controls only line generation", protocol)

    def test_schema_describes_effect_extent_contract(self) -> None:
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
        styles = schema["$defs"]["styles"]["properties"]
        outline = styles["outlineRoles"]
        outline_width = outline["additionalProperties"]["properties"]["width"]
        shadow = styles["shadowRoles"]
        shadow_properties = shadow["additionalProperties"]["properties"]

        self.assertIn("participates in Styled Semantic Geometry", outline["description"])
        self.assertIn("Full width of a centered stroke", outline_width["description"])
        self.assertIn("inflate the aligned fill/path AABB by width/2", outline_width["description"])
        self.assertIn("applied after element rotation", shadow_properties["offsetX"]["description"])
        self.assertIn("positive is right", shadow_properties["offsetX"]["description"])
        self.assertIn("positive is down", shadow_properties["offsetY"]["description"])
        self.assertIn("Gaussian sigma", shadow_properties["blur"]["description"])
        self.assertIn("three-sigma support", shadow_properties["blur"]["description"])

    def test_decomposition_guides_effect_extent_authoring(self) -> None:
        decomposition = _normalized_document(DECOMPOSITION_PATH)

        for required_text in (
            "Reserve anchor space for the complete styled element",
            "visible outline is a centered stroke",
            "finite three-sigma blur support",
            "`clip` is the only policy under which cropping these effects",
            "fully transparent outline or shadow does not affect fit",
            "outline-only or shadow-only element",
        ):
            self.assertIn(required_text, decomposition)

    def test_semantic_outline_authority_inflates_fill_aabb(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Let `F` be the aligned, unrotated continuous fill/path AABB", protocol)
        self.assertIn("O = inflate(F, r)", protocol)

    def test_actual_stroked_bounds_are_not_fit_authority(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn(
            "actual platform stroked-path bounds, GDI stroker bounds, and final raster pixels are not fit authority",
            protocol,
        )

    def test_outline_raster_requires_round_join(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("centered stroke with round line joins", protocol)

    def test_outline_raster_requires_round_cap(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("and round line caps", protocol)

    def test_outline_raster_prohibits_miter_join(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Miter, miter-clipped, bevel, platform-default joins", protocol)
        self.assertIn("are forbidden", protocol)

    def test_rotated_outline_bounds_use_four_rectangle_corners(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("transforms all four corners of `O`", protocol)
        self.assertIn("R = AABB(rotateCorners(O, anchorCenter, rotation))", protocol)

    def test_shadow_base_uses_normative_rotated_rectangle(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("semantic base authority is the normative rectangle `R`", protocol)

    def test_shrink_recomputes_f_o_r_s_at_each_scale(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Each shrink candidate MUST repeat layout and recompute `F`", protocol)
        self.assertIn("scaled outline radius, `O`, `R`, scaled shadow offset and blur, `S`", protocol)
        self.assertIn("scaling a previously rotated AABB is forbidden", protocol)

    def test_ellipsis_recomputes_f_o_r_s_for_each_candidate(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("Each ellipsis candidate MUST recompute `F`, `O`, `R`, `S`", protocol)
        self.assertIn("without consulting platform stroke bounds", protocol)

    def test_invisible_outline_collapses_o_to_f(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("If the outline is invisible or `w = 0`, `O = F`", protocol)
        self.assertIn("outline whose resolved color alpha is zero is neither drawn", protocol)

    def test_shadow_only_element_retains_source_geometry_authority(self) -> None:
        protocol = _normalized_document(PROTOCOL_PATH)

        self.assertIn("`S` when only shadow is visible", protocol)
        self.assertIn("glyph/path geometry remains available as the source for a visible outline or shadow", protocol)

    def test_schema_describes_normative_outline_rectangle_authority(self) -> None:
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
        styles = schema["$defs"]["styles"]["properties"]
        outline_width = styles["outlineRoles"]["additionalProperties"]["properties"]["width"]
        shadow = styles["shadowRoles"]

        self.assertIn("inflate the aligned fill/path AABB by width/2", outline_width["description"])
        self.assertIn("actual stroker bounds never redefine fit", outline_width["description"])
        self.assertIn("Raster joins and caps are round", outline_width["description"])
        self.assertIn("normative rotated fill-plus-outline rectangle R", shadow["description"])

    def test_decomposition_requires_conservative_outline_space(self) -> None:
        decomposition = _normalized_document(DECOMPOSITION_PATH)

        for required_text in (
            "conservative semantic rectangle",
            "inflate every side by `outline.width / 2`",
            "bound its four transformed corners",
            "actual stroked-path bounds never replace the contract rectangle",
            "Raster outlines use round joins and round caps",
        ):
            self.assertIn(required_text, decomposition)

    def test_valid_radial_6_example_passes(self) -> None:
        report = validate_theme_v2_document(_example())

        self.assertEqual("radial-6", report.layout_profile)
        self.assertEqual(7, report.state_count)
        self.assertEqual("full-state-frame", report.render_strategy)

    def test_valid_radial_8_manifest_passes(self) -> None:
        report = validate_theme_v2_document(_radial_8())

        self.assertEqual("radial-8", report.layout_profile)
        self.assertEqual(9, report.state_count)

    def test_legacy_origin_field_is_rejected(self) -> None:
        manifest = _example()
        reference_scale = manifest["referenceScale"]
        reference_scale["origin"] = reference_scale.pop("contentOrigin")  # type: ignore[union-attr]

        self.assert_invalid(manifest, "contentOrigin|origin")

    def test_content_origin_is_required(self) -> None:
        manifest = _example()
        del manifest["referenceScale"]["contentOrigin"]  # type: ignore[index]

        self.assert_invalid(manifest, "contentOrigin")

    def test_activation_anchor_is_required(self) -> None:
        manifest = _example()
        del manifest["placement"]  # type: ignore[arg-type]

        self.assert_invalid(manifest, "placement")

    def test_negative_content_origin_is_rejected(self) -> None:
        manifest = _example()
        manifest["referenceScale"]["contentOrigin"]["x"] = -0.01  # type: ignore[index]

        self.assert_invalid(manifest, "contentOrigin|greater than or equal")

    def test_content_overflow_is_rejected(self) -> None:
        manifest = _example()
        manifest["referenceScale"]["contentOrigin"]["x"] = 0.01  # type: ignore[index]

        self.assert_invalid(manifest, "content rectangle lies outside Logical Surface")

    def test_negative_activation_anchor_is_rejected(self) -> None:
        manifest = _example()
        manifest["placement"]["activationAnchor"]["x"] = -0.01  # type: ignore[index]

        self.assert_invalid(manifest, "activationAnchor|greater than or equal")

    def test_activation_anchor_beyond_logical_surface_is_rejected(self) -> None:
        manifest = _example()
        manifest["placement"]["activationAnchor"]["x"] = 400.01  # type: ignore[index]

        self.assert_invalid(manifest, "activationAnchor lies outside Logical Surface")

    def test_non_square_centered_content_is_valid(self) -> None:
        manifest = _example()

        transform = reference_to_logical_transform(manifest)
        report = validate_theme_v2_document(manifest)

        self.assertEqual("radial-6", report.layout_profile)
        self.assertAlmostEqual(0.5, transform.scale)
        self.assertEqual((0.0, 75.0), (transform.content_x, transform.content_y))
        self.assertEqual((400.0, 250.0), (transform.content_width, transform.content_height))

    def test_non_square_non_centered_content_is_valid(self) -> None:
        manifest = _example()
        manifest["referenceScale"] = {
            "logicalWidth": 500,
            "logicalHeight": 300,
            "fit": "contain",
            "contentOrigin": {"x": 20, "y": 0},
        }
        manifest["placement"] = {"activationAnchor": {"x": 100, "y": 150}}

        validate_theme_v2_document(manifest)
        transform = reference_to_logical_transform(manifest)

        self.assertAlmostEqual(0.6, transform.scale)
        self.assertEqual((480.0, 300.0), (transform.content_width, transform.content_height))
        self.assertEqual((20.0, 0.0), transform.map_point(0, 0))
        self.assertEqual((500.0, 300.0), transform.map_point(800, 500))

    def test_reference_to_logical_point_mapping_is_exact(self) -> None:
        transform = reference_to_logical_transform(_example())

        self.assertEqual((0.0, 75.0), transform.map_point(0, 0))
        self.assertEqual((400.0, 325.0), transform.map_point(800, 500))
        self.assertEqual((200.0, 200.0), transform.map_point(400, 250))

    def test_logical_surface_bounds_are_explicit(self) -> None:
        transform = reference_to_logical_transform(_example())

        self.assertEqual(400.0, transform.logical_width)
        self.assertEqual(400.0, transform.logical_height)

    def test_show_at_activation_anchor_semantics_are_documented(self) -> None:
        protocol = PROTOCOL_PATH.read_text(encoding="utf-8")

        for required_text in (
            "hwndLeft = screenPointPhysical.x - LogicalEdgeToPhysical(activationAnchor.x, dpi)",
            "hwndTop  = screenPointPhysical.y - LogicalEdgeToPhysical(activationAnchor.y, dpi)",
            "affects visual overlay placement only",
        ):
            self.assertIn(required_text, protocol)
        self.assertRegex(
            protocol,
            r"does not define the Visual Surface center or HWND\s+anchor",
        )

    def test_v1_wheel_center_maps_to_legacy_activation_anchor(self) -> None:
        legacy_logical_size = 280.0
        mapped_center = 627.0 * legacy_logical_size / 1254.0

        self.assertEqual(140.0, mapped_center)
        for dpi, expected in ((96, 140), (120, 175), (144, 210), (168, 245), (192, 280)):
            self.assertEqual(expected, logical_edge_to_physical(mapped_center, dpi))

    def test_invalid_logical_surface_size_is_rejected(self) -> None:
        manifest = _example()
        manifest["referenceScale"]["logicalWidth"] = 4097  # type: ignore[index]

        self.assert_invalid(manifest, "4096|maximum")

    def test_non_finite_coordinate_is_rejected(self) -> None:
        manifest = _example()
        manifest["placement"]["activationAnchor"]["x"] = math.nan  # type: ignore[index]

        self.assert_invalid(manifest, "finite|nan")

    def test_dpi_rounding_contract_matches_stable_away_from_zero_rule(self) -> None:
        protocol = PROTOCOL_PATH.read_text(encoding="utf-8")

        self.assertIn("midpoint = AwayFromZero", protocol)
        self.assertIn("physical width is `right - left`", protocol)
        self.assertEqual(1, logical_edge_to_physical(0.4, 120))
        self.assertEqual(-1, logical_edge_to_physical(-0.4, 120))
        self.assertEqual(500, logical_edge_to_physical(400, 120))

    def test_content_containment_tolerance_is_deterministic(self) -> None:
        manifest = _example()
        manifest["referenceScale"]["contentOrigin"]["x"] = 5e-10  # type: ignore[index]

        validate_theme_v2_document(manifest)

        manifest["referenceScale"]["contentOrigin"]["x"] = 2e-9  # type: ignore[index]
        self.assert_invalid(manifest, "content rectangle lies outside Logical Surface")

    def test_layered_state_manifest_passes(self) -> None:
        manifest = _example()
        manifest["renderStrategy"] = "layered-state"
        required = manifest["capabilities"]["required"]  # type: ignore[index]
        required.remove("fullStateFrame")  # type: ignore[union-attr]
        required.append("layeredState")  # type: ignore[union-attr]

        report = validate_theme_v2_document(manifest)

        self.assertEqual("layered-state", report.render_strategy)

    def test_procedural_authoring_is_rejected_as_render_strategy(self) -> None:
        manifest = _example()
        manifest["renderStrategy"] = "procedural-authoring"

        self.assert_invalid(manifest, "unsupported renderStrategy: procedural-authoring")

    def test_authoring_metadata_does_not_change_runtime_capabilities(self) -> None:
        manifest = _example()
        manifest["authoring"] = {"method": "procedural"}

        report = validate_theme_v2_document(
            manifest,
            supported_capabilities={
                "fullStateFrame", "dynamicAnchors", "maskAssets", "instantTransitions"
            },
        )

        self.assertEqual("full-state-frame", report.render_strategy)

    def test_invalid_authoring_method_fails(self) -> None:
        manifest = _example()
        manifest["authoring"] = {"method": "runtime-script"}

        self.assert_invalid(manifest, "unsupported authoring method: runtime-script")

    def test_full_state_frame_rejects_multiple_compatible_layouts(self) -> None:
        manifest = _example()
        manifest["compatibleLayouts"] = ["radial-6", "radial-8"]

        self.assert_invalid(manifest, "full-state-frame requires exactly one compatible layout")

    def test_layered_state_accepts_coherent_multiple_layouts(self) -> None:
        manifest = _radial_8()
        manifest["layoutProfile"] = "radial-6"
        manifest["compatibleLayouts"] = ["radial-6", "radial-8"]
        manifest["renderStrategy"] = "layered-state"
        required = manifest["capabilities"]["required"]  # type: ignore[index]
        required.remove("fullStateFrame")  # type: ignore[union-attr]
        required.append("layeredState")  # type: ignore[union-attr]

        report = validate_theme_v2_document(manifest)

        self.assertEqual(("radial-6", "radial-8"), report.compatible_layouts)

    def test_missing_idle_state_fails(self) -> None:
        manifest = _example()
        del manifest["states"]["idle"]  # type: ignore[index]

        self.assert_invalid(manifest, "missing required state.*idle")

    def test_missing_selected_state_fails(self) -> None:
        manifest = _example()
        del manifest["states"]["selected-4"]  # type: ignore[index]

        self.assert_invalid(manifest, "missing required state.*selected-4")

    def test_duplicate_state_json_key_fails_during_load(self) -> None:
        source = EXAMPLE_PATH.read_text(encoding="utf-8")
        duplicate = source.replace(
            '"idle": {',
            '"idle": {"slotId": null, "assets": {"stateFrame": "states/idle.png"}},\n    "idle": {',
            1,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(duplicate, encoding="utf-8")

            with self.assertRaisesRegex(ThemeV2ValidationError, "duplicate JSON field: idle"):
                load_theme_v2_manifest(path)

    def test_duplicate_layer_id_fails(self) -> None:
        manifest = _example()
        manifest["layers"].append(copy.deepcopy(manifest["layers"][0]))  # type: ignore[union-attr,index]

        self.assert_invalid(manifest, "duplicate layer id: background")

    def test_duplicate_anchor_id_fails(self) -> None:
        manifest = _example()
        manifest["dynamicAnchors"].append(copy.deepcopy(manifest["dynamicAnchors"][0]))  # type: ignore[union-attr,index]

        self.assert_invalid(manifest, "duplicate anchor id: actionLabel")

    def test_layer_bounds_outside_canvas_fails(self) -> None:
        manifest = _example()
        manifest["layers"][0]["bounds"]["width"] = 1255  # type: ignore[index]

        self.assert_invalid(manifest, r"layers\[0\]\.bounds lies outside referenceCanvas")

    def test_anchor_bounds_outside_canvas_fails(self) -> None:
        manifest = _example()
        manifest["dynamicAnchors"][0]["bounds"]["x"] = -1  # type: ignore[index]

        self.assert_invalid(manifest, r"dynamicAnchors\[0\]\.bounds lies outside referenceCanvas")

    def test_unknown_render_strategy_fails(self) -> None:
        manifest = _example()
        manifest["renderStrategy"] = "magic-css"

        self.assert_invalid(manifest, "unsupported renderStrategy: magic-css")

    def test_alpha_mode_only_accepts_straight_source_assets(self) -> None:
        manifest = _example()
        manifest["referenceCanvas"]["alphaMode"] = "premultiplied"  # type: ignore[index]

        self.assert_invalid(manifest, "requires sRGB with straight alpha")

    def test_package_and_runtime_alpha_contract_is_documented(self) -> None:
        protocol = PROTOCOL_PATH.read_text(encoding="utf-8")

        for required_text in (
            "SOURCE / PACKAGE SPACE",
            "PNG-decoded RGBA",
            "straight alpha",
            "PREMULTIPLIED RESAMPLING SPACE",
            "Format32bppPArgb",
            "directly to the final Physical target size",
            "UpdateLayeredWindow",
            "semi-transparent red edge",
            "transparent colored pixel",
        ):
            self.assertIn(required_text, protocol)

    def test_unknown_surface_fails(self) -> None:
        manifest = _example()
        manifest["surface"] = "settings-page"

        self.assert_invalid(manifest, "unsupported surface: settings-page")

    def test_package_revision_is_not_protocol_version(self) -> None:
        manifest = _example()
        manifest["packageRevision"] = 0

        self.assert_invalid(manifest, "packageRevision must be at least 1")

    def test_unknown_layout_fails(self) -> None:
        manifest = _example()
        manifest["layoutProfile"] = "grid-3x2"

        self.assert_invalid(manifest, "unsupported layoutProfile: grid-3x2")

    def test_missing_hash_record_fails(self) -> None:
        manifest = _example()
        del manifest["assetHashes"]["states/idle.png"]  # type: ignore[index]

        self.assert_invalid(manifest, "missing asset hash record.*states/idle.png")

    def test_orphan_hash_record_fails(self) -> None:
        manifest = _example()
        manifest["assetHashes"]["unused.png"] = "C" * 64  # type: ignore[index]

        self.assert_invalid(manifest, "orphan asset hash record.*unused.png")

    def test_parent_path_traversal_fails(self) -> None:
        manifest = _example()
        _replace_asset_path(manifest, "layers/background.png", "../background.png")

        self.assert_invalid(manifest, "unsafe path segment|package-relative")

    def test_absolute_path_fails(self) -> None:
        manifest = _example()
        _replace_asset_path(manifest, "layers/background.png", "C:/theme/background.png")

        self.assert_invalid(manifest, "package-relative POSIX path")

    def test_ownership_conflict_fails(self) -> None:
        manifest = _example()
        manifest["elementOwnership"][0]["owner"] = "DYNAMIC"  # type: ignore[index]
        manifest["elementOwnership"][0]["contentKey"] = "badDynamicOwner"  # type: ignore[index]

        self.assert_invalid(manifest, "conflicts with layer ownership")

    def test_layer_kind_ownership_mismatch_fails(self) -> None:
        manifest = _example()
        manifest["layers"][1]["ownership"] = "STATIC"  # type: ignore[index]

        self.assert_invalid(manifest, "ownership must be STATE_ASSET for stateAsset")

    def test_duplicate_element_ownership_fails(self) -> None:
        manifest = _example()
        manifest["elementOwnership"].append(copy.deepcopy(manifest["elementOwnership"][0]))  # type: ignore[union-attr,index]

        self.assert_invalid(manifest, "duplicate element ownership: backgroundArt")

    def test_unknown_style_role_fails(self) -> None:
        manifest = _example()
        manifest["dynamicAnchors"][0]["styleRole"] = "missingStyle"  # type: ignore[index]

        self.assert_invalid(manifest, "unknown styleRole: missingStyle")

    def test_unknown_glyph_role_fails(self) -> None:
        manifest = _example()
        manifest["dynamicAnchors"][1]["glyphRole"] = "missingGlyph"  # type: ignore[index]

        self.assert_invalid(manifest, "unknown glyphRole: missingGlyph")

    def test_unknown_style_dependency_fails(self) -> None:
        manifest = _example()
        manifest["styles"]["dynamicRoles"]["actionLabel"]["fontRole"] = "missingFont"  # type: ignore[index]

        self.assert_invalid(manifest, "fontRole references unknown role: missingFont")

    def test_unsupported_required_capability_context_fails(self) -> None:
        manifest = _example()

        with self.assertRaisesRegex(ThemeV2ValidationError, "unsupported required capability.*maskAssets"):
            validate_theme_v2_document(
                manifest,
                supported_capabilities={"fullStateFrame", "dynamicAnchors", "instantTransitions"},
            )

    def test_supported_capability_context_passes(self) -> None:
        manifest = _example()

        validate_theme_v2_document(
            manifest,
            supported_capabilities={
                "fullStateFrame", "dynamicAnchors", "maskAssets", "instantTransitions"
            },
        )

    def test_state_slot_id_must_match_state_name(self) -> None:
        manifest = _example()
        manifest["states"]["selected-2"]["slotId"] = 3  # type: ignore[index]

        self.assert_invalid(manifest, "state/slot mismatch for selected-2")

    def test_state_must_fit_compatible_layout(self) -> None:
        manifest = _example()
        manifest["states"]["selected-7"] = {"slotId": 7, "assets": {"stateFrame": "states/selected-7.png"}}  # type: ignore[index]
        manifest["assetHashes"]["states/selected-7.png"] = "F" * 64  # type: ignore[index]
        manifest["capabilities"]["required"].append("futureStates")  # type: ignore[index]

        self.assert_invalid(manifest, "state selected-7 exceeds compatible layout slot count 6")

    def test_state_binding_to_unknown_layer_fails(self) -> None:
        manifest = _example()
        manifest["states"]["idle"]["assets"]["missingLayer"] = "states/other.png"  # type: ignore[index]
        manifest["assetHashes"]["states/other.png"] = "D" * 64  # type: ignore[index]

        self.assert_invalid(manifest, "references unknown/non-state layer: missingLayer")

    def test_required_state_layer_binding_fails(self) -> None:
        manifest = _example()
        del manifest["states"]["selected-3"]["assets"]["stateFrame"]  # type: ignore[index]
        del manifest["assetHashes"]["states/selected-3.png"]  # type: ignore[index]

        self.assert_invalid(manifest, "missing required layer binding.*stateFrame")

    def test_duplicate_glyph_fallback_source_type_fails(self) -> None:
        manifest = _example()
        sources = manifest["glyphs"]["roles"]["primaryAction"]["keyboard"]["sources"]  # type: ignore[index]
        sources.append({"type": "text", "styleRole": "actionGlyphText"})  # type: ignore[union-attr]

        self.assert_invalid(manifest, "repeats a source type")

    def test_dynamic_anchor_requires_matching_ownership(self) -> None:
        manifest = _example()
        manifest["elementOwnership"][2]["element"] = "renamedActionLabel"  # type: ignore[index]

        self.assert_invalid(manifest, "dynamic anchor actionLabel requires matching DYNAMIC elementOwnership")

    def test_unknown_visibility_selector_fails(self) -> None:
        manifest = _example()
        manifest["layers"][0]["visibleStates"] = ["hover"]  # type: ignore[index]

        self.assert_invalid(manifest, "unknown state selector: hover")

    def test_non_instant_transition_fails(self) -> None:
        manifest = _example()
        manifest["transitions"]["mode"] = "crossfade"  # type: ignore[index]

        self.assert_invalid(manifest, "permits only instant")

    def test_future_state_requires_capability(self) -> None:
        manifest = _example()
        manifest["states"]["confirm"] = {"slotId": None, "assets": {"stateFrame": "states/confirm.png"}}  # type: ignore[index]
        manifest["assetHashes"]["states/confirm.png"] = "E" * 64  # type: ignore[index]

        self.assert_invalid(manifest, "future states require the futureStates capability")

    def test_v1_manifest_is_not_interpreted_as_v2(self) -> None:
        v1 = {
            "id": "v1-fixture",
            "name": "V1 Fixture",
            "version": 1,
            "layoutProfile": "radial-6",
            "slotCount": 6,
            "base": "base.png",
            "selected": "selected.png",
            "layout": "layout.json",
            "selectionAssetMode": "canonical-transform",
        }

        self.assert_invalid(v1, "missing required field.*protocolVersion")

    def test_radial_8_capability_remains_stable_and_integrated(self) -> None:
        contract = PROFILE_CONTRACTS["radial-8"]

        self.assertEqual("stable", contract.status)
        self.assertTrue(contract.runtime_integrated)

    def test_check_assets_accepts_small_synthetic_files(self) -> None:
        manifest = _example()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index, relative_path in enumerate(sorted(manifest["assetHashes"])):  # type: ignore[arg-type]
                asset = root / relative_path
                asset.parent.mkdir(parents=True, exist_ok=True)
                content = f"synthetic-{index}".encode()
                asset.write_bytes(content)
                manifest["assetHashes"][relative_path] = hashlib.sha256(content).hexdigest()  # type: ignore[index]
            path = root / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")

            report = validate_theme_v2(path, check_assets=True)

            self.assertTrue(report.assets_verified)

    def test_check_assets_detects_hash_mismatch(self) -> None:
        manifest = _example()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative_path in manifest["assetHashes"]:  # type: ignore[union-attr]
                asset = root / relative_path
                asset.parent.mkdir(parents=True, exist_ok=True)
                asset.write_bytes(b"same-content")
            path = root / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")

            with self.assertRaisesRegex(ThemeV2ValidationError, "asset hash mismatch"):
                validate_theme_v2(path, check_assets=True)


if __name__ == "__main__":
    unittest.main()
