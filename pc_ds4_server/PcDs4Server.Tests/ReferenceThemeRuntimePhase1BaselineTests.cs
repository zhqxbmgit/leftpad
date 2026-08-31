using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;

namespace PcDs4Server.Tests;

public sealed class ReferenceThemeRuntimePhase1BaselineTests
{
    private static readonly int[] Dpis = { 96, 120, 144, 168, 192 };
    private readonly ITestOutputHelper _output;

    public ReferenceThemeRuntimePhase1BaselineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CandidateRenderMatchesFrozenParentBaselineAtEveryDpi()
    {
        var lines = new List<string>();
        foreach (string packId in new[] { "radial-v5", "radial-8-minimal-v1" })
            CapturePack(packId, lines);

        string[] expected = ReadParentBaseline();
        Assert.Equal(238, expected.Length);
        Assert.Equal(expected, lines);
    }

    [Theory]
    [InlineData("radial-v5")]
    [InlineData("radial-8-minimal-v1")]
    public void CandidatePerformanceProbe_ReportsPositiveTimingAndExpectedBundleSize(string packId)
    {
        var lines = new List<string>();
        CapturePerformance(LoadProductionPack(packId), lines);
        string result = Assert.Single(lines);
        _output.WriteLine(result);
        string[] fields = result.Split('|');
        double buildMedianMs = double.Parse(
            fields[2].Split('=')[1],
            System.Globalization.CultureInfo.InvariantCulture);
        double composeMeanUs = double.Parse(
            fields[3].Split('=')[1],
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(buildMedianMs > 0d);
        Assert.True(composeMeanUs > 0d);
        long expectedBytes = packId == "radial-v5" ? 2_508_800L : 3_136_000L;
        Assert.Equal($"physical-cache-bytes={expectedBytes}", fields[4]);
    }

    private static void CapturePack(string packId, ICollection<string> lines)
    {
        string directory = Path.Combine(RadialVisualPackContract.DiscoveryRoot, packId);
        RadialVisualPackDefinition definition = RadialVisualPackDefinition.Load(directory);
        lines.Add(string.Join('|',
            "META",
            packId,
            definition.Manifest.Name,
            definition.Manifest.Version,
            definition.LayoutDefinition.ProfileId,
            definition.LayoutDefinition.SlotCount,
            $"{definition.Layout.Canvas.Width}x{definition.Layout.Canvas.Height}",
            $"{definition.LayoutDefinition.WheelCenter.X:0.####},{definition.LayoutDefinition.WheelCenter.Y:0.####}"));
        lines.Add($"SOURCE|{packId}|base|{FileSha256(definition.BasePath)}");
        lines.Add($"SOURCE|{packId}|selected|{FileSha256(definition.SelectedPath)}");
        lines.Add($"SOURCE|{packId}|layout|{CanonicalAssetHash.JsonSha256(definition.LayoutPath)}");

        foreach (int dpi in Dpis)
        {
            int targetSize = RadialDpiScaling.ToPhysicalPixels(280, dpi);
            using var cache = new RadialVisualPackCache(definition, targetSize);
            AddBitmap(lines, packId, dpi, "cache.base", cache.ScaledBase);
            for (int slot = 1; slot <= definition.LayoutDefinition.SlotCount; slot++)
                AddBitmap(lines, packId, dpi, $"cache.selected.{slot}", cache.GetSelectedSlot(slot));

            foreach ((string fixtureId, RadialMenuSettings settings) in Fixtures(definition))
            {
                using var dynamicContent = new RadialDynamicContentCache(
                    definition,
                    WindowsUiFontResolver.ResolveUiFontFamily());
                Assert.True(dynamicContent.Ensure(settings, targetSize));
                AddBitmap(lines, packId, dpi, $"dynamic.{fixtureId}", dynamicContent.Content);
                using Bitmap idle = Compose(cache, dynamicContent, selectedSlot: 0);
                AddBitmap(lines, packId, dpi, $"final.{fixtureId}.idle", idle);

                if (!string.Equals(fixtureId, "none", StringComparison.Ordinal)) continue;
                for (int slot = 1; slot <= definition.LayoutDefinition.SlotCount; slot++)
                {
                    using Bitmap selected = Compose(cache, dynamicContent, slot);
                    AddBitmap(lines, packId, dpi, $"final.none.selected.{slot}", selected);
                }
            }
        }

    }

    private static string[] ReadParentBaseline()
    {
        Assembly assembly = typeof(ReferenceThemeRuntimePhase1BaselineTests).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("phase1-parent-render-baseline.txt", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded baseline: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<(string Id, RadialMenuSettings Settings)> Fixtures(
        RadialVisualPackDefinition definition)
    {
        string profileId = definition.LayoutDefinition.ProfileId;
        int slotCount = definition.LayoutDefinition.SlotCount;
        RadialMenuSettings BaseSettings() => (RadialMenuSettings.Default with
        {
            VisualPackId = definition.Manifest.Id,
            MappingProfileId = profileId
        }).SetProfileMappings(profileId, RadialSlotMappings.Create(slotCount));

        yield return ("none", BaseSettings());
        yield return ("keyboard-key", BaseSettings().SetProfileMappings(
            profileId,
            RadialSlotMappings.Create(slotCount).WithSlot(1, new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardKey,
                Key = KeyboardKey.F1
            })));
        yield return ("keyboard-shortcut", BaseSettings().SetProfileMappings(
            profileId,
            RadialSlotMappings.Create(slotCount).WithSlot(1, new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardShortcut,
                Key = KeyboardKey.K,
                Ctrl = true,
                Shift = true
            })));
        yield return ("ds4-button", BaseSettings().SetProfileMappings(
            profileId,
            RadialSlotMappings.Create(slotCount).WithSlot(1, new RadialSlotMapping
            {
                Kind = RadialActionKind.Ds4Button,
                Ds4Button = "cross"
            })));
    }

    private static Bitmap Compose(
        RadialVisualPackCache cache,
        RadialDynamicContentCache dynamicContent,
        int selectedSlot)
    {
        var bitmap = new Bitmap(
            cache.TargetSize,
            cache.TargetSize,
            PixelFormat.Format32bppPArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            MethodInfo method = typeof(RadialMenuOverlay).GetMethod(
                "DrawComposition",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(nameof(RadialMenuOverlay), "DrawComposition");
            method.Invoke(null, new object[] { graphics, cache, dynamicContent, selectedSlot });
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void AddBitmap(
        ICollection<string> lines,
        string packId,
        int dpi,
        string surface,
        Bitmap bitmap)
    {
        lines.Add(string.Join('|',
            "PIXEL",
            packId,
            dpi,
            surface,
            bitmap.Width,
            bitmap.Height,
            bitmap.PixelFormat,
            RawPixelSha256(bitmap)));
    }

    private static string RawPixelSha256(Bitmap bitmap)
    {
        Assert.Equal(PixelFormat.Format32bppPArgb, bitmap.PixelFormat);
        Rectangle bounds = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(
            bounds,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            int rowLength = checked(bitmap.Width * 4);
            byte[] row = new byte[rowLength];
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), row, 0, rowLength);
                hash.AppendData(row);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static string FileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CapturePerformance(
        RadialVisualPackDefinition definition,
        ICollection<string> lines)
    {
        RadialMenuSettings settings = Fixtures(definition).First().Settings;
        const int sampleCount = 5;
        var buildSamples = new double[sampleCount];
        for (int sample = 0; sample < sampleCount; sample++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var session = new RadialVisualPackSession(definition, settings, targetSize: 280);
            stopwatch.Stop();
            buildSamples[sample] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(buildSamples);
        using var renderSession = new RadialVisualPackSession(definition, settings, targetSize: 280);
        using Bitmap warmup = Compose(renderSession.AssetCache, renderSession.DynamicContent, 1);
        const int composeSamples = 100;
        var composeStopwatch = Stopwatch.StartNew();
        for (int sample = 0; sample < composeSamples; sample++)
        {
            using Bitmap composed = Compose(
                renderSession.AssetCache,
                renderSession.DynamicContent,
                (sample % definition.LayoutDefinition.SlotCount) + 1);
        }
        composeStopwatch.Stop();

        long bundleBytes = (1L + definition.LayoutDefinition.SlotCount + 1L) * 280 * 280 * 4;
        lines.Add(string.Join('|',
            "PERF",
            definition.Manifest.Id,
            $"build-median-ms={buildSamples[sampleCount / 2]:0.000}",
            $"compose-mean-us={composeStopwatch.Elapsed.TotalMicroseconds / composeSamples:0.000}",
            $"physical-cache-bytes={bundleBytes}"));
    }

    private static RadialVisualPackDefinition LoadProductionPack(string packId) =>
        RadialVisualPackDefinition.Load(Path.Combine(
            RadialVisualPackContract.DiscoveryRoot,
            packId));
}
