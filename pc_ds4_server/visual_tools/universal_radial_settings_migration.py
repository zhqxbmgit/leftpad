"""Pure Phase 0 prototypes for Universal radial settings migration.

This module is deliberately independent from RadialMenuSettingsStore and all
production Runtime code.  It exists so the frozen contract math can be tested
before the later settings-migration implementation phase.
"""

from __future__ import annotations

from fractions import Fraction


LEGACY_SETTINGS_SEMANTIC_REVISION = 1
UNIVERSAL_SETTINGS_SEMANTIC_REVISION = 2


def legacy_surface_scale_ratio(base_canvas_size: int, scale_percent: int) -> Fraction:
    """Return the exact BaseCanvasSize/ScalePercent migration ratio."""

    if type(base_canvas_size) is not int or base_canvas_size <= 0:
        raise ValueError("base_canvas_size must be a positive integer")
    if type(scale_percent) is not int or scale_percent <= 0:
        raise ValueError("scale_percent must be a positive integer")
    return Fraction(base_canvas_size, 280) * Fraction(scale_percent, 100)


def legacy_surface_scale(base_canvas_size: int, scale_percent: int) -> float:
    """Return the double-precision presentation multiplier for migration."""

    return float(legacy_surface_scale_ratio(base_canvas_size, scale_percent))


def _round_nonnegative_away_from_zero(numerator: int, denominator: int) -> int:
    if numerator < 0 or denominator <= 0:
        raise ValueError("rounding inputs must be non-negative with a positive denominator")
    return (2 * numerator + denominator) // (2 * denominator)


def migrate_legacy_dead_alpha(old_value: int, old_default: int) -> int:
    """Migrate Fill/Border/Highlight alpha to Universal strength semantics."""

    if type(old_value) is not int or not 0 <= old_value <= 255:
        raise ValueError("old_value must be an integer from 0 through 255")
    if type(old_default) is not int or not 1 <= old_default <= 255:
        raise ValueError("old_default must be an integer from 1 through 255")
    if old_value > old_default:
        return 255
    return _round_nonnegative_away_from_zero(255 * old_value, old_default)


def migrate_legacy_text_alpha(old_text_alpha: int) -> int:
    """Migrate the legacy min(TextAlpha, 235) behavior to Universal strength."""

    if type(old_text_alpha) is not int or not 0 <= old_text_alpha <= 255:
        raise ValueError("old_text_alpha must be an integer from 0 through 255")
    old_effective = min(old_text_alpha, 235)
    return _round_nonnegative_away_from_zero(255 * old_effective, 235)
