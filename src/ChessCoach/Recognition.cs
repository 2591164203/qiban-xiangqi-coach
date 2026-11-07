using System.Diagnostics;
using VinXiangQi;
using Yolov5Net.Scorer;

namespace ChessCoach;

public sealed record Detection(Position? Position, bool RedAtBottom, string Message, float Confidence, long Milliseconds, int PieceCount)
{
    public bool Success => Position != null;
    public RectangleF? Grid {get;init;}
}
public sealed class Recognizer : IDisposable
{
    private readonly YoloScorer<YoloXiangQiModel> _scorer;
    // The linked YoloXiangQiModel.cs and these class names come from VinXiangQi.
    private static readonly Dictionary<string, char> PieceMap = new()
    {
        ["b_ma"]='n', ["b_xiang"]='b', ["b_shi"]='a', ["b_jiang"]='k', ["b_che"]='r', ["b_pao"]='c', ["b_bing"]='p',
        ["r_ma"]='N', ["r_xiang"]='B', ["r_shi"]='A', ["r_jiang"]='K', ["r_che"]='R', ["r_pao"]='C', ["r_bing"]='P'
    };

    public Recognizer(string model)
    {
        if (!File.Exists(model)) throw new FileNotFoundException("找不到 VinXiangQi 识别模型", model);
        _scorer = new YoloScorer<YoloXiangQiModel>(model);
    }

    // grid denotes the outermost intersections, NOT the decorative board border.
    public Detection Detect(Bitmap image, RectangleF? knownGrid, bool redToMove, bool? orientation = null)
    {
        var watch=Stopwatch.StartNew();
        var original=DetectSingle(image,knownGrid,redToMove,orientation);
        bool Plausible(Detection d)=>d.Position!=null&&(d.Position.WithTurn(true).Validate(out _)||d.Position.WithTurn(false).Validate(out _));
        // Screen rotation may also rotate the glyphs. Normalize pixels before inference,
        // then map the view orientation back. Never rotate only the FEN or the arrow.
        if(!original.RedAtBottom||!Plausible(original))
        {
            using var rotated=(Bitmap)image.Clone();rotated.RotateFlip(RotateFlipType.Rotate180FlipNone);
            RectangleF? rotatedGrid=knownGrid.HasValue?new RectangleF(image.Width-knownGrid.Value.Right,image.Height-knownGrid.Value.Bottom,knownGrid.Value.Width,knownGrid.Value.Height):null;
            var normalized=DetectSingle(rotated,rotatedGrid,redToMove,orientation.HasValue?!orientation.Value:null);
            normalized=normalized with{RedAtBottom=!normalized.RedAtBottom,Grid=normalized.Grid.HasValue?new RectangleF(image.Width-normalized.Grid.Value.Right,image.Height-normalized.Grid.Value.Bottom,normalized.Grid.Value.Width,normalized.Grid.Value.Height):null};
            if(Plausible(normalized)&&(!Plausible(original)||normalized.PieceCount>original.PieceCount||normalized.PieceCount==original.PieceCount&&normalized.Confidence>original.Confidence))original=normalized;
        }
        return original with{Milliseconds=watch.ElapsedMilliseconds};
    }

    private Detection DetectSingle(Bitmap image, RectangleF? knownGrid, bool redToMove, bool? orientation)
    {
        var watch = Stopwatch.StartNew();
        var predictions = _scorer.Predict(image);
        var pieces = predictions.Where(p => PieceMap.ContainsKey(p.Label.Name) && p.Rectangle.Width / p.Rectangle.Height is > 0.65f and < 1.4f).ToArray();
        // Selection is a search region. It may contain the frame, buttons and margins;
        // it must not override the board intersections detected by the model.
        var board = predictions.Where(p=>p.Label.Name=="board").OrderByDescending(p=>knownGrid.HasValue?Overlap(p.Rectangle,knownGrid.Value):p.Score).ThenByDescending(p=>p.Score).FirstOrDefault();
        var grids=new List<(RectangleF Grid,bool Automatic)>();
        var centers=pieces.Select(p=>Center(p.Rectangle)).ToArray();
        if(board!=null)grids.Add((GridCalibration.Fit(board.Rectangle,centers),true));
        if(knownGrid.HasValue)grids.Add((GridCalibration.Fit(knownGrid.Value,centers),false));
        if(grids.Count==0)return new Detection(null,true,"未识别到完整棋盘，请完整框入棋盘",0,watch.ElapsedMilliseconds,0);
        Detection? fallback=null;
        foreach(var candidate in grids)
        {
            var result=Map(candidate.Grid,candidate.Automatic);
            fallback ??= result;
            if(result.Position!=null&&(result.Position.WithTurn(true).Validate(out _)||result.Position.WithTurn(false).Validate(out _)))return result;
            if(result.Position!=null)fallback=result;
        }
        return fallback!;

        Detection Map(RectangleF grid,bool automatic)
        {
        if (grid.Width < 80 || grid.Height < 90) return Fail("棋盘区域过小");
        var cells = Enumerable.Repeat('.', 90).ToArray();
        var confidence = new float[90];
        float dx=grid.Width/8, dy=grid.Height/9;
        var area=grid; area.Inflate(dx*.48f,dy*.48f);
        foreach (var p in pieces.OrderByDescending(p=>p.Score))
        {
            var center=Center(p.Rectangle);
            if (!area.Contains(center)) continue;
            float gx=(center.X-grid.X)/dx, gy=(center.Y-grid.Y)/dy;
            int x=(int)Math.Round(gx),y=(int)Math.Round(gy);
            if (!Position.Inside(x,y)) continue;
            if (Math.Abs(gx-x)>.31 || Math.Abs(gy-y)>.31) return Fail("等待棋子落稳并自动校准网格");
            char piece=PieceMap[p.Label.Name]; int index=y*9+x;
            if (cells[index]!='.' && cells[index]!=piece) return Fail("同一位置出现不同棋子识别，请重新校准");
            if (p.Score<.70f) return Fail("棋子识别置信度不足");
            if (cells[index]=='.') { cells[index]=piece;confidence[index]=p.Score; }
        }
        int redKing=Array.IndexOf(cells,'K'),blackKing=Array.IndexOf(cells,'k');
        if (redKing<0 || blackKing<0) return Fail("未可靠识别双方将帅，请确保棋盘完整可见");
        bool bottom=orientation ?? redKing/9>=5;
        if (!bottom) Array.Reverse(cells);
        var position=new Position(cells,redToMove);
        int count=cells.Count(c=>c!='.');
        return new Detection(position,bottom,$"{(automatic?"已自动校准 · ":"")}识别 {count} 子",confidence.Where(c=>c>0).DefaultIfEmpty(0).Min(),watch.ElapsedMilliseconds,count){Grid=grid};

        Detection Fail(string message) => new(null,true,message,0,watch.ElapsedMilliseconds,0){Grid=grid};
        }
    }
    private static PointF Center(RectangleF r)=>new(r.X+r.Width/2,r.Y+r.Height/2);
    private static float Overlap(RectangleF a,RectangleF b)
    {
        var intersection=RectangleF.Intersect(a,b);
        float area=intersection.Width*intersection.Height;
        return area/Math.Max(1,a.Width*a.Height+b.Width*b.Height-area);
    }
    public void Dispose()=>_scorer.Dispose();
}

public static class GridCalibration
{
    public static RectangleF Fit(RectangleF seed,IReadOnlyList<PointF> centers)
    {
        var x=FitAxis(seed.Left,seed.Width/8,9,centers.Select(p=>p.X).ToArray());
        var y=FitAxis(seed.Top,seed.Height/9,10,centers.Select(p=>p.Y).ToArray());
        return new RectangleF(x.Origin,y.Origin,x.Step*8,y.Step*9);
    }
    private static (float Origin,float Step) FitAxis(float origin,float step,int count,float[] positions)
    {
        if(step<1)return(origin,step);
        var samples=positions.Select(p=>(Index:(int)Math.Round((p-origin)/step),Value:p))
            .Where(p=>p.Index>=0&&p.Index<count&&Math.Abs((p.Value-origin)/step-p.Index)<.32).ToArray();
        if(samples.Select(p=>p.Index).Distinct().Count()<3||samples.Max(p=>p.Index)-samples.Min(p=>p.Index)<3)return(origin,step);
        double meanIndex=samples.Average(p=>p.Index),meanValue=samples.Average(p=>p.Value);
        double variance=samples.Sum(p=>Math.Pow(p.Index-meanIndex,2));
        double fittedStep=samples.Sum(p=>(p.Index-meanIndex)*(p.Value-meanValue))/variance;
        double fittedOrigin=meanValue-fittedStep*meanIndex;
        if(fittedStep<step*.88||fittedStep>step*1.12||Math.Abs(fittedOrigin-origin)>step*.4)return(origin,step);
        return((float)fittedOrigin,(float)fittedStep);
    }
}
