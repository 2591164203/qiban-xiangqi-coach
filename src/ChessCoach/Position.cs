using System.Text;

namespace ChessCoach;

// Canonical coordinates: a9 at upper left, red at the bottom. Screens may be rotated.
public sealed class Position
{
    public const string StartFen = "rnbakabnr/9/1c5c1/p1p1p1p1p/9/9/P1P1P1P1P/1C5C1/9/RNBAKABNR w - - 0 1";
    public char[] Cells { get; }
    public bool RedToMove { get; }
    public string Key => new(Cells);
    public char this[int x, int y] => Cells[y * 9 + x];

    public Position(char[] cells, bool redToMove)
    {
        if (cells.Length != 90) throw new ArgumentException("棋盘必须有 90 个交叉点");
        Cells = (char[])cells.Clone();
        RedToMove = redToMove;
    }

    public Position WithTurn(bool red) => new(Cells, red);
    public static bool IsRed(char p) => char.IsUpper(p);
    public static bool Inside(int x, int y) => x >= 0 && x < 9 && y >= 0 && y < 10;
    public static bool Palace(int x, int y, bool red) => x >= 3 && x <= 5 && (red ? y >= 7 && y <= 9 : y >= 0 && y <= 2);

    public static Position ParseFen(string fen)
    {
        var fields = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || fields[1] is not ("w" or "b")) throw new FormatException("FEN 缺少正确的行棋方 w/b");
        var rows = fields[0].Split('/');
        if (rows.Length != 10) throw new FormatException("FEN 应有十行");
        var cells = Enumerable.Repeat('.', 90).ToArray();
        for (int y = 0; y < 10; y++)
        {
            int x = 0;
            foreach (char c in rows[y])
            {
                if (c >= '1' && c <= '9') x += c - '0';
                else
                {
                    if (!"rnbakcpRNBAKCP".Contains(c) || x >= 9) throw new FormatException("FEN 含未知棋子或行过长");
                    cells[y * 9 + x++] = c;
                }
            }
            if (x != 9) throw new FormatException("FEN 每行应有九路");
        }
        return new Position(cells, fields[1] == "w");
    }

    public string ToFen()
    {
        var s = new StringBuilder();
        for (int y = 0; y < 10; y++)
        {
            int empty = 0;
            for (int x = 0; x < 9; x++)
            {
                char c = this[x, y];
                if (c == '.') empty++;
                else { if (empty > 0) s.Append(empty); empty = 0; s.Append(c); }
            }
            if (empty > 0) s.Append(empty);
            if (y < 9) s.Append('/');
        }
        return s + (RedToMove ? " w - - 0 1" : " b - - 0 1");
    }

    public bool Validate(out string error)
    {
        error = "";
        foreach (bool red in new[] { false, true })
        {
            char king = red ? 'K' : 'k';
            if (Cells.Count(c => c == king) != 1) { error = "未可靠识别双方将帅"; return false; }
            foreach (var (piece, max) in new[] { ('r', 2), ('n', 2), ('b', 2), ('a', 2), ('c', 2), ('p', 5) })
            {
                if (Cells.Count(c => c == (red ? char.ToUpper(piece) : piece)) > max)
                { error = "棋子数量异常，请检查识别或校对"; return false; }
            }
            for (int y = 0; y < 10; y++) for (int x = 0; x < 9; x++)
            {
                char c = this[x, y];
                if (c == '.' || IsRed(c) != red) continue;
                char kind = char.ToLower(c);
                if ((kind == 'k' || kind == 'a') && !Palace(x, y, red)) { error = "将帅或仕士不在九宫内"; return false; }
                if (kind == 'a' && (x+y)%2 != (red?0:1)) { error = "仕士不在可达位置"; return false; }
                if (kind == 'b' && (red ? y < 5 : y > 4)) { error = "相象位置异常"; return false; }
                if (kind == 'b')
                {
                    int row=red?9-y:y;
                    if (!(row==2 && x is 0 or 4 or 8 || row is 0 or 4 && x is 2 or 6)) { error = "相象不在可达位置"; return false; }
                }
                if (kind == 'p' && (red ? y > 6 : y < 3)) { error = "兵卒位置异常"; return false; }
                if (kind == 'p' && (red?y>=5:y<=4) && x%2!=0) { error = "未过河兵卒位置异常"; return false; }
            }
        }
        if (InCheck(!RedToMove)) { error = "非行棋方被将军：请确认轮到谁走，或校对棋子"; return false; }
        return true;
    }

    private int Between(Point from, Point to)
    {
        if (from.X != to.X && from.Y != to.Y) return -1;
        int dx = Math.Sign(to.X - from.X), dy = Math.Sign(to.Y - from.Y), count = 0;
        for (int x = from.X + dx, y = from.Y + dy; x != to.X || y != to.Y; x += dx, y += dy)
            if (this[x, y] != '.') count++;
        return count;
    }

    private bool CanReach(Point from, Point to)
    {
        if (!Inside(from.X, from.Y) || !Inside(to.X, to.Y) || from == to) return false;
        char piece = this[from.X, from.Y], target = this[to.X, to.Y];
        if (piece == '.' || (target != '.' && IsRed(piece) == IsRed(target))) return false;
        bool red = IsRed(piece);
        int dx = to.X - from.X, dy = to.Y - from.Y, ax = Math.Abs(dx), ay = Math.Abs(dy);
        switch (char.ToLower(piece))
        {
            case 'r': return Between(from, to) == 0;
            case 'c': return Between(from, to) == (target == '.' ? 0 : 1);
            case 'n':
                return ax == 2 && ay == 1 && this[from.X + dx / 2, from.Y] == '.' ||
                       ax == 1 && ay == 2 && this[from.X, from.Y + dy / 2] == '.';
            case 'b': return ax == 2 && ay == 2 && (red ? to.Y >= 5 : to.Y <= 4) && this[from.X + dx / 2, from.Y + dy / 2] == '.';
            case 'a': return ax == 1 && ay == 1 && Palace(to.X, to.Y, red);
            case 'k':
                if (char.ToLower(target) == 'k' && dx == 0 && Between(from, to) == 0) return true;
                return ax + ay == 1 && Palace(to.X, to.Y, red);
            case 'p':
                return dx == 0 && dy == (red ? -1 : 1) ||
                       ay == 0 && ax == 1 && (red ? from.Y <= 4 : from.Y >= 5);
            default: return false;
        }
    }

    public bool InCheck(bool red)
    {
        int k = Array.IndexOf(Cells, red ? 'K' : 'k');
        if (k < 0) return true;
        var target = new Point(k % 9, k / 9);
        for (int i = 0; i < 90; i++)
            if (Cells[i] != '.' && IsRed(Cells[i]) != red && CanReach(new Point(i % 9, i / 9), target)) return true;
        return false;
    }

    public static bool TryCoordinates(string uci, out Point from, out Point to)
    {
        from = to = Point.Empty;
        if (uci.Length != 4 || uci[0] < 'a' || uci[0] > 'i' || uci[2] < 'a' || uci[2] > 'i' ||
            uci[1] < '0' || uci[1] > '9' || uci[3] < '0' || uci[3] > '9') return false;
        from = new Point(uci[0] - 'a', 9 - (uci[1] - '0'));
        to = new Point(uci[2] - 'a', 9 - (uci[3] - '0'));
        return from != to;
    }

    public bool IsLegal(string uci)
    {
        if (!TryCoordinates(uci, out var from, out var to)) return false;
        char p = this[from.X, from.Y];
        if (p == '.' || IsRed(p) != RedToMove || char.ToLower(this[to.X, to.Y]) == 'k' || !CanReach(from, to)) return false;
        return !ApplyUnchecked(from, to).InCheck(RedToMove);
    }

    private Position ApplyUnchecked(Point from, Point to)
    {
        var cells = (char[])Cells.Clone();
        cells[to.Y * 9 + to.X] = cells[from.Y * 9 + from.X];
        cells[from.Y * 9 + from.X] = '.';
        return new Position(cells, !RedToMove);
    }

    public Position Apply(string move)
    {
        if (!IsLegal(move)) throw new InvalidOperationException("走法不合法：" + move);
        TryCoordinates(move, out var from, out var to);
        return ApplyUnchecked(from, to);
    }

    public bool TryTransition(Position next, out string move)
    {
        move = "";
        var changed = Enumerable.Range(0, 90).Where(i => Cells[i] != next.Cells[i]).ToArray();
        if (changed.Length != 2) return false;
        int source = changed.FirstOrDefault(i => next.Cells[i] == '.' && Cells[i] != '.', -1);
        if (source < 0) return false;
        int dest = changed.First(i => i != source);
        if (Cells[source] != next.Cells[dest]) return false;
        move = string.Concat((char)('a' + source % 9), (char)('0' + 9 - source / 9), (char)('a' + dest % 9), (char)('0' + 9 - dest / 9));
        return IsLegal(move) && Apply(move).Key == next.Key;
    }

    public IEnumerable<string> LegalMoves()
    {
        for(int i=0;i<90;i++)
        {
            char p=Cells[i];if(p=='.'||IsRed(p)!=RedToMove)continue;
            int x=i%9,y=i/9;
            IEnumerable<Point> destinations=char.ToLower(p) switch
            {
                'n'=>new[]{new Point(x-2,y-1),new Point(x-2,y+1),new Point(x+2,y-1),new Point(x+2,y+1),new Point(x-1,y-2),new Point(x+1,y-2),new Point(x-1,y+2),new Point(x+1,y+2)},
                'b'=>new[]{new Point(x-2,y-2),new Point(x+2,y-2),new Point(x-2,y+2),new Point(x+2,y+2)},
                'a'=>new[]{new Point(x-1,y-1),new Point(x+1,y-1),new Point(x-1,y+1),new Point(x+1,y+1)},
                'k'=>new[]{new Point(x-1,y),new Point(x+1,y),new Point(x,y-1),new Point(x,y+1)},
                'p'=>new[]{new Point(x,y+(IsRed(p)?-1:1)),new Point(x-1,y),new Point(x+1,y)},
                _=>Enumerable.Range(0,9).Select(tx=>new Point(tx,y)).Concat(Enumerable.Range(0,10).Select(ty=>new Point(x,ty)))
            };
            foreach(var dest in destinations)
            {
                if(!Inside(dest.X,dest.Y))continue;
                string move=string.Concat((char)('a'+x),(char)('0'+9-y),(char)('a'+dest.X),(char)('0'+9-dest.Y));
                if(IsLegal(move))yield return move;
            }
        }
    }

    public int Differences(Position other)
    {
        int count=0;for(int i=0;i<90;i++)if(Cells[i]!=other.Cells[i])count++;
        return count;
    }

    public static string PieceName(char c) => c switch
    {
        'K' => "帅", 'k' => "将", 'A' => "仕", 'a' => "士", 'B' => "相", 'b' => "象",
        'N' or 'n' => "马", 'R' or 'r' => "车", 'C' or 'c' => "炮", 'P' => "兵", 'p' => "卒", _ => "·"
    };

    public string Describe(string move)
    {
        if (!TryCoordinates(move, out var from, out var to) || this[from.X, from.Y] == '.') return move;
        char p = this[from.X, from.Y];
        bool red = IsRed(p);
        string Number(int n) => red ? "零一二三四五六七八九"[n].ToString() : n.ToString();
        int file = red ? 9 - from.X : from.X + 1, dest = red ? 9 - to.X : to.X + 1;
        var sameFile = Enumerable.Range(0, 10).Where(y => this[from.X, y] == p).OrderBy(y => red ? y : -y).ToArray();
        string start = PieceName(p) + Number(file);
        if (sameFile.Length > 1 && "rncp".Contains(char.ToLower(p)))
        {
            int index = Array.IndexOf(sameFile, from.Y);
            string prefix = index == 0 ? "前" : index == sameFile.Length - 1 ? "后" : sameFile.Length == 3 ? "中" : Number(index + 1);
            start = prefix + PieceName(p);
        }
        if (from.Y == to.Y) return start + "平" + Number(dest);
        bool advance = red ? to.Y < from.Y : to.Y > from.Y;
        int count = "nba".Contains(char.ToLower(p)) ? dest : Math.Abs(to.Y - from.Y);
        return start + (advance ? "进" : "退") + Number(count);
    }

    public string DescribeLine(IEnumerable<string> moves, int limit = 6)
    {
        var result = new List<string>();
        Position pos = this;
        foreach (var move in moves.Take(limit))
        {
            if (!pos.IsLegal(move)) break;
            result.Add(pos.Describe(move));
            pos = pos.Apply(move);
        }
        return string.Join(" → ", result);
    }
}
