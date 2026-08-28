using System.Drawing;
using System.Drawing.Imaging;

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "Usage: DreamscapeChromeRepair <reference.png> <current-static-art.png> <output.png>");
    return 2;
}

string referencePath = Path.GetFullPath(args[0]);
string currentPath = Path.GetFullPath(args[1]);
string outputPath = Path.GetFullPath(args[2]);
using var reference = new Bitmap(referencePath);
using var current = new Bitmap(currentPath);
if (reference.Size != new Size(1672, 941) || current.Size != reference.Size)
    throw new InvalidDataException("Dreamscape Settings art must be 1672x941.");

using var repaired = new Bitmap(current);

// Restore the original continuous sky where the former broad inpaint mask
// created the blue rectangle. No other Settings art lies in this region.
for (int y = 0; y < 90; y++)
for (int x = 1500; x < 1672; x++)
    repaired.SetPixel(x, y, reference.GetPixel(x, y));

RemoveGlyph(
    repaired,
    reference,
    outer: Rectangle.FromLTRB(1543, 27, 1583, 52),
    inner: Rectangle.FromLTRB(1550, 34, 1577, 45));
RemoveGlyph(
    repaired,
    reference,
    outer: Rectangle.FromLTRB(1603, 19, 1643, 60),
    inner: Rectangle.FromLTRB(1610, 26, 1636, 53));

string? directory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrEmpty(directory))
    Directory.CreateDirectory(directory);
repaired.Save(outputPath, ImageFormat.Png);
Console.WriteLine($"repaired={outputPath}");
return 0;

static void RemoveGlyph(
    Bitmap target,
    Bitmap source,
    Rectangle outer,
    Rectangle inner)
{
    for (int y = outer.Top; y < outer.Bottom; y++)
    for (int x = outer.Left; x < outer.Right; x++)
    {
        Color left = source.GetPixel(outer.Left - 1, y);
        Color right = source.GetPixel(outer.Right, y);
        Color top = source.GetPixel(x, outer.Top - 1);
        Color bottom = source.GetPixel(x, outer.Bottom);
        double horizontalT = (x - outer.Left) / (double)Math.Max(1, outer.Width - 1);
        double verticalT = (y - outer.Top) / (double)Math.Max(1, outer.Height - 1);
        Color horizontal = Lerp(left, right, horizontalT);
        Color vertical = Lerp(top, bottom, verticalT);
        Color patch = Lerp(horizontal, vertical, 0.5);

        double featherX = x < inner.Left
            ? (x - outer.Left) / (double)Math.Max(1, inner.Left - outer.Left)
            : x >= inner.Right
                ? (outer.Right - 1 - x) / (double)Math.Max(1, outer.Right - inner.Right)
                : 1;
        double featherY = y < inner.Top
            ? (y - outer.Top) / (double)Math.Max(1, inner.Top - outer.Top)
            : y >= inner.Bottom
                ? (outer.Bottom - 1 - y) / (double)Math.Max(1, outer.Bottom - inner.Bottom)
                : 1;
        double alpha = SmoothStep(Math.Clamp(Math.Min(featherX, featherY), 0, 1));
        target.SetPixel(x, y, Lerp(source.GetPixel(x, y), patch, alpha));
    }
}

static double SmoothStep(double value) => value * value * (3 - 2 * value);

static Color Lerp(Color from, Color to, double amount) => Color.FromArgb(
    255,
    Blend(from.R, to.R, amount),
    Blend(from.G, to.G, amount),
    Blend(from.B, to.B, amount));

static int Blend(byte from, byte to, double amount) =>
    Math.Clamp((int)Math.Round(from + (to - from) * amount), 0, 255);
