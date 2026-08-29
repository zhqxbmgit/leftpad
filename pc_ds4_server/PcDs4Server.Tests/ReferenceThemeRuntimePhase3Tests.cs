using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace PcDs4Server.Tests;

public sealed class ReferenceThemeRuntimePhase3Tests
{
    private readonly ITestOutputHelper _output;
    public ReferenceThemeRuntimePhase3Tests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void LoaderNormalizesAllDynamicLayersAnchorsStylesGlyphsAndOwnership()
    {
        using var fixture = new DynamicThemeFixture();
        NormalizedRenderPlan plan = fixture.Load().Plan;
        Assert.True(plan.HasV2DynamicContent);
        Assert.Contains("dynamicAnchors", plan.RequiredRuntimeCapabilities);
        Assert.Equal(18, plan.DynamicTheme!.Anchors.Count);
        Assert.Equal(18, plan.DynamicTheme.OwnershipByLayer.Count);
        Assert.Equal(2, plan.DynamicTheme.Styles.Count);
        Assert.Equal(2, plan.DynamicTheme.FontRoles.Count);
        Assert.Single(plan.DynamicTheme.GlyphRoles);
        Assert.Equal(20, Assert.IsType<FullStateFrameRenderPlan>(plan.RenderModel).OrderedLayers.Count);
    }

    [Fact]
    public void AuthoredAndDynamicLayersInterleaveByZIndexThenDeclarationOrder()
    {
        using var fixture=new DynamicThemeFixture();
        FullStateFrameLayer[] layers=Assert.IsType<FullStateFrameRenderPlan>(fixture.Load().Plan.RenderModel).OrderedLayers.ToArray();
        Assert.True(Array.FindIndex(layers,x=>x.Kind=="dynamicText")<Array.FindIndex(layers,x=>x.Id=="stateFrame"));
        Assert.True(Array.FindLastIndex(layers,x=>x.Id=="stateFrame")<Array.FindIndex(layers,x=>x.Kind=="dynamicGlyph"));
        foreach(IGrouping<int,FullStateFrameLayer> group in layers.GroupBy(x=>x.ZIndex))
            Assert.Equal(group.OrderBy(x=>x.DeclarationOrder).Select(x=>x.Id),group.Select(x=>x.Id));
    }

    [Theory]
    [InlineData("all","idle",null,true)]
    [InlineData("selected","idle",null,false)]
    [InlineData("selected","selected-3",3,true)]
    [InlineData("selected-3","selected-3",3,true)]
    [InlineData("selected-3","selected-4",4,false)]
    public void VisibilitySelectorsAreDeterministic(string selector,string state,int? slot,bool expected) =>
        Assert.Equal(expected,FullStateFrameCache.IsVisible(new[]{selector},state,slot));

    [Theory]
    [InlineData("unknown-key")]
    [InlineData("wrong-profile-slot")]
    [InlineData("unknown-style")]
    [InlineData("unknown-glyph")]
    [InlineData("anchor-outside-layer")]
    [InlineData("anchor-layer-mismatch")]
    [InlineData("unknown-symbol-set")]
    [InlineData("theme-asset")]
    [InlineData("safe-surface")]
    [InlineData("dynamic-capability-missing")]
    public void LoaderRejectsInvalidDynamicCandidates(string mutation)
    {
        using var fixture = new DynamicThemeFixture();
        fixture.Mutate(root =>
        {
            JsonObject ownership = root["elementOwnership"]![2]!.AsObject();
            JsonObject anchor = root["dynamicAnchors"]![0]!.AsObject();
            switch (mutation)
            {
                case "unknown-key": ownership["contentKey"] = "mysteryActionLabel"; break;
                case "wrong-profile-slot": root["layoutProfile"] = "radial-6";
                    root["compatibleLayouts"] = new JsonArray("radial-6"); break;
                case "unknown-style": anchor["styleRole"] = "missing"; break;
                case "unknown-glyph": root["dynamicAnchors"]![1]!["glyphRole"] = "missing"; break;
                case "anchor-outside-layer": anchor["bounds"]!["x"] = 319; break;
                case "anchor-layer-mismatch": anchor["layerId"] = "slot1GlyphLayer"; break;
                case "unknown-symbol-set": root["glyphs"]!["roles"]!["primary"]!["ds4"]!["sources"]![0]!["symbolSet"] = "other"; break;
                case "theme-asset": root["glyphs"]!["roles"]!["primary"]!["ds4"]!["sources"]![0] =
                    new JsonObject { ["type"]="themeAsset", ["assets"] = new JsonObject { ["cross"]="cross.png" } }; break;
                case "safe-surface": anchor["safeSurface"] = "mask"; break;
                case "dynamic-capability-missing": root["capabilities"]!["required"]!.AsArray().RemoveAt(1); break;
            }
        });
        Assert.ThrowsAny<Exception>(fixture.Load);
    }

    [Fact]
    public void RgbaParserUsesRrGgBbAaOrder()
    {
        using var fixture = new DynamicThemeFixture();
        fixture.Mutate(root => root["styles"]!["colorRoles"]!["fill"] = "#FF000080");
        NormalizedThemeColor color = fixture.Load().Plan.DynamicTheme!.Styles["label"].Color;
        Assert.Equal((byte)255, color.Red); Assert.Equal((byte)0, color.Green);
        Assert.Equal((byte)0, color.Blue); Assert.Equal((byte)128, color.Alpha);
    }

    [Theory]
    [InlineData("#00FF00FF",0,255,0,255)]
    [InlineData("#00000000",0,0,0,0)]
    public void RgbaParserHandlesOpaqueAndFullyTransparentColors(string value,int r,int g,int b,int a)
    {
        using var fixture=new DynamicThemeFixture();fixture.Mutate(root=>root["styles"]!["colorRoles"]!["fill"]=value);
        NormalizedThemeColor color=fixture.Load().Plan.DynamicTheme!.Styles["label"].Color;
        Assert.Equal((byte)r,color.Red);Assert.Equal((byte)g,color.Green);Assert.Equal((byte)b,color.Blue);Assert.Equal((byte)a,color.Alpha);
    }

    [Theory]
    [InlineData(0, "selectedActionLabel", true, null)]
    [InlineData(0, "selectedActionGlyph", true, null)]
    [InlineData(1, "selectedActionLabel", true, 1)]
    [InlineData(2, "selectedActionLabel", false, 2)]
    [InlineData(8, "selectedActionGlyph", false, 8)]
    [InlineData(7, "slot2ActionLabel", false, 2)]
    [InlineData(1, "slot4ActionGlyph", false, 4)]
    public void ResolverUsesSelectedAndPerSlotAuthority(int selected, string key, bool empty, int? sourceSlot)
    {
        ThemeMappingSnapshot snapshot = ThemeMappingSnapshot.Capture(Mappings(), "radial-8");
        ResolvedDynamicContent content = ThemeDynamicContentResolver.Resolve(key, selected, snapshot);
        Assert.Equal(empty, content.IsEmpty); Assert.Equal(sourceSlot, content.SourceSlot);
    }

    [Fact]
    public void ResolverReusesStableActionFormatting()
    {
        ThemeMappingSnapshot snapshot = ThemeMappingSnapshot.Capture(Mappings(), "radial-8");
        Assert.Equal("E", ThemeDynamicContentResolver.Resolve("slot2ActionLabel", 0, snapshot).LabelText);
        Assert.Equal("Ctrl+Shift+K", ThemeDynamicContentResolver.Resolve("slot3ActionLabel", 0, snapshot).LabelText);
        Assert.Equal("CROSS", ThemeDynamicContentResolver.Resolve("slot4ActionLabel", 0, snapshot).LabelText);
    }

    [Fact]
    public void MappingSnapshotIsImmutableAndValueComparable()
    {
        RadialMenuSettings settings = Mappings();
        ThemeMappingSnapshot first = ThemeMappingSnapshot.Capture(settings, "radial-8");
        ThemeMappingSnapshot same = ThemeMappingSnapshot.Capture(settings with { }, "radial-8");
        RadialMenuSettings changed = settings.SetProfileMappings("radial-8",
            settings.GetProfileMappings("radial-8").WithSlot(2, new RadialSlotMapping { Kind=RadialActionKind.KeyboardKey, Key=KeyboardKey.F }));
        Assert.Equal(first, same); Assert.NotEqual(first, ThemeMappingSnapshot.Capture(changed, "radial-8"));
        Assert.Equal(KeyboardKey.E, first.GetSlot(2).Key);
    }

    [Theory]
    [InlineData("ui", "Segoe UI")]
    [InlineData("display", "Segoe UI Semibold")]
    [InlineData("monospace", "Consolas")]
    [InlineData("symbol", "Segoe UI Symbol")]
    public void FontFallbackUsesClosedDeclaredOrder(string role, string expected)
    {
        var installed = new HashSet<string>(new[] { "Consolas", "Segoe UI", "Segoe UI Semibold", "Segoe UI Symbol" },
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected, ThemeFontResolver.SelectFamilyName(role, installed));
    }

    [Fact]
    public void FontFallbackDoesNotDependOnEnumerationOrder()
    {
        string[] names = { "Arial", "Segoe UI", "Microsoft YaHei UI" };
        foreach (string[] order in new[] { names, names.Reverse().ToArray() })
            Assert.Equal("Microsoft YaHei UI", ThemeFontResolver.SelectFamilyName("ui",
                order.ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("CROSS")]
    [InlineData("CIRCLE")]
    [InlineData("SQUARE")]
    [InlineData("TRIANGLE")]
    [InlineData("L1")]
    [InlineData("L3")]
    [InlineData("R3")]
    [InlineData("DPAD_DOWN")]
    public void Ds4RuntimeSymbolsAreClosedVectorPaths(string id)
    {
        var provider = new LeftPadRuntimeSymbolProvider();
        Assert.True(provider.TryCreatePath("leftpad-ds4", id, 64, out GraphicsPath path));
        using (path) Assert.True(path.PointCount > 0);
    }

    [Theory]
    [InlineData("ENTER")]
    [InlineData("SPACE")]
    [InlineData("TAB")]
    public void BasicRuntimeSymbolsAreClosedVectorPaths(string id)
    {
        var provider = new LeftPadRuntimeSymbolProvider();
        Assert.True(provider.TryCreatePath("leftpad-basic", id, 64, out GraphicsPath path));
        path.Dispose();
        Assert.False(provider.TryCreatePath("leftpad-basic", "ARBITRARY", 64, out _));
    }

    [Fact]
    public void GlyphFallbackHonorsManifestSourceOrder()
    {
        var textFirst = new NormalizedGlyphRole(
            Family(Text(), Symbol("leftpad-basic")), Family(Text()), Family(Text()), Family(Text()));
        var symbolFirst = new NormalizedGlyphRole(
            Family(Symbol("leftpad-basic"), Text()), Family(Text()), Family(Text()), Family(Text()));
        var content = new ResolvedDynamicContent(false,"Enter",RadialActionKind.KeyboardKey,"keyboard","ENTER","Enter",1);
        var provider = new LeftPadRuntimeSymbolProvider();
        Assert.Equal("text", ThemeGlyphResolver.Resolve(textFirst, content, provider)!.Type);
        Assert.Equal("runtimeSymbol", ThemeGlyphResolver.Resolve(symbolFirst, content, provider)!.Type);
    }

    [Fact]
    public void GlyphWithNoCapableSourceReturnsNoDrawDiagnosticResult()
    {
        var role = new NormalizedGlyphRole(Family(Symbol("leftpad-basic")), Family(Text()), Family(Text()), Family(Text()));
        var content = new ResolvedDynamicContent(false,"Z",RadialActionKind.KeyboardKey,"keyboard","Z","Z",1);
        Assert.Null(ThemeGlyphResolver.Resolve(role, content, new LeftPadRuntimeSymbolProvider()));
    }

    [Theory]
    [InlineData("left", "top")]
    [InlineData("center", "center")]
    [InlineData("right", "bottom")]
    public void SemanticFRespectsAlignmentBeforeRotation(string horizontal, string vertical)
    {
        using FontFamily font = WindowsUiFontResolver.ResolveUiFontFamily();
        NormalizedDynamicAnchor anchor = Anchor(horizontal, vertical, "clip", rotation: 0);
        using ThemeDynamicLayoutResult result = ThemeDynamicLayoutEngine.Layout(anchor, Style(), font, "E");
        Assert.True(result.Draw);
        if (horizontal == "left") Assert.Equal(anchor.Bounds.X, result.F.Left, 3);
        if (horizontal == "right") Assert.Equal(anchor.Bounds.X+anchor.Bounds.Width, result.F.Right, 3);
        if (vertical == "top") Assert.Equal(anchor.Bounds.Y, result.F.Top, 3);
        if (vertical == "bottom") Assert.Equal(anchor.Bounds.Y+anchor.Bounds.Height, result.F.Bottom, 3);
    }

    [Fact]
    public void SemanticOutlineRotationShadowAndStyledUnionAreNormative()
    {
        using FontFamily font = WindowsUiFontResolver.ResolveUiFontFamily();
        NormalizedDynamicStyle style = Style(outlineWidth: 4, shadowOffsetX: 3, shadowOffsetY: 5, blur: 2);
        using ThemeDynamicLayoutResult result = ThemeDynamicLayoutEngine.Layout(
            Anchor("center","center","clip", rotation: 30), style, font, "TEST");
        Assert.Equal(result.F.Left-2, result.O.Left, 6);
        Assert.True(result.R.Width > result.O.Width);
        Assert.Equal(result.R.Left+3-6, result.S!.Value.Left, 6);
        Assert.Equal(ThemeSemanticRect.Union(result.R,result.S.Value), result.StyledBounds);
    }

    [Fact]
    public void AlphaZeroRulesPreservePathSourceButRemoveContributions()
    {
        using FontFamily font = WindowsUiFontResolver.ResolveUiFontFamily();
        var transparentFill = new NormalizedDynamicStyle("font", new(255,255,255,0), 30,
            new(new(0,0,0,255),4), null);
        using ThemeDynamicLayoutResult outlined = ThemeDynamicLayoutEngine.Layout(Anchor(), transparentFill, font, "E");
        Assert.True(outlined.Draw); Assert.True(outlined.O.Width > outlined.F.Width);
        var transparentOutline = transparentFill with { Outline = new(new(0,0,0,0),4) };
        using ThemeDynamicLayoutResult collapsed = ThemeDynamicLayoutEngine.Layout(Anchor(), transparentOutline, font, "E");
        Assert.Equal(collapsed.F, collapsed.O); Assert.False(collapsed.Draw);
    }

    [Fact]
    public void ZeroWidthOutlineCollapsesSemanticOToF()
    {
        using FontFamily font=WindowsUiFontResolver.ResolveUiFontFamily();
        NormalizedDynamicStyle style=Style() with {Outline=new(new(0,0,0,255),0)};
        using ThemeDynamicLayoutResult result=ThemeDynamicLayoutEngine.Layout(Anchor(),style,font,"E");
        Assert.Equal(result.F,result.O);
    }

    [Fact]
    public void TransparentShadowAddsNoSemanticContribution()
    {
        using FontFamily font=WindowsUiFontResolver.ResolveUiFontFamily();
        NormalizedDynamicStyle style=Style() with {Shadow=new(new(0,0,0,0),50,50,20)};
        using ThemeDynamicLayoutResult result=ThemeDynamicLayoutEngine.Layout(Anchor(),style,font,"E");
        Assert.Null(result.S);Assert.Equal(result.R,result.StyledBounds);
    }

    [Fact]
    public void ZeroBlurShadowIsHardTranslatedRWithoutExpansion()
    {
        using FontFamily font=WindowsUiFontResolver.ResolveUiFontFamily();
        NormalizedDynamicStyle style=Style() with {Shadow=new(new(0,0,0,255),3,4,0)};
        using ThemeDynamicLayoutResult result=ThemeDynamicLayoutEngine.Layout(Anchor(),style,font,"E");
        Assert.Equal(result.R.Translate(3,4),result.S);
    }

    [Fact]
    public void ShrinkUsesLargestFixedPrecisionAndScalesEveryEffect()
    {
        using FontFamily font = WindowsUiFontResolver.ResolveUiFontFamily();
        var anchor = Anchor("center","center","shrink", width:45, height:32, minimum:.25);
        using ThemeDynamicLayoutResult result = ThemeDynamicLayoutEngine.Layout(anchor,
            Style(outlineWidth:4, shadowOffsetX:4, shadowOffsetY:2, blur:2), font, "LONGTEXT");
        Assert.True(result.Draw); Assert.InRange(result.Scale, .25, 1);
        Assert.Equal(0, result.Scale*ThemeDynamicLayoutEngine.ScalePrecisionDenominator % 1, 10);
        Assert.Equal(4*result.Scale/2, result.F.Left-result.O.Left, 6);
    }

    [Fact]
    public void ShrinkMinimumFailureAndHideReturnNoDrawWhileClipDraws()
    {
        using FontFamily font = WindowsUiFontResolver.ResolveUiFontFamily();
        using ThemeDynamicLayoutResult shrink = ThemeDynamicLayoutEngine.Layout(
            Anchor(overflow:"shrink",width:2,height:2,minimum:.9),Style(),font,"WIDE");
        using ThemeDynamicLayoutResult hide = ThemeDynamicLayoutEngine.Layout(
            Anchor(overflow:"hide",width:2,height:2),Style(),font,"WIDE");
        using ThemeDynamicLayoutResult clip = ThemeDynamicLayoutEngine.Layout(
            Anchor(overflow:"clip",width:2,height:2),Style(),font,"WIDE");
        Assert.False(shrink.Draw); Assert.False(hide.Draw); Assert.True(clip.Draw);
    }

    [Fact]
    public void EllipsisUsesUnicodeTextElementsAndTerminalMarker()
    {
        using FontFamily font = WindowsUiFontResolver.ResolveUiFontFamily();
        string grapheme = "A\U0001F469\u200D\U0001F4BBB";
        using ThemeDynamicLayoutResult result = ThemeDynamicLayoutEngine.Layout(
            Anchor(overflow:"ellipsis",width:35,height:40),Style(),font,grapheme);
        Assert.True(result.DisplayedText.EndsWith("\u2026",StringComparison.Ordinal) || result.DisplayedText==grapheme);
        Assert.DoesNotContain("\uFFFD", result.DisplayedText);
    }

    [Fact]
    public void SemanticLayoutIsIdenticalAcrossAllPhysicalDpis()
    {
        using FontFamily font = WindowsUiFontResolver.ResolveUiFontFamily();
        var records = new List<string>();
        foreach (int dpi in new[] {96,120,144,168,192})
        {
            using ThemeDynamicLayoutResult result = ThemeDynamicLayoutEngine.Layout(
                Anchor(overflow:"shrink",rotation:27,width:80,height:40),Style(2,2,3,1),font,"Ctrl+Shift+K");
            records.Add($"{result.DisplayedText}|{result.Scale:R}|{result.Draw}|{result.F}|{result.O}|{result.R}|{result.S}|{result.StyledBounds}");
        }
        Assert.Single(records.Distinct());
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    [InlineData(192)]
    public void DynamicFinalStatesArePrebuiltPArgbAtEveryDpi(int dpi)
    {
        using var fixture = new DynamicThemeFixture();
        using var cache = new FullStateFrameCache(fixture.Load().Plan, fixture.Settings, dpi, fixture.Decoder);
        Assert.Equal(9,cache.StateCount); Assert.Equal(10,cache.DecodedAssetCount);
        Assert.Equal(10,fixture.Decoder.DecodeCount); Assert.True(cache.DynamicBuildCount>0);
        for(int slot=0;slot<=8;slot++) Assert.Equal(PixelFormat.Format32bppPArgb,cache.GetState(slot).PixelFormat);
    }

    [Fact]
    public void MappingOnlyRebuildReusesAuthoredDecodeAndAtomicallyChangesRelevantStates()
    {
        using var fixture = new DynamicThemeFixture();
        using var cache = new FullStateFrameCache(fixture.Load().Plan, fixture.Settings, 96, fixture.Decoder);
        int decoded=fixture.Decoder.DecodeCount, authored=cache.AuthoredLayerCount;
        string before=Sha(cache.GetState(0));
        RadialMenuSettings changed=fixture.Settings.SetProfileMappings("radial-8",
            fixture.Settings.GetProfileMappings("radial-8").WithSlot(2,new RadialSlotMapping{Kind=RadialActionKind.KeyboardKey,Key=KeyboardKey.F}));
        Assert.True(cache.EnsureMappings(changed));
        Assert.Equal(decoded,fixture.Decoder.DecodeCount); Assert.Equal(authored,cache.AuthoredLayerCount);
        Assert.NotEqual(before,Sha(cache.GetState(0))); Assert.Equal(1,cache.MappingRebuildCount);
        Assert.False(cache.EnsureMappings(changed));
    }

    [Fact]
    public void FailedMappingCandidateRetainsOldCompleteStateCache()
    {
        using var fixture=new DynamicThemeFixture();int attempts=0;
        using var cache=new FullStateFrameCache(fixture.Load().Plan,fixture.Settings,96,null,
            _=>{attempts++;if(attempts>1)throw new InvalidDataException("synthetic mapping failure");});
        Bitmap before=cache.GetState(0);string hash=Sha(before);
        RadialMenuSettings changed=fixture.Settings.SetProfileMappings("radial-8",
            fixture.Settings.GetProfileMappings("radial-8").WithSlot(2,new RadialSlotMapping{Kind=RadialActionKind.KeyboardKey,Key=KeyboardKey.F}));
        Assert.Throws<InvalidDataException>(()=>cache.EnsureMappings(changed));
        Assert.Same(before,cache.GetState(0));Assert.Equal(hash,Sha(cache.GetState(0)));
        Assert.Equal(0,cache.MappingRebuildCount);
    }

    [Fact]
    public void StateLookupPerformsNoResolveLayoutBlurDecodeOrResize()
    {
        using var fixture = new DynamicThemeFixture();
        using var cache = new FullStateFrameCache(fixture.Load().Plan, fixture.Settings, 96, fixture.Decoder);
        int decoded=fixture.Decoder.DecodeCount, builds=cache.DynamicBuildCount;
        var watch=Stopwatch.StartNew();
        for(int i=0;i<100;i++) _=cache.GetState(i%9);
        watch.Stop();
        Assert.Equal(decoded,fixture.Decoder.DecodeCount); Assert.Equal(builds,cache.DynamicBuildCount);
        Assert.Equal(0,cache.HotPathDynamicWorkCount);
        _output.WriteLine($"dynamic stateLookup100={watch.Elapsed.TotalMilliseconds:F3}ms");
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void DynamicPixelGoldensAreStableRawByteHashes(int dpi)
    {
        using var fixture = new DynamicThemeFixture();
        using var cache = new FullStateFrameCache(fixture.Load().Plan, fixture.Settings, dpi);
        string first=Sha(cache.GetState(0)); string selected=Sha(cache.GetState(4));
        using var second = new FullStateFrameCache(fixture.Load().Plan, fixture.Settings, dpi);
        Assert.Equal(first,Sha(second.GetState(0))); Assert.Equal(selected,Sha(second.GetState(4)));
        Assert.NotEqual(first,selected);
        _output.WriteLine($"dpi={dpi}; idle={first}; selected4={selected}");
    }

    [Fact]
    public void DynamicCacheAndBundleDoubleDisposeSafely()
    {
        using var fixture = new DynamicThemeFixture();
        var bundle=RuntimeRenderBundle.Build(fixture.Load().Plan,fixture.Settings,280,96);
        bundle.Dispose(); bundle.Dispose(); Assert.Equal(1,bundle.DisposeCount);
        Assert.Throws<ObjectDisposedException>(()=>bundle.GetFinalState(0));
    }

    [Fact]
    public void V1DynamicV1ThemeSwitchPublishesAndDisposesAtomically()
    {
        using var fixture=new DynamicThemeFixture();using var runtime=new RadialVisualPackRuntime(fixture.Catalog);
        RadialVisualPackSession v1=runtime.Ensure(RadialMenuSettings.SafeFallback,280,96)!;
        RadialVisualPackSession dynamic=runtime.Ensure(fixture.Settings,280,96)!;
        Assert.True(dynamic.Plan.HasV2DynamicContent);Assert.True(v1.Bundle.IsDisposed);
        RadialVisualPackSession v1Again=runtime.Ensure(RadialMenuSettings.SafeFallback,280,96)!;
        Assert.False(v1Again.Bundle.IsFullStateFrame);Assert.True(dynamic.Bundle.IsDisposed);
    }

    [Fact]
    public void RealHwndIdleDraftPreviewUsesDynamicProductionOverlayPath()
    {
        using var fixture = new DynamicThemeFixture();
        Exception? failure = null;
        bool visible = false, hidden = false;
        int dpi = 0;
        var thread = new Thread(() =>
        {
            try
            {
                using var overlay = new RadialMenuOverlay(fixture.Catalog);
                using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default,
                    fixture.Load().Plan.LayoutDefinition);
                RadialMenuSettings draft = fixture.Settings.SetProfileMappings("radial-8",
                    fixture.Settings.GetProfileMappings("radial-8")
                        .WithSlot(1,new RadialSlotMapping{Kind=RadialActionKind.KeyboardKey,Key=KeyboardKey.E})
                        .WithSlot(2,new RadialSlotMapping{Kind=RadialActionKind.KeyboardShortcut,Key=KeyboardKey.K,Ctrl=true,Shift=true})
                        .WithSlot(3,new RadialSlotMapping{Kind=RadialActionKind.Ds4Button,Ds4Button="cross"}));
                controller.PreviewAt(new Point(700,500),draft);
                visible=controller.IsPreviewActive&&overlay.IsVisible&&overlay.IsHandleCreated;
                dpi=overlay.ActiveDpi;
                controller.ClosePreview();
                hidden=!controller.IsPreviewActive&&!overlay.IsVisible;
            }
            catch(Exception exception){failure=exception;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)),"Dynamic HWND preview timed out.");
        Assert.Null(failure);Assert.True(visible);Assert.True(hidden);Assert.True(dpi>0);
        _output.WriteLine($"dynamic idle draft preview: dpi={dpi}; visible={visible}; hidden={hidden}");
    }

    [Fact]
    public void PerformanceRecordsInitialMappingDpiLookupAndPayload()
    {
        using var fixture=new DynamicThemeFixture();
        long before=GC.GetTotalMemory(true);var initial=Stopwatch.StartNew();
        using var cache=new FullStateFrameCache(fixture.Load().Plan,fixture.Settings,96);initial.Stop();
        long retained=Math.Max(0,GC.GetTotalMemory(false)-before);
        RadialMenuSettings changed=fixture.Settings.SetProfileMappings("radial-8",
            fixture.Settings.GetProfileMappings("radial-8").WithSlot(2,new RadialSlotMapping{Kind=RadialActionKind.KeyboardKey,Key=KeyboardKey.F}));
        var mapping=Stopwatch.StartNew();cache.EnsureMappings(changed);mapping.Stop();
        var dpiWatch=Stopwatch.StartNew();using(var dpiCache=new FullStateFrameCache(fixture.Load().Plan,changed,144)){}dpiWatch.Stop();
        var lookup=Stopwatch.StartNew();for(int i=0;i<100;i++)_=cache.GetState(i%9);lookup.Stop();
        _output.WriteLine($"initial={initial.Elapsed.TotalMilliseconds:F3}ms; mapping={mapping.Elapsed.TotalMilliseconds:F3}ms; dpi={dpiWatch.Elapsed.TotalMilliseconds:F3}ms; lookup100={lookup.Elapsed.TotalMilliseconds:F3}ms; retained={retained}");
        Assert.True(initial.Elapsed>TimeSpan.Zero);Assert.True(mapping.Elapsed>TimeSpan.Zero);
        Assert.True(dpiWatch.Elapsed>TimeSpan.Zero);Assert.True(lookup.Elapsed>TimeSpan.Zero);
    }

    private static RadialMenuSettings Mappings()
    {
        var slots=RadialSlotMappings.Create(8,new RadialSlotMapping[]
        {
            RadialSlotMapping.None,
            new(){Kind=RadialActionKind.KeyboardKey,Key=KeyboardKey.E},
            new(){Kind=RadialActionKind.KeyboardShortcut,Key=KeyboardKey.K,Ctrl=true,Shift=true},
            new(){Kind=RadialActionKind.Ds4Button,Ds4Button="cross"},
            new(){Kind=RadialActionKind.KeyboardKey,Key=KeyboardKey.Enter},
            new(){Kind=RadialActionKind.Ds4Button,Ds4Button="circle"},
            new(){Kind=RadialActionKind.KeyboardKey,Key=KeyboardKey.Space},
            new(){Kind=RadialActionKind.Ds4Button,Ds4Button="dpad_down"}
        });
        return (RadialMenuSettings.Default with {MappingProfileId="radial-8"}).SetProfileMappings("radial-8",slots);
    }

    private static NormalizedDynamicAnchor Anchor(string horizontal="center",string vertical="center",
        string overflow="clip",double rotation=0,double width=120,double height=60,double minimum=.5) =>
        new("a","l","text",new(10,20,width,height),horizontal,vertical,overflow,minimum,rotation,1,
            Array.AsReadOnly(new[]{"all"}),"s",null);
    private static NormalizedDynamicStyle Style(double outlineWidth=0,double shadowOffsetX=0,
        double shadowOffsetY=0,double blur=0) => new("font",new(255,255,255,255),30,
            outlineWidth>0?new(new(0,0,0,255),outlineWidth):null,
            shadowOffsetX!=0||shadowOffsetY!=0||blur!=0?new(new(0,0,0,128),shadowOffsetX,shadowOffsetY,blur):null);
    private static NormalizedGlyphSource Text()=>new("text","glyph",null);
    private static NormalizedGlyphSource Symbol(string set)=>new("runtimeSymbol",null,set);
    private static NormalizedGlyphFamily Family(params NormalizedGlyphSource[] sources)=>new(sources);

    private static string Sha(Bitmap bitmap)
    {
        Rectangle rect=new(0,0,bitmap.Width,bitmap.Height);
        BitmapData data=bitmap.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try{byte[] bytes=new byte[data.Stride*data.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);return Convert.ToHexString(SHA256.HashData(bytes));}
        finally{bitmap.UnlockBits(data);}
    }

    private sealed class CountingDecoder:IThemeAssetDecoder
    { public int DecodeCount{get;private set;} public Bitmap Decode(VerifiedThemeAsset asset){DecodeCount++;return new GdiThemeAssetDecoder().Decode(asset);} }

    private sealed class DynamicThemeFixture:IDisposable
    {
        private readonly RadialVisualPackTestDirectory _v1=new();
        private readonly string _manifest;
        public DynamicThemeFixture()
        {
            _v1.AddPack("default","radial-v5","Default"); _v1.AddRadial8Pack();
            ThemeRoot=Path.Combine(Path.GetTempPath(),"LeftPad.Tests",Guid.NewGuid().ToString("N"));
            PackageRoot=Path.Combine(ThemeRoot,"dynamic-v3");Directory.CreateDirectory(PackageRoot);
            WritePng("background.png",Color.FromArgb(255,8,12,20));WritePng("idle.png",Color.FromArgb(80,20,40,70));
            for(int slot=1;slot<=8;slot++)WritePng($"selected-{slot}.png",Color.FromArgb(80,20*slot,100,180-slot*10));
            JsonArray layers=new(Layer("background","staticAsset",0,"STATIC","background.png"),Layer("stateFrame","stateAsset",10,"STATE_ASSET",null));
            JsonArray anchors=new(); JsonArray ownership=new(Owner("backgroundElement","background","STATIC",null),Owner("stateElement","stateFrame","STATE_ASSET",null));
            for(int slot=1;slot<=8;slot++){AddDynamic(layers,anchors,ownership,$"slot{slot}Label",$"slot{slot}ActionLabel","dynamicText",5+slot%3,slot);AddDynamic(layers,anchors,ownership,$"slot{slot}Glyph",$"slot{slot}ActionGlyph","dynamicGlyph",12+slot%3,slot);}
            AddDynamic(layers,anchors,ownership,"selectedLabel","selectedActionLabel","dynamicText",20,0);
            AddDynamic(layers,anchors,ownership,"selectedGlyph","selectedActionGlyph","dynamicGlyph",21,0);
            JsonObject states=new(){["idle"]=State(null,"idle.png")};for(int i=1;i<=8;i++)states[$"selected-{i}"]=State(i,$"selected-{i}.png");
            JsonObject root=new()
            {
                ["protocolVersion"]=2,["packageRevision"]=1,["id"]="synthetic-dynamic-v3",["name"]="Synthetic Dynamic V3",
                ["surface"]="radial-overlay",["renderStrategy"]="full-state-frame",["layoutProfile"]="radial-8",["compatibleLayouts"]=new JsonArray("radial-8"),
                ["referenceCanvas"]=new JsonObject{{"width",320},{"height",240},{"colorSpace","sRGB"},{"alphaMode","straight"}},
                ["referenceScale"]=new JsonObject{{"logicalWidth",320},{"logicalHeight",240},{"fit","contain"},{"contentOrigin",new JsonObject{{"x",0},{"y",0}}}},
                ["placement"]=new JsonObject{{"activationAnchor",new JsonObject{{"x",160},{"y",120}}}},["states"]=states,["layers"]=layers,["dynamicAnchors"]=anchors,
                ["styles"]=Styles(),["glyphs"]=Glyphs(),["elementOwnership"]=ownership,["masks"]=new JsonArray(),["visualRegions"]=new JsonArray(),
                ["fallback"]=new JsonObject{{"onInvalidCandidate","retain-active"},{"onUnsupportedVersion","retain-active"},{"startupThemeId","radial-v5"}},
                ["capabilities"]=new JsonObject{{"required",new JsonArray("fullStateFrame","dynamicAnchors","instantTransitions")},{"optional",new JsonArray()}},
                ["transitions"]=new JsonObject{{"mode","instant"}},["assetHashes"]=new JsonObject()
            };
            _manifest=Path.Combine(PackageRoot,"manifest.json");Write(root);Catalog=new(_v1.Root,ThemeRoot);
            Settings=(Mappings() with {VisualPackId="synthetic-dynamic-v3"});
        }
        public string ThemeRoot{get;} public string PackageRoot{get;} public RadialVisualPackCatalog Catalog{get;} public RadialMenuSettings Settings{get;} public CountingDecoder Decoder{get;}=new();
        public UiThemeV2Package Load(){LayoutDefinition layout=Catalog.Discover().Packs.First(x=>!x.IsV2&&x.LayoutDefinition.ProfileId=="radial-8").LayoutDefinition;return UiThemeV2Loader.Load(PackageRoot,layout);}
        public void Mutate(Action<JsonObject> edit){JsonObject root=JsonNode.Parse(File.ReadAllText(_manifest))!.AsObject();edit(root);Write(root,false);}
        public void Dispose(){_v1.Dispose();if(Directory.Exists(ThemeRoot))Directory.Delete(ThemeRoot,true);}
        private static JsonObject Layer(string id,string kind,int z,string owner,string? asset){var value=new JsonObject{{"id",id},{"kind",kind},{"zIndex",z},{"bounds",new JsonObject{{"x",0},{"y",0},{"width",320},{"height",240}}},{"visibleStates",new JsonArray("all")},{"ownership",owner},{"required",true}};if(asset!=null)value["asset"]=asset;return value;}
        private static void AddDynamic(JsonArray layers,JsonArray anchors,JsonArray ownership,string id,string content,string kind,int z,int index)
        {int column=index==0?1:(index-1)%4,row=index==0?2:(index-1)/4;double x=column*76+4,y=row*70+6,w=68,h=54;string layer=id+"Layer";layers.Add(new JsonObject{{"id",layer},{"kind",kind},{"zIndex",z},{"bounds",new JsonObject{{"x",x},{"y",y},{"width",w},{"height",h}}},{"visibleStates",new JsonArray("all")},{"ownership","DYNAMIC"},{"required",true}});var anchor=new JsonObject{{"id",id},{"layerId",layer},{"role",kind=="dynamicText"?"text":"glyph"},{"bounds",new JsonObject{{"x",x},{"y",y},{"width",w},{"height",h}}},{"horizontalAlignment","center"},{"verticalAlignment","center"},{"overflowPolicy",index%4==0?"ellipsis":index%4==1?"shrink":index%4==2?"clip":"hide"},{"minimumScale",.5},{"rotation",index%2==0?12:0},{"maxLines",1},{"visibleStates",new JsonArray("all")},{"styleRole",kind=="dynamicText"?"label":"glyph"}};if(kind=="dynamicGlyph")anchor["glyphRole"]="primary";anchors.Add(anchor);ownership.Add(Owner(id,layer,"DYNAMIC",content));}
        private static JsonObject Owner(string element,string layer,string owner,string? key){var value=new JsonObject{{"element",element},{"owner",owner},{"layerId",layer}};if(key!=null)value["contentKey"]=key;return value;}
        private static JsonObject State(int? slot,string asset)=>new(){{"slotId",slot==null?null:JsonValue.Create(slot)},{"assets",new JsonObject{{"stateFrame",asset}}}};
        private static JsonObject Styles() => new()
        {
            ["fontRoles"] = new JsonObject { ["interface"]="ui", ["symbols"]="symbol" },
            ["colorRoles"] = new JsonObject { ["fill"]="#F4F8FFFF", ["edge"]="#071018E6", ["shade"]="#00000070" },
            ["outlineRoles"] = new JsonObject { ["edge"] = new JsonObject { ["colorRole"]="edge", ["width"]=1.5 } },
            ["shadowRoles"] = new JsonObject { ["soft"] = new JsonObject
                { ["colorRole"]="shade", ["offsetX"]=1, ["offsetY"]=2, ["blur"]=1 } },
            ["dynamicRoles"] = new JsonObject
            {
                ["label"] = new JsonObject { ["fontRole"]="interface", ["colorRole"]="fill",
                    ["outlineRole"]="edge", ["shadowRole"]="soft", ["size"]=15 },
                ["glyph"] = new JsonObject { ["fontRole"]="symbols", ["colorRole"]="fill",
                    ["outlineRole"]="edge", ["size"]=22 }
            }
        };
        private static JsonObject Glyphs(){JsonObject Text()=>new(){{"type","text"},{"styleRole","glyph"}};JsonObject Runtime(string set)=>new(){{"type","runtimeSymbol"},{"symbolSet",set}};JsonObject Family(params JsonObject[] sources)=>new(){{"sources",new JsonArray(sources)}};return new(){{"roles",new JsonObject{{"primary",new JsonObject{{"keyboard",Family(Runtime("leftpad-basic"),Text())},{"keyboardShortcut",Family(Text())},{"ds4",Family(Runtime("leftpad-ds4"),Text())},{"genericAction",Family(Text())}}}}}};}
        private void WritePng(string name,Color color){using var bitmap=new Bitmap(4,4,PixelFormat.Format32bppArgb);using Graphics graphics=Graphics.FromImage(bitmap);graphics.Clear(color);bitmap.Save(Path.Combine(PackageRoot,name),ImageFormat.Png);}
        private void Write(JsonObject root,bool hashes=true){if(hashes){var values=new JsonObject();foreach(string file in Directory.GetFiles(PackageRoot,"*.png"))values[Path.GetFileName(file)]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));root["assetHashes"]=values;}File.WriteAllText(_manifest,root.ToJsonString(new JsonSerializerOptions{WriteIndented=true,TypeInfoResolver=new DefaultJsonTypeInfoResolver()}));}
    }
}
