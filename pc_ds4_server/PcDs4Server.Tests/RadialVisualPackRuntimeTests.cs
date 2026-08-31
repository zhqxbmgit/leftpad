using System.Text.Json.Nodes;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialVisualPackRuntimeTests
{
    [Fact]
    public void SameRequestedId_ReusesInstalledSession()
    {
        using var temporary = CatalogWithDefault();
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root), RadialRenderPolicy.Legacy);

        RadialVisualPackSession first = runtime.Ensure(RadialMenuSettings.Default, 280)!;
        RadialVisualPackSession second = runtime.Ensure(RadialMenuSettings.Default, 280)!;

        Assert.Same(first, second);
        Assert.Equal(1, runtime.InstallCount);
        Assert.False(first.IsDisposed);
    }

    [Fact]
    public void ValidNewPack_IsFullyBuiltThenAtomicallyInstalledAndOldSessionDisposed()
    {
        using var temporary = CatalogWithDefault();
        temporary.AddPack("alternate", "alternate", "Alternate");
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root), RadialRenderPolicy.Legacy);
        RadialVisualPackSession first = runtime.Ensure(RadialMenuSettings.Default, 280)!;

        RadialVisualPackSession second = runtime.Ensure(
            RadialMenuSettings.Default with { VisualPackId = "alternate" },
            392)!;

        Assert.NotSame(first, second);
        Assert.Equal("alternate", runtime.ActivePackId);
        Assert.Equal(392, second.AssetCache.TargetSize);
        Assert.Equal(2, runtime.InstallCount);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void InvalidNewPack_PreservesCurrentCacheAndDoesNotDisposeIt()
    {
        using var temporary = CatalogWithDefault();
        string broken = temporary.AddPack("broken", "broken", "Broken");
        File.WriteAllText(Path.Combine(broken, "radial-base-v5.png"), "not a png");
        var log = new List<string>();
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root),
            RadialRenderPolicy.Legacy,
            log.Add);
        RadialVisualPackSession first = runtime.Ensure(RadialMenuSettings.Default, 280)!;

        RadialVisualPackSession result = runtime.Ensure(
            RadialMenuSettings.Default with { VisualPackId = "broken" },
            280)!;

        Assert.Same(first, result);
        Assert.Equal("radial-v5", runtime.ActivePackId);
        Assert.Equal(1, runtime.InstallCount);
        Assert.False(first.IsDisposed);
        Assert.Contains(log, message =>
            message.Contains("Requested pack 'broken' failed") &&
            message.Contains("fallback 'radial-v5'"));
    }

    [Fact]
    public void InvalidNewPack_FromNonDefaultThemeRetainsOldSession()
    {
        using var temporary = CatalogWithDefault();
        temporary.AddPack("alternate", "alternate", "Alternate");
        string broken = temporary.AddPack("broken", "broken", "Broken");
        File.WriteAllText(Path.Combine(broken, "radial-selected-card-v5.png"), "not a png");
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root), RadialRenderPolicy.Legacy);
        _ = runtime.Ensure(RadialMenuSettings.Default, 280);
        RadialVisualPackSession alternate = runtime.Ensure(
            RadialMenuSettings.Default with { VisualPackId = "alternate" },
            280)!;

        RadialVisualPackSession retained = runtime.Ensure(
            RadialMenuSettings.Default with { VisualPackId = "broken" },
            280)!;

        Assert.Same(alternate, retained);
        Assert.Equal("alternate", retained.PackId);
        Assert.False(alternate.IsDisposed);
        Assert.Equal(2, runtime.InstallCount);
    }

    [Fact]
    public void UnknownIdAtStartup_FallsBackToRadialV5AndLogsRequestedId()
    {
        using var temporary = CatalogWithDefault();
        var log = new List<string>();
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root),
            RadialRenderPolicy.Legacy,
            log.Add);

        RadialVisualPackSession session = runtime.Ensure(
            RadialMenuSettings.Default with { VisualPackId = "future-theme" },
            280)!;

        Assert.Equal("radial-v5", session.PackId);
        Assert.Contains(log, message =>
            message.Contains("future-theme") && message.Contains("fallback 'radial-v5'"));
    }

    [Fact]
    public void ThemeSwitch_DynamicContentUsesActivePackAnchors()
    {
        using var temporary = CatalogWithDefault();
        temporary.AddPack(
            "alternate",
            "alternate",
            "Alternate",
            editLayout: layout =>
            {
                JsonArray slots = layout["slots"]!.AsArray();
                slots[0]!["glyphAnchor"]!["x"] = 700;
            });
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(temporary.Root), RadialRenderPolicy.Legacy);
        _ = runtime.Ensure(RadialMenuSettings.Default, 280);

        RadialVisualPackSession active = runtime.Ensure(
            RadialMenuSettings.Default with { VisualPackId = "alternate" },
            280)!;

        Assert.Equal(700d, active.Definition.Layout.Slots[0].GlyphAnchor.X);
        Assert.Same(active.Plan, active.AssetCache.Plan);
        Assert.Equal(1, active.DynamicContent.BuildCount);
    }

    private static RadialVisualPackTestDirectory CatalogWithDefault()
    {
        var temporary = new RadialVisualPackTestDirectory();
        temporary.AddPack("default", "radial-v5", "Tactical HUD V5");
        return temporary;
    }
}
