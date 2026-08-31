using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class ReviewSheetCaptionTests
{
    [Theory]
    [InlineData(TextRenderingHint.SystemDefault)]
    [InlineData(TextRenderingHint.ClearTypeGridFit)]
    public void CaptionDrawsAndPreservesCandidateSourceCopy(TextRenderingHint hint)
    {
        using var sheet = new Bitmap(320, 100, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(sheet);
        graphics.Clear(Color.Black);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.TextRenderingHint = hint;
        using var font = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        ReviewSheetCaption.Draw(graphics, "Idle | Selected slot 3", font, brush, 4, 4);

        Assert.Equal(CompositingMode.SourceCopy, graphics.CompositingMode);
        Assert.Equal(hint, graphics.TextRenderingHint);
        Assert.Contains(Enumerable.Range(0, 320 * 35), index =>
            sheet.GetPixel(index % 320, index / 320).R != 0);

        using var candidate = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        candidate.SetPixel(0, 0, Color.FromArgb(128, 200, 100, 50));
        graphics.DrawImageUnscaled(candidate, 10, 60);
        Assert.Equal(candidate.GetPixel(0, 0), sheet.GetPixel(10, 60));
    }
}
