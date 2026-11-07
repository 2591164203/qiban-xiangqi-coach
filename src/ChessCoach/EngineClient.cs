using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ChessCoach;

public sealed record Candidate(int Rank, int Depth, int Score, bool IsMate, string[] Moves)
{
    public string Evaluation => IsMate ? $"终局分 {Score}" : (Score / 100d).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
}
public sealed record Analysis(string BestMove, Candidate[] Candidates, long ElapsedMs);

public sealed class EngineClient(string path)
{
    public async Task<Analysis> AnalyzeAsync(Position position, int milliseconds, CancellationToken ct, string? rootFen = null, IReadOnlyList<string>? history = null)
    {
        if (!position.Validate(out var error)) throw new InvalidOperationException(error);
        if (!File.Exists(path)) throw new FileNotFoundException("找不到皮卡鱼引擎", path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15 + milliseconds / 1000));
        var token = deadline.Token;
        using var process = new Process { StartInfo = new ProcessStartInfo(path) {
            WorkingDirectory = Path.GetDirectoryName(path)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        }};
        if (!process.Start()) throw new InvalidOperationException("皮卡鱼未能启动");
        using var kill = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var stderr = process.StandardError.ReadToEndAsync();
        var recent = new Queue<string>();
        try
        {
            async Task Send(string command) { await process.StandardInput.WriteLineAsync(command.AsMemory(), token); await process.StandardInput.FlushAsync(token); }
            async Task<string> Read()
            {
                string? line = await process.StandardOutput.ReadLineAsync(token);
                token.ThrowIfCancellationRequested();
                if (line == null) throw new InvalidOperationException("引擎提前退出：" + string.Join(" / ", recent));
                recent.Enqueue(line); while (recent.Count > 6) recent.Dequeue();
                if (line.Contains("CRITICAL ERROR", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("引擎拒绝局面：" + line);
                return line;
            }
            await Send("uci");
            while (await Read() != "uciok") { }
            await Send("setoption name Threads value " + Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
            await Send("setoption name Hash value 64");
            await Send("setoption name MultiPV value 3");
            await Send("isready");
            while (await Read() != "readyok") { }
            // Preserve confirmed move history for repetition and chasing evaluation.
            string command = "position fen " + (rootFen ?? position.ToFen());
            if (history is { Count: > 0 }) command += " moves " + string.Join(' ', history);
            await Send(command);
            var watch = Stopwatch.StartNew();
            await Send("go movetime " + Math.Clamp(milliseconds, 200, 10000));
            var batches = new Dictionary<int, Dictionary<int, Candidate>>();
            string best;
            while (true)
            {
                string line = await Read();
                if (line.StartsWith("bestmove ")) { best = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]; break; }
                var candidate = ParseInfo(line);
                if (candidate != null && position.IsLegal(candidate.Moves[0]))
                {
                    if (!batches.TryGetValue(candidate.Depth, out var batch)) batches[candidate.Depth] = batch = new();
                    batch[candidate.Rank] = candidate;
                }
            }
            if (best != "(none)" && best != "0000" && !position.IsLegal(best)) throw new InvalidOperationException("引擎走法与当前局面不匹配，已丢弃提示");
            await Send("quit");
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new InvalidOperationException("引擎异常退出：" + await stderr);
            // A timed search can stop halfway through a new MultiPV depth. Mixing
            // ranks from different depths produced duplicated first moves.
            var completed = batches.OrderByDescending(b => b.Key).FirstOrDefault(b => b.Value.Count >= 3).Value;
            var latest = batches.OrderByDescending(b => b.Key).SelectMany(b => b.Value.Values).ToArray();
            var primary = latest.FirstOrDefault(c => c.Moves[0] == best);
            var ranked = new List<Candidate>();
            if (primary != null) ranked.Add(primary);
            foreach (var c in (completed?.Values.OrderBy(c => c.Rank) ?? latest.AsEnumerable()).Concat(latest))
                if (!ranked.Any(r => r.Moves[0] == c.Moves[0])) ranked.Add(c);
            return new Analysis(best, ranked.Take(3).Select((c,i) => c with { Rank=i+1 }).ToArray(), watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("皮卡鱼响应超时，请重试或减少思考时间"); }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    }

    public static Candidate? ParseInfo(string line)
    {
        var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int pv = Array.IndexOf(t, "pv"), score = Array.IndexOf(t, "score"), depth = Array.IndexOf(t, "depth"), rank = Array.IndexOf(t, "multipv");
        if (t.Length == 0 || t[0] != "info" || pv < 0 || pv + 1 >= t.Length || score < 0 || score + 2 >= t.Length || depth < 0 || depth + 1 >= t.Length) return null;
        if (t.Contains("lowerbound") || t.Contains("upperbound") || !int.TryParse(t[score + 2], out int value) || !int.TryParse(t[depth + 1], out int d)) return null;
        int r = rank >= 0 && rank + 1 < t.Length && int.TryParse(t[rank + 1], out int m) ? m : 1;
        if (t[score + 1] is not ("cp" or "mate")) return null;
        return new Candidate(r, d, value, t[score + 1] == "mate", t.Skip(pv + 1).ToArray());
    }
}
