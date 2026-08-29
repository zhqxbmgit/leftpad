# LeftPad Universal Radial Protocol V3

- Status: Phase 0 normative contract
- Protocol version: 3
- Render strategy: universal-radial
- Surface: radial-overlay

This document freezes product semantics, package semantics, canonical geometry,
semantic ownership, validation, migration math, authoring outputs, and fidelity
requirements for Universal radial themes. It is not a Runtime implementation.
The words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, and
MAY are normative.

## 1. Scope and version identity

Three protocol families remain distinct:

- V1 is the legacy compatibility protocol and remains on its existing
  loader/runtime path.
- V2 is the full-state-frame compatibility protocol and retains its existing
  meanings, schema, loader, and runtime path.
- V3 is the strict Universal semantic radial protocol defined here.

A V3 package MUST declare protocolVersion = 3, renderStrategy =
universal-radial, and surface = radial-overlay. packageRevision identifies a
revision of one package. Neither field is the app-level Radial settings
semantic revision.

V3 does not redefine V1 or V2. No V3 rule may be inferred as a change to the
V2 contract.

## 2. Universal radial setting rule

Every setting classified as a Universal radial setting MUST produce a
deterministic, testable Runtime effect for every production radial theme.
Consequently, Universal radial behavior MUST NOT contain:

- a V1-only editable control;
- a V2 or V3 silent no-op;
- the same setting disabled only for selected theme IDs;
- a Theme-ID hardcode that substitutes for capability or contract data;
- a successful save whose value is not consumed by the renderer, input
  pipeline, or mapping pipeline named by the setting;
- Runtime color, RGB, luma, edge, or alpha-threshold analysis used to guess
  semantic ownership.

If a radial setting cannot have author-independent product semantics, it MUST
be removed from the production Radial Settings UI. Theme authors may preserve
their local art offsets, materials, alignments, and rotations only inside the
semantic boundaries specified here.

ReceiverUiScalePercent is a RECEIVER SHELL SETTING, not a Universal radial
setting. It MAY remain on the same Settings page, but its consumer and contract
are the Receiver shell. Sharing a page does not make it a radial visual or
input parameter.

## 3. Canonical Radial Units

CRU means Canonical Radial Units.

- 280 CRU is the nominal radial design width.
- 140 CRU is the nominal radial radius.
- CRU is not a PNG pixel.
- CRU is not a monitor physical pixel.
- CRU is not tied to a theme reference resolution.

Reference assets may be 512, 1024, 1254, or another validated square
resolution. referenceScale maps that square to a 280 by 280 CRU nominal
logical surface. The normative transform order is:

    reference semantic artwork
    -> authored nominal logical coordinates
    -> CRU-based user geometry transform
    -> ScalePercent presentation transform
    -> monitor-DPI physical coordinates

Implementations MUST retain sufficient precision through the CRU and
presentation stages. Integer pixel rounding occurs only at the final physical
edge/sampling boundary and MUST follow the platform placement contract.

## 4. Universal neutral vector

The Universal neutral vector is:

| Setting | Neutral |
| --- | ---: |
| ScalePercent | 100 |
| HubRadius | 35 CRU |
| PetalInnerRadius | 42 CRU |
| PetalOuterRadius | 103 CRU |
| PetalGapDegrees | 4 degrees |
| TextRadius | 73 CRU |
| FontSize | 15 |
| FillAlpha | 255 |
| BorderAlpha | 255 |
| TextAlpha | 255 |
| HighlightAlpha | 255 |
| DoubleTapWindowMs | 150 ms |
| SelectionDeadZone | 28 DIP |
| SelectionPollIntervalMs | 16 ms |

At this vector, a valid package's semantic reconstruction MUST be raw-pixel
identical to its frozen identity state. Alpha values are strength controls;
255 means complete authored strength.

ReceiverUiScalePercent = 150 percent is the separate Receiver shell neutral.
It is not a member of the Universal neutral vector.

## 5. Universal radial settings semantics

The ranges below freeze the current product validation boundary. Relational
constraints are part of the range.

| Setting | Product meaning and coordinate space | Semantic owner/effect | Selection effect | Neutral | Validation range |
| --- | --- | --- | --- | --- | --- |
| VisualPackId | Select the validated package by identifier | Selects the complete render plan; MUST NOT branch settings behavior by ID | Selects the package layout only | Application production-default policy | Non-empty installed production-compatible identifier |
| MappingProfileId | Select the active action-map topology by identifier | Selects one mapping profile used by dynamic content/input | Selects profile slot topology | Active package layout profile | Non-empty supported profile identifier; slot count MUST match layout |
| MappingsByProfile | Per-profile slot-to-action mapping collection | DYNAMIC_SLOT_CONTENT and DYNAMIC_SELECTED_CONTENT values | Determines action bound to each existing slot; does not move boundaries | Product defaults for each profile | Each known profile has exactly its declared slot count and valid actions |
| ScalePercent | Uniform presentation multiplier of the theme nominal logical surface; normalized as a double-precision SurfaceScale | Art, semantic layers, dynamic anchors, activation-anchor offset, and final logical surface | None; MUST NOT scale SelectionDeadZone | 100 | Finite 240 / 7 through 400 percent, the exact closure of valid legacy BaseCanvasSize and ScalePercent combinations |
| HubRadius | Visual hub boundary in CRU | HUB geometry and adjoining semantic warp boundary | None | 35 CRU | Finite and 0 < H < I |
| PetalInnerRadius | Visual petal inner boundary in CRU | PETAL_FILL, PETAL_BORDER, PETAL_DETAIL, and SELECTED_EMPHASIS geometry | None | 42 CRU | Finite and H < I < O |
| PetalOuterRadius | Visual petal outer boundary in CRU | PETAL_FILL, PETAL_BORDER, PETAL_DETAIL, SELECTED_EMPHASIS, and outer-envelope transform | None | 103 CRU | Finite and I < O |
| PetalGapDegrees | Visual angular separation in degrees | Slot-owned petal geometry only | None; sectors remain continuous with no selection holes | 4 degrees | Finite, 0 through 12, and strictly less than 360 / slotCount |
| TextRadius | Radial position of each slot glyph-plus-label group in CRU; future UI label is 槽位内容半径 | DYNAMIC_SLOT_CONTENT group anchor | None | 73 CRU | Finite and I <= TextRadius <= O |
| FontSize | Product font-size strength relative to 15 | Dynamic text and text glyph fallback; never a vector-symbol scale | None | 15 | Finite 6 through 48 |
| FillAlpha | Strength of authored petal fill | PETAL_FILL only | None | 255 | Integer 0 through 255 |
| BorderAlpha | Strength of authored petal border | PETAL_BORDER only | None | 255 | Integer 0 through 255 |
| TextAlpha | Strength of all dynamic text, Runtime symbols, and text glyph fallback | DYNAMIC_SLOT_CONTENT and DYNAMIC_SELECTED_CONTENT pixels | None | 255 | Integer 0 through 255 |
| HighlightAlpha | Blend strength from base appearance to complete selected appearance | SELECTED_EMPHASIS only | None; selected action glyph/label remain visible independently | 255 | Integer 0 through 255 |
| DoubleTapWindowMs | Maximum elapsed input time for the existing double-tap gesture | Input state machine only | Gesture timing only | 150 ms | Integer 80 through 500 |
| SelectionDeadZone | Screen-logical distance from activation point before directional selection | Input state machine only; DIP/screen logical pixels | Directly sets dead-zone distance | 28 DIP | Integer 8 through 80 |
| SelectionPollIntervalMs | Stable input sampling interval | Input polling only | Sampling cadence only | 16 ms | Integer 8 through 50 |

VisualPackId, MappingProfileId, and MappingsByProfile are Universal routing and
content settings. DoubleTapWindowMs, SelectionDeadZone, and
SelectionPollIntervalMs are Universal input-only settings. Their lack of pixel
mutation is not a theme no-op because their radial Runtime consumer is
explicitly named above.

### 5.1 Receiver shell setting

| Setting | Product meaning | Consumer | Neutral | Validation range |
| --- | --- | --- | ---: | --- |
| ReceiverUiScalePercent | Receiver shell/Dreamscape presentation preset | Receiver shell only; never radial artwork, radial input, or selection | 150 percent | One of 100, 125, 150, 175, 200 |

ReceiverUiScalePercent is not a Universal radial visual parameter, MUST NOT be
placed in UniversalRadialParameters, and MUST NOT enter a
UniversalRadialRenderPlan. A production radial theme is not required to
consume it.

## 6. BaseCanvasSize deprecation and settings semantic revision

BaseCanvasSize is not a Universal editable setting. In the legacy V1 model:

    targetSize = BaseCanvasSize * ScalePercent / 100

Both terms express the same uniform logical presentation scale once the
Universal nominal width is fixed at 280 CRU, so BaseCanvasSize has no
independent observable.

The production UI MUST remove BaseCanvasSize in the future. The existing C#
and JSON property MUST remain temporarily as a legacy migration input. This
Phase 0 contract does not remove or change that property or its store.

The app settings document will carry an app-level
settingsSemanticRevision. This field is separate from protocolVersion and
packageRevision:

- missing or settingsSemanticRevision = 1 is the legacy revision and means
  values retain the existing V1-era meanings;
- settingsSemanticRevision = 2 is the Universal revision and means the neutral
  vector and Universal strength meanings in this document have already been
  applied.

Migration MUST be atomic and idempotent: only a legacy revision is transformed,
and the revision is advanced only with the transformed settings. Re-loading a
Universal revision MUST NOT transform it again. Production store work is
deferred to a later phase.

## 7. Legacy settings migration

### 7.1 BaseCanvasSize plus ScalePercent

The exact presentation multiplier is:

    effectiveScale =
        (legacy BaseCanvasSize / 280)
        * (legacy ScalePercent / 100)

The calculation MUST use exact rational semantics or double precision.
Implementations MUST NOT integer-round either factor before multiplication.
The resulting future SurfaceScale/ScalePercent representation is one
observable, not two. Universal validation accepts the exact legacy closure
12 / 35 through 4 as SurfaceScale (240 / 7 through 400 percent). The current
legacy C# integer property and its 60-through-140 UI control remain untouched
in Phase 0; a later production migration MUST preserve the double result rather
than clamp it back to that legacy UI interval.

| Legacy BaseCanvasSize | Legacy ScalePercent | Exact multiplier | Universal percentage |
| ---: | ---: | ---: | ---: |
| 280 | 100 | 1 | 100 |
| 280 | 120 | 6 / 5 | 120 |
| 336 | 100 | 6 / 5 | 120 |
| 320 | 125 | 10 / 7 | 142.857142857... |

If a later production storage range cannot represent the exact migrated
multiplier, that phase MUST define an explicit persistence policy. It MUST NOT
silently pre-round the Phase 0 formula.

### 7.2 Legacy dead-alpha controls

Legacy FillAlpha, BorderAlpha, and HighlightAlpha defaults visually represented
complete historical authored strength even though their stored defaults were
218, 100, and 80. For each of those controls:

    if oldValue <= oldDefault:
        newStrength = round(255 * oldValue / oldDefault)
    else:
        newStrength = 255

round means nearest integer with an exact midpoint rounded away from zero.
The old default therefore maps to 255. Values above the old default saturate
at 255. Zero remains zero.

### 7.3 Legacy TextAlpha

The legacy Runtime independently clamped effective text alpha at 235:

    oldEffective = min(oldTextAlpha, 235)
    newStrength = round(255 * oldEffective / 235)

The same deterministic rounding rule applies. Both oldTextAlpha = 240 and
oldTextAlpha = 255 map to newStrength = 255. Universal rendering MUST NOT
retain the legacy min(TextAlpha, 235) clamp.

## 8. Selection invariants

SelectionDeadZone is measured in DIP/screen logical pixels:

    physicalDeadZone = SelectionDeadZone * monitorDpi / 96

It MUST NOT be multiplied by ScalePercent, HubRadius, PetalInnerRadius,
PetalOuterRadius, TextRadius, BaseCanvasSize, theme nominal size, or reference
asset scale.

HubRadius, PetalInnerRadius, PetalOuterRadius, and PetalGapDegrees change visual
geometry only. They MUST NOT change slot sector topology, directional
boundaries, or the existing selection maximum radius. Every directional angle
belongs to a slot sector. PetalGapDegrees creates a visual gap and MUST NOT
create a selection hole.

## 9. V3 package shape

The normative closed shape is schemas/ui-theme-v3.schema.json. A package binds
one stable radial layout profile and its exact slot count. Required top-level
groups include:

- referenceCanvas, referenceScale, and placement;
- radialGeometry;
- identityStates;
- semanticLayers;
- masks;
- dynamicGroups;
- fallback;
- capabilities and instant transitions;
- the exact assetHashes closure.

Every object uses a strict additionalProperties policy. All asset paths are
forward-slash, package-relative safe paths. Absolute paths, drive paths,
backslashes, empty/dot/parent segments, alternate streams, and unapproved
characters are invalid.

Every referenced asset MUST have exactly one SHA-256 entry, and every hash
entry MUST be referenced. Asset existence, containment, PNG decoding, RGBA
mode, dimensions, and hash are required for a package candidate.

Windows device basenames, trailing-dot aliases, and paths that differ only by
case are unsafe and invalid. Every V3 raster asset path ends in .png.

V3 required capabilities are exactly the current frozen minimum:

- universalRadial;
- semanticLayers;
- dynamicAnchors;
- instantTransitions.

New capability names require a reviewed contract revision. V3 does not
mechanically inherit themeGlyphAssets, visualRegions, safeDynamicSurfaces,
futureStates, or occlusionLayers from V2.

fallback.onInvalidCandidate and fallback.onUnsupportedVersion MUST both be
retain-active. fallback.startupThemeId MUST be a syntactically valid theme
identifier resolved by the application fallback policy. A V3 package MUST NOT
embed a private Theme-ID-specific fallback algorithm.

## 10. Semantic ownership and z order

The minimum semantic ownership set is:

- exactly one STATIC_RESIDUAL;
- exactly one HUB;
- exactly one PETAL_FILL(slot) for every slot;
- exactly one PETAL_BORDER(slot) for every slot;
- zero or one PETAL_DETAIL(slot) for every slot;
- exactly one SELECTED_EMPHASIS(slot) for every slot;
- exactly one DYNAMIC_SLOT_CONTENT(slot) for every slot;
- exactly one DYNAMIC_SELECTED_CONTENT.

Every authored visible pixel MUST have one definite semantic owner in the
reviewed decomposition. Layers MAY overlap spatially, but all semantic and
dynamic zIndex values MUST be globally deterministic. Duplicate ownership of
the same role and slot is invalid.

STATIC_RESIDUAL holds authored pixels that do not belong to an adjustable
semantic role. HUB owns the center material. PETAL_FILL and PETAL_BORDER are
independently strength-adjustable. PETAL_DETAIL is optional theme detail that
does not become fill or border merely because of color or edge location.
SELECTED_EMPHASIS owns the complete authored selected appearance for one slot.

Runtime MUST NOT infer ownership from RGB, hue, luma, edge detection, connected
components, alpha threshold, filename convention, or a production Theme ID.
Runtime consumes descriptors and validated explicit assets only.

## 11. Explicit mask contract

Masks and clips are package-owned compiler outputs. Each mask record declares:

- an identifier;
- a safe package-relative PNG asset;
- coordinateSpace = reference;
- width and height equal to referenceCanvas.

The decoded mask asset MUST be a full-reference-canvas RGBA PNG and participate
in SHA closure. SELECTED_EMPHASIS requires an explicit mask. Other semantic
layers MAY reference an explicit mask when the reviewed decomposition needs
one. Runtime MUST NOT generate, classify, expand, or guess a semantic
ownership mask.

## 12. Geometry contract

radialGeometry declares:

- canonicalUnitWidth = 280;
- center = (140, 140) CRU;
- neutralHubRadius H0 = 35 CRU;
- neutralInnerRadius I0 = 42 CRU;
- neutralOuterRadius O0 = 103 CRU;
- neutralGap G0 = 4 degrees;
- one ordered slotCenter per slot, measured clockwise from top;
- outerWarpEnvelope authored radius R0, declared by the package/compiler;
- targetRule = scale-padding-by-petal-thickness;
- overflowPolicy = expand-transparent.

The authored and target geometry constraints are:

    0 < H0 < I0 < O0 < R0 <= 140
    0 < H < I < O
    0 <= gap <= 12 degrees
    gap < slotPitch

The value 140 CRU is the canonical nominal surface radius and the inclusive
upper bound for authored R0. It is not a required R0 value. R0 is the
theme/compiler-declared outer effect and padding envelope for authored
PETAL_FILL, PETAL_BORDER, PETAL_DETAIL, and SELECTED_EMPHASIS support that must
follow petal geometry. For example, O0 = 103 with R0 = 132 and O0 = 103 with
R0 = 140 are both valid calibrations.

Slot centers are ordered 1 through N and separated by exactly 360 / N degrees.
Their first angle is the layout orientation authority.

The target outer envelope is:

    Rt =
        O
        + (R0 - O0)
          * (O - I)
          / (O0 - I0)

The authored outer padding R0 - O0 is normalized by authored petal thickness
O0 - I0, then scaled by target petal thickness O - I. Because O0 - I0 > 0,
the formula cannot divide by zero. R0 > O0 and O > I require Rt > O. A package
or user parameter set that cannot produce a finite Rt > O is invalid; Runtime
MUST NOT clamp or replace the result. Rt may exceed the nominal 140 CRU radius;
the transparent-surface overflow rule applies.

Contract geometry uses double-precision continuous math. Rt MUST NOT be
integer-rounded in semantic geometry. Quantization occurs only at the final
Logical-to-Physical edge/raster stage under the existing DPI rounding
contract.

For I0 = 42, O0 = 103, R0 = 140, I = 50, and O = 120:

    Rt = 120 + 37 * 70 / 61

This is intentionally different from the rejected translation expression
140 + (120 - 103).

R0 is the maximum authored radial sampling support for the warp-owned petal
roles named above. The compiler MUST prove that their nontransparent pixels,
explicit masks, and required filter support lie at or inside R0. Source
samples for those roles outside R0 are transparent black. STATIC_RESIDUAL is not
petal support and is not required to lie inside R0. Runtime MUST NOT scan pixels
to discover or extend the envelope.

## 13. Normative piecewise polar transform

For a target sample, convert its CRU position relative to radialGeometry.center
to polar radius r and angle theta. Let:

    A = [0, H0, I0, O0, R0]
    B = [0, H,  I,  O,  Rt]

For the unique segment k where:

    B[k] <= r <= B[k + 1]

the inverse source radius is:

    rSource =
        A[k]
        + (r - B[k])
          * (A[k + 1] - A[k])
          / (B[k + 1] - B[k])

The strict radius ordering makes every denominator positive. Boundary samples
use the shared exact knot value, so adjacent segments cannot disagree.

For N slots, using radians:

    P  = 2 * pi / N
    w0 = (P - G0) / 2
    w  = (P - g) / 2
    delta = wrap(theta - theta[i]) into [-P / 2, P / 2)
    deltaSource = delta * w0 / w
    thetaSource = theta[i] + deltaSource

G0 and g MUST be converted from degrees to radians before these expressions.
The gap constraint guarantees w > 0. The mapping is an inverse sampling
mapping; no forward-splat ambiguity is permitted. These equations are
normative and are not implementation-defined.

## 14. Surface overflow and placement

After geometry transformation, implementations MUST compute the complete
nontransparent art bounds, including filter support. If those bounds exceed
the nominal surface, the renderer MUST expand the transparent logical surface.
It MUST preserve the radial center in screen space by applying the same
left/top expansion to the activation anchor before logical-to-physical
placement.

Silent clipping and implicit recentering are forbidden. Expanded placement
MUST remain compatible with the existing ShowAt activation-point contract:
the requested physical activation point still coincides with the transformed
activation anchor.

Rt is the transformed geometry-support authority for warp-owned petal roles;
it is not a hard final-bitmap clipping rectangle. Final bounds also include
filter support, STATIC_RESIDUAL, HUB, and validated dynamic content. Any Rt or
complete art bound beyond the nominal radius triggers transparent expansion.

ScalePercent then multiplies the expanded logical surface and its activation
anchor offset. Monitor DPI is applied only after that presentation transform.

## 15. Sampling, alpha, and compositing

### 15.1 Identity path

At the complete neutral vector, the matching frozen identity state MUST be
returned from its frozen decoded pixels without resampling. It MUST NOT pass
through bicubic sampling, decomposition, geometry reconstruction, or a
round-trip encoder.

### 15.2 Non-identity path

Every non-identity geometry transform MUST use:

- the inverse mapping in this contract;
- deterministic high-quality bicubic filtering;
- premultiplied PArgb processing during resampling/compositing;
- transparent black outside source support.

Nearest-neighbor sampling, nondeterministic filters, and Runtime image
classification are forbidden. Package PNG sources are sRGB RGBA with straight
alpha; conversion to premultiplied processing MUST scale color channels with
alpha and avoid transparent-color residue.

### 15.3 Fill and border

For authored alpha Aauthored and user strength S:

    Aeffective = round(Aauthored * S / 255)

Premultiplied RGB MUST be scaled by the same factor. FillAlpha applies only to
PETAL_FILL. BorderAlpha applies only to PETAL_BORDER. A value of 0 makes that
semantic layer invisible. A value of 255 preserves complete authored strength.
Neither setting may affect the other's owner.

### 15.4 Text and symbols

TextAlpha uses the same equation and applies to dynamicText, runtimeSymbol, and
text glyph fallback in both slot and selected-center groups. Premultiplied RGB
MUST change proportionally. Universal rendering has no independent 235 clamp;
legacy compatibility is provided solely by settings migration.

### 15.5 Selected emphasis

Let:

    t = HighlightAlpha / 255
    SelectedResult = lerp(BaseAppearance, FullSelectedAppearance, t)

The interpolation is performed within the selected descriptor's explicit
mask in premultiplied space. FullSelectedAppearance may lighten, darken,
replace color, replace borders, or use a different authored material. It is
not restricted to additive glow opacity.

HighlightAlpha = 0 yields the base appearance with no authored selected
emphasis. HighlightAlpha = 255 yields the complete authored selected
appearance. The setting MUST NOT change selectedActionGlyph or
selectedActionLabel.

## 16. Dynamic content geometry

The effective font size is:

    effectiveFontSize =
        themeAuthoredFontSize * FontSize / 15

This applies to V1 labels through the future adapter, V2/V3 dynamic text, and
text glyph fallback. A Runtime vector symbol is not scaled by FontSize unless
it falls back to text. Every glyph and text output is affected by TextAlpha.

TextRadius keeps its C#/JSON property name. Its future production label is
槽位内容半径. For every DYNAMIC_SLOT_CONTENT group:

    radialDelta = TextRadius - 73 CRU
    effectiveGroupRadius = authoredGroupRadius + radialDelta

The glyph and label move as one group. Theme-authored alignment, rotation,
glyph-label local offset, spacing, bounds, and line policy remain unchanged.
DYNAMIC_SELECTED_CONTENT is centered and MUST NOT move with TextRadius.

DYNAMIC_SLOT_CONTENT consumes slotActionGlyph and slotActionLabel for its
bound mapping slot. DYNAMIC_SELECTED_CONTENT consumes selectedActionGlyph and
selectedActionLabel for the currently selected action. Runtime symbol lookup
is attempted before text glyph fallback; both outputs use the authored local
group geometry and TextAlpha rule.

## 17. ScalePercent

ScalePercent is the presentation multiplier of the complete theme nominal
logical surface. It scales:

- semantic art and all semantic layers;
- dynamic group anchors and local bounds;
- any transparent surface expansion;
- the activation-anchor offset;
- the final logical surface.

ScalePercent does not scale the SelectionDeadZone semantic value. ScalePercent
= 100 is identity presentation scale.

## 18. Frozen identity states and fidelity authority

Every V3 package MUST include exactly:

- idle;
- selected-1 through selected-N.

Each selected state binds the matching slot ID. Identity frames serve four
purposes: neutral identity fast path, fidelity authority, compiler
reconstruction verification, and failure diagnosis.

The authoring compiler MUST prove:

    semantic reconstruction at the complete neutral vector
    == frozen identity state

Equality is raw decoded RGBA pixel equality over the complete reference
canvas. No tolerance, perceptual score, cropped comparison, or hidden-pixel
exception is allowed by this contract. Any future tolerance requires a
separate reviewed contract change.

## 19. Authoring compiler contract

The authoring flow is:

    reference artwork
    -> reviewed decomposition
    -> semantic assets and explicit masks
    -> validated V3 package

The compiler MUST output:

- semantic assets for every declared owner;
- explicit package-owned masks/clips;
- radial geometry calibration;
- manifest.json;
- exact SHA-256 asset closure;
- a compiler report identifying source and deterministic tool revision;
- a neutral reconstruction report with raw-pixel results for every identity
  state;
- extreme-setting review inputs;
- contact-sheet inputs.

Extreme review inputs MUST cover range boundaries and cross-effects for
geometry, scale, fill, border, text, highlight, and dynamic placement. Human
review remains required for visual quality, but it cannot waive validator or
raw-pixel reconstruction failures.

Runtime MUST NOT perform decomposition. Any change to ownership is an
authoring/compiler change followed by a new packageRevision and validation.

## 20. Future normalized model

Future production implementation will define concepts equivalent to:

- UniversalRadialParameters;
- UniversalRadialRenderPlan;
- SemanticRadialGeometry;
- SemanticLayerDescriptor;
- DynamicContentGroup;
- SelectedEmphasisDescriptor.

These names describe the required responsibilities; this Phase 0 work does not
add production C# types. A future V1 adapter and V3 loader will normalize to a
UniversalRadialRenderPlan. V2 full-state packages continue on their current
path until a separately approved migration.

A normalized plan MUST contain validated package identity, slot topology,
semantic geometry, deterministic layers/z-order, dynamic groups, selected
emphasis descriptors, decoded identity frames, explicit masks, placement, and
capabilities. It MUST contain no Runtime-inferred ownership.

ReceiverUiScalePercent is owned by the Receiver shell and is excluded from
both UniversalRadialParameters and UniversalRadialRenderPlan.

## 21. Validator requirements

validate_theme_v3.py is the independent Phase 0 entry point. It validates:

- the closed JSON Schema and duplicate/non-finite JSON rejection;
- protocol, surface, render strategy, layout, and slot coherence;
- canonical neutral geometry, package-authored O0 < R0 <= 140 envelope
  declarations, and evenly spaced slot centers;
- safe package-relative paths;
- exact SHA-256 reference closure;
- identity-state completeness and state/slot coherence;
- semantic layer completeness and duplicate ownership rejection;
- deterministic z-order;
- mask references, reference-space metadata, decoded dimensions, RGBA PNG
  format, containment, and hash;
- dynamic group completeness and neutral anchors;
- fallback identifiers and policies;
- the exact frozen capability set.

Manifest-only validation checks structure and closure without pretending that
placeholder assets exist. Package-candidate validation with asset checking is
mandatory before production use.

The validator is not a renderer, compiler, package installer, Runtime
discovery mechanism, or semantic classifier.

The validator checks declared envelope and mask metadata only. The future
compiler, not this validator or Runtime, proves nontransparent semantic and
mask support against R0; no Runtime pixel scan is permitted.

## 22. Compatibility and retirement

Before Phase 6, production may run V1, V2, and V3 through their respective
validated paths. V1 and V2 behavior remains unchanged in this phase.

After Phase 6, every production-selectable theme MUST have Universal
capability. A legacy theme that has not been migrated MUST no longer be
production-selectable, although its compatibility loader may temporarily
remain for migration and rollback diagnosis.

V1 is DEPRECATED FOR NEW AUTHORING immediately. No new production V1 theme is
permitted. radial-v5 and radial-8-minimal-v1 remain compatibility and migration
sources only. V1 Runtime may be removed only after those supported behaviors
have migrated to a validated Universal plan.

This Phase 0 contract does not create a Dark Fantasy V3 package, create
production masks, modify production states or manifests, modify Spike 01 or
Spike 02, modify Settings UI, alter user configuration, or implement Runtime
rendering.
