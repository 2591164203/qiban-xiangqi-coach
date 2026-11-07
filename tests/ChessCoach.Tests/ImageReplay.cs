using ChessCoach;
using System.Drawing;

internal static class ImageReplay
{
    // Deterministically move the existing piece pixels in the local QQ fixture.
    // This is a computer-vision regression input, not a live game operation.
    public static Bitmap Move(Bitmap source,RectangleF grid,string move)
    {
        Position.TryCoordinates(move,out var from,out var to);
        float sx=grid.Width/8,sy=grid.Height/9;
        int w=(int)Math.Round(sx*1.03),h=(int)Math.Round(sy*1.03);
        Rectangle Cell(Point p)=>new((int)Math.Round(grid.Left+p.X*sx-w/2f),(int)Math.Round(grid.Top+p.Y*sy-h/2f),w,h);
        var result=(Bitmap)source.Clone();
        using var piece=source.Clone(Cell(from),System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var blank=source.Clone(Cell(new Point(1,8)),System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var graphics=Graphics.FromImage(result);
        graphics.DrawImageUnscaled(blank,Cell(from).Location);
        graphics.DrawImageUnscaled(piece,Cell(to).Location);
        return result;
    }
}
