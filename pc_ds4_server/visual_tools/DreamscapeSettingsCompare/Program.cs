using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: DreamscapeSettingsCompare <reference> <current> <mask> <output>");
    return 2;
}

string referencePath = Path.GetFullPath(args[0]);
string currentPath = Path.GetFullPath(args[1]);
string maskPath = Path.GetFullPath(args[2]);
string outputPath = Path.GetFullPath(args[3]);
Directory.CreateDirectory(outputPath);

using var referenceSource = new Bitmap(referencePath);
using var currentSource = new Bitmap(currentPath);
using var maskSource = new Bitmap(maskPath);
using Bitmap reference = ToRgb(referenceSource, referenceSource.Size);
using Bitmap current = Normalize(currentSource, reference.Size);
using Bitmap mask = ToRgb(maskSource, reference.Size);
using Bitmap overlay = new(reference.Width, reference.Height, PixelFormat.Format24bppRgb);
using Bitmap difference = new(reference.Width, reference.Height, PixelFormat.Format24bppRgb);

(Metrics raw, Metrics staticOnly, int dynamicPixels) = Compare(
    reference, current, mask, overlay, difference);

reference.Save(Path.Combine(outputPath, "reference-settings.png"), ImageFormat.Png);
current.Save(Path.Combine(outputPath, "current-settings.png"), ImageFormat.Png);
overlay.Save(Path.Combine(outputPath, "overlay-50-settings.png"), ImageFormat.Png);
difference.Save(Path.Combine(outputPath, "difference-settings.png"), ImageFormat.Png);

var report = new
{
    referenceWidth = reference.Width,
    referenceHeight = reference.Height,
    sourceCurrentWidth = currentSource.Width,
    sourceCurrentHeight = currentSource.Height,
    raw,
    staticOnly,
    dynamicMaskPixelCount = dynamicPixels,
    ssim = (double?)null,
    ssimReason = "not calculated; no existing SSIM dependency"
};
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(outputPath, "metrics-settings.json"), json + Environment.NewLine);
Console.WriteLine(json);
return 0;

static Bitmap ToRgb(Image source, Size size)
{
    var target = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
    using Graphics graphics = Graphics.FromImage(target);
    graphics.DrawImage(source, new Rectangle(Point.Empty, size));
    return target;
}

static Bitmap Normalize(Image source, Size targetSize)
{
    double scale = Math.Min(
        source.Width / (double)targetSize.Width,
        source.Height / (double)targetSize.Height);
    int cropWidth = Math.Max(1, (int)Math.Round(targetSize.Width * scale));
    int cropHeight = Math.Max(1, (int)Math.Round(targetSize.Height * scale));
    int left = Math.Max(0, (source.Width - cropWidth) / 2);
    int top = Math.Max(0, (source.Height - cropHeight) / 2);
    var target = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format24bppRgb);
    using Graphics graphics = Graphics.FromImage(target);
    graphics.CompositingMode = CompositingMode.SourceCopy;
    graphics.CompositingQuality = CompositingQuality.HighQuality;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    graphics.DrawImage(
        source,
        new Rectangle(Point.Empty, targetSize),
        new Rectangle(left, top, cropWidth, cropHeight),
        GraphicsUnit.Pixel);
    return target;
}

static unsafe (Metrics Raw, Metrics StaticOnly, int DynamicPixels) Compare(
    Bitmap reference,
    Bitmap current,
    Bitmap mask,
    Bitmap overlay,
    Bitmap difference)
{
    var bounds = new Rectangle(0, 0, reference.Width, reference.Height);
    BitmapData referenceData = reference.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
    BitmapData currentData = current.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
    BitmapData maskData = mask.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
    BitmapData overlayData = overlay.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
    BitmapData differenceData = difference.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
    try
    {
        double absolute = 0;
        double squared = 0;
        long channels = 0;
        double staticAbsolute = 0;
        double staticSquared = 0;
        long staticChannels = 0;
        int dynamicPixels = 0;
        for (int y = 0; y < reference.Height; y++)
        {
            byte* r = (byte*)referenceData.Scan0 + y * referenceData.Stride;
            byte* c = (byte*)currentData.Scan0 + y * currentData.Stride;
            byte* m = (byte*)maskData.Scan0 + y * maskData.Stride;
            byte* o = (byte*)overlayData.Scan0 + y * overlayData.Stride;
            byte* d = (byte*)differenceData.Scan0 + y * differenceData.Stride;
            for (int x = 0; x < reference.Width; x++)
            {
                bool dynamic = m[x * 3] > 0 || m[x * 3 + 1] > 0 || m[x * 3 + 2] > 0;
                if (dynamic)
                    dynamicPixels++;
                for (int channel = 0; channel < 3; channel++)
                {
                    int index = x * 3 + channel;
                    int delta = Math.Abs(r[index] - c[index]);
                    absolute += delta;
                    squared += delta * delta;
                    channels++;
                    if (!dynamic)
                    {
                        staticAbsolute += delta;
                        staticSquared += delta * delta;
                        staticChannels++;
                    }
                    o[index] = (byte)((r[index] + c[index] + 1) / 2);
                    d[index] = (byte)Math.Min(255, delta * 2);
                }
            }
        }
        return (
            new Metrics(absolute / channels, Math.Sqrt(squared / channels)),
            new Metrics(staticAbsolute / staticChannels, Math.Sqrt(staticSquared / staticChannels)),
            dynamicPixels);
    }
    finally
    {
        reference.UnlockBits(referenceData);
        current.UnlockBits(currentData);
        mask.UnlockBits(maskData);
        overlay.UnlockBits(overlayData);
        difference.UnlockBits(differenceData);
    }
}

internal sealed record Metrics(double Mae, double Rmse);
