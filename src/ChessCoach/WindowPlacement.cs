namespace ChessCoach;

public static class WindowPlacement
{
    public static Rectangle? Beside(Rectangle workingArea,Rectangle avoid,Size size,int gap=16)
    {
        if(size.Width>workingArea.Width||size.Height>workingArea.Height)return null;
        int y=Math.Clamp(avoid.Top,workingArea.Top,workingArea.Bottom-size.Height);
        int x=Math.Clamp(avoid.Left,workingArea.Left,workingArea.Right-size.Width);
        var candidates=new[]{
            new Rectangle(avoid.Right+gap,y,size.Width,size.Height),
            new Rectangle(avoid.Left-gap-size.Width,y,size.Width,size.Height),
            new Rectangle(x,avoid.Bottom+gap,size.Width,size.Height),
            new Rectangle(x,avoid.Top-gap-size.Height,size.Width,size.Height)
        };
        return candidates.Cast<Rectangle?>().FirstOrDefault(r=>workingArea.Contains(r!.Value)&&!r.Value.IntersectsWith(avoid));
    }
}
