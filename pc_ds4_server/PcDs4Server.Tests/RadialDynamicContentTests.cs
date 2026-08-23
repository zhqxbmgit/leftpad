using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialDynamicContentTests
{
    [Fact]
    public void FontResolver_PrefersChineseUiFamiliesInDeclaredOrder()
    {
        string? selected = WindowsUiFontResolver.SelectPreferredFamilyName(new[]
        {
            "Segoe UI",
            "DengXian",
            "Microsoft YaHei",
            "Microsoft YaHei UI"
        });

        Assert.Equal("Microsoft YaHei UI", selected);
    }

    [Fact]
    public void FontResolver_FallbackReturnsUsableFontFamily()
    {
        Assert.Null(WindowsUiFontResolver.SelectPreferredFamilyName(Array.Empty<string>()));
        using FontFamily family = WindowsUiFontResolver.CreateFontFamily(
            "LeftPad Missing Font Family");
        using var font = new Font(family, 20f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bitmap = new Bitmap(400, 100, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);

        foreach (string text in RequiredTextSamples())
            Assert.True(graphics.MeasureString(text, font).Width > 0f);
    }

    [Fact]
    public void FontResolver_ResolvedFamilyIsValidOnCurrentMachine()
    {
        using FontFamily family = WindowsUiFontResolver.ResolveUiFontFamily();

        Assert.False(string.IsNullOrWhiteSpace(family.Name));
        Assert.False(string.IsNullOrWhiteSpace(WindowsUiFontResolver.ResolvedFamilyName));
    }

    [Fact]
    public void DisplayText_ContainsOnlyPrimaryMappingContent()
    {
        Assert.Equal(
            new[] { "Primary" },
            typeof(RadialActionDisplayText).GetProperties().Select(property => property.Name));
        Assert.Equal(
            new RadialActionDisplayText("未设置"),
            RadialActionDisplayText.FromMapping(RadialSlotMapping.None));
        Assert.Equal(
            new RadialActionDisplayText("Ctrl+Alt+Shift+Win+K"),
            RadialActionDisplayText.FromMapping(new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardShortcut,
                Key = KeyboardKey.K,
                Ctrl = true,
                Alt = true,
                Shift = true,
                Win = true
            }));
        Assert.Equal(
            new RadialActionDisplayText("CROSS"),
            RadialActionDisplayText.FromMapping(new RadialSlotMapping
            {
                Kind = RadialActionKind.Ds4Button,
                Ds4Button = "cross"
            }));
        Assert.Equal(
            new RadialActionDisplayText("D-Pad Down"),
            RadialActionDisplayText.FromMapping(new RadialSlotMapping
            {
                Kind = RadialActionKind.Ds4Button,
                Ds4Button = "dpad_down"
            }));

        RadialActionDisplayText[] runtimeTexts =
        [
            RadialActionDisplayText.FromMapping(RadialSlotMapping.None),
            RadialActionDisplayText.FromMapping(new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardKey,
                Key = KeyboardKey.K
            }),
            RadialActionDisplayText.FromMapping(new RadialSlotMapping
            {
                Kind = RadialActionKind.Ds4Button,
                Ds4Button = "cross"
            })
        ];
        Assert.All(runtimeTexts, display =>
            Assert.DoesNotContain("Slot", display.Primary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DynamicCache_SameInputsReuseAcrossSelectionRedraws()
    {
        using RadialDynamicContentCache cache = CreateCache();
        RadialMenuSettings settings = SettingsWithRepresentativeMappings();

        Assert.True(cache.Ensure(settings, targetSize: 280));
        Bitmap original = cache.Content;
        foreach (int _ in Enumerable.Range(0, 7))
            Assert.False(cache.Ensure(settings, targetSize: 280));

        Assert.Same(original, cache.Content);
        Assert.Equal(1, cache.BuildCount);
    }

    [Fact]
    public void DynamicCache_ScaleChangeRebuilds()
    {
        using RadialDynamicContentCache cache = CreateCache();
        RadialMenuSettings initial = SettingsWithRepresentativeMappings();
        Assert.True(cache.Ensure(initial, targetSize: 280));
        Bitmap original = cache.Content;

        RadialMenuSettings scaled = initial with { ScalePercent = 140 };
        Assert.True(cache.Ensure(scaled, targetSize: 392));

        Assert.NotSame(original, cache.Content);
        Assert.Equal(new Size(392, 392), cache.Content.Size);
        Assert.Equal(2, cache.BuildCount);
    }

    [Fact]
    public void DynamicCache_MappingChangeRebuilds()
    {
        using RadialDynamicContentCache cache = CreateCache();
        RadialMenuSettings initial = SettingsWithRepresentativeMappings();
        Assert.True(cache.Ensure(initial, targetSize: 280));
        Bitmap original = cache.Content;

        RadialMenuSettings remapped = initial with
        {
            SlotMappings = initial.SlotMappings.WithSlot(1, new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardKey,
                Key = KeyboardKey.Space
            })
        };
        Assert.True(cache.Ensure(remapped, targetSize: 280));

        Assert.NotSame(original, cache.Content);
        Assert.Equal(2, cache.BuildCount);
    }

    [Fact]
    public void DynamicCache_RendersSupersampledPArgbContent()
    {
        using RadialDynamicContentCache cache = CreateCache();
        Assert.True(cache.Ensure(SettingsWithRepresentativeMappings(), targetSize: 280));

        Assert.Equal(4, RadialDynamicContentCache.SupersampleScale);
        Assert.Equal(PixelFormat.Format32bppPArgb, cache.Content.PixelFormat);
        Assert.True(ContainsVisiblePixel(cache.Content));
    }

    private static RadialDynamicContentCache CreateCache() => new(
        RadialVisualPackDefinition.Load(RadialVisualPackDefinition.DefaultDirectory),
        WindowsUiFontResolver.ResolveUiFontFamily());

    private static RadialMenuSettings SettingsWithRepresentativeMappings() =>
        RadialMenuSettings.Default with
        {
            SlotMappings = new RadialSlotMappings(
                RadialSlotMapping.None,
                new RadialSlotMapping
                {
                    Kind = RadialActionKind.KeyboardShortcut,
                    Key = KeyboardKey.K,
                    Ctrl = true,
                    Alt = true,
                    Shift = true,
                    Win = true
                },
                new RadialSlotMapping
                {
                    Kind = RadialActionKind.Ds4Button,
                    Ds4Button = "cross"
                },
                new RadialSlotMapping
                {
                    Kind = RadialActionKind.Ds4Button,
                    Ds4Button = "dpad_down"
                },
                new RadialSlotMapping
                {
                    Kind = RadialActionKind.KeyboardKey,
                    Key = KeyboardKey.Tab
                },
                RadialSlotMapping.None)
        };

    private static IEnumerable<string> RequiredTextSamples()
    {
        yield return "未设置";
        yield return "Ctrl";
        yield return "Alt";
        yield return "Shift";
        yield return "Win";
        yield return "CROSS";
        yield return "D-Pad Down";
    }

    private static bool ContainsVisiblePixel(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A > 0) return true;
            }
        }
        return false;
    }
}
