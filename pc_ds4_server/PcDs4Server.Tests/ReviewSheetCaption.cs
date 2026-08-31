using System.Drawing;
using System.Drawing.Drawing2D;

namespace PcDs4Server.Tests;

internal static class ReviewSheetCaption
{
    public static void Draw(Graphics graphics, string text, Font font, Brush brush, float x, float y)
    {
        CompositingMode original = graphics.CompositingMode;
        try
        {
            // GDI+ text (including ClearType) cannot reliably draw with SourceCopy.
            // Only review annotations use SourceOver; candidate images keep their authority.
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.DrawString(text, font, brush, x, y);
        }
        finally
        {
            graphics.CompositingMode = original;
        }
    }
}
