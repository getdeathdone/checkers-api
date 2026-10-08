using System.Diagnostics;
using System.Text.RegularExpressions;
using CheckersApi.Core.Logic;
using CheckersApi.Core.Models;

namespace CheckersApi.Core.Engine;

/// <summary>
/// CLI adapter for KingsRow / Chinook checkers engine.
/// Manages a long-lived engine child process communicating via stdin/stdout.
/// </summary>
public class KingsRowCliAdapter : IEngineAdapter
{
    private readonly string _executablePath;
    private readonly string? _databasePath;
    private readonly BuiltInCheckersEngine _fallbackEngine = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _useFallback;
    private bool _disposed;

    public string EngineName => _useFallback ? "chinook-fallback" : "chinook";
    public bool IsAlive => _useFallback ? _fallbackEngine.IsAlive : (_process != null && !_process.HasExited);

    public KingsRowCliAdapter(string executablePath, string? databasePath = null)
    {
        _executablePath = executablePath;
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_executablePath) || !File.Exists(_executablePath))
        {
            _useFallback = true;
            await _fallbackEngine.InitializeAsync(cancellationToken);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                Arguments = !string.IsNullOrEmpty(_databasePath) ? $"-tb \"{_databasePath}\"" : "",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo };
            _process.Start();

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            // Warm up engine
            await SendCommandAsync("init", cancellationToken);
            await SendCommandAsync("warmup", cancellationToken);
        }
        catch
        {
            // If starting the process fails, gracefully fall back
            _useFallback = true;
            await _fallbackEngine.InitializeAsync(cancellationToken);
        }
    }

    public async Task SetPositionAsync(string pdn, CancellationToken cancellationToken = default)
    {
        if (_useFallback)
        {
            await _fallbackEngine.SetPositionAsync(pdn, cancellationToken);
            return;
        }

        await SendCommandAsync($"set {pdn}", cancellationToken);
    }

    public async Task<TablebaseProbeResult?> ProbeTablebaseAsync(BoardPosition board, CancellationToken cancellationToken = default)
    {
        if (board.TotalPieceCount() <= 8)
        {
            if (_useFallback)
            {
                return await _fallbackEngine.ProbeTablebaseAsync(board, cancellationToken);
            }

            // KingsRow tbprobe / probe command
            var sw = Stopwatch.StartNew();
            await SendCommandAsync($"probe {board.ToCanonicalPdn()}", cancellationToken);
            string? line = await ReadLineWithTimeoutAsync(50, cancellationToken);
            sw.Stop();

            if (!string.IsNullOrEmpty(line) && line.Contains("bestmove", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"bestmove\s+([0-9x\-]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return new TablebaseProbeResult(
                        Hit: true,
                        BestMove: match.Groups[1].Value,
                        ScoreOrWdl: 0,
                        Depth: 1,
                        TimeMs: sw.ElapsedMilliseconds);
                }
            }

            // Fallback to tablebase prober if command returned without hit
            return await _fallbackEngine.ProbeTablebaseAsync(board, cancellationToken);
        }

        return null;
    }

    public async Task<EngineSearchResult> SearchAsync(SearchLimits limits, CancellationToken cancellationToken = default)
    {
        if (_useFallback)
        {
            return await _fallbackEngine.SearchAsync(limits, cancellationToken);
        }

        int maxDepth = limits.MaxDepth ?? 12;
        int softTimeMs = limits.SoftTimeMs ?? 250;

        var sw = Stopwatch.StartNew();
        await SendCommandAsync($"go depth {maxDepth} movetime {softTimeMs}", cancellationToken);

        string bestMove = "";
        var pv = new List<string>();
        int depth = maxDepth;
        long nodes = 0;
        int score = 0;
        bool tablebaseHit = false;

        while (sw.ElapsedMilliseconds < softTimeMs + 500 && !cancellationToken.IsCancellationRequested)
        {
            string? line = await ReadLineWithTimeoutAsync(100, cancellationToken);
            if (line == null) break;

            if (line.StartsWith("info", StringComparison.OrdinalIgnoreCase))
            {
                var depthMatch = Regex.Match(line, @"depth\s+(\d+)");
                if (depthMatch.Success) depth = int.Parse(depthMatch.Groups[1].Value);

                var nodesMatch = Regex.Match(line, @"nodes\s+(\d+)");
                if (nodesMatch.Success) nodes = long.Parse(nodesMatch.Groups[1].Value);

                var scoreMatch = Regex.Match(line, @"score\s+([-\d]+)");
                if (scoreMatch.Success) score = int.Parse(scoreMatch.Groups[1].Value);

                if (line.Contains("tbhits", StringComparison.OrdinalIgnoreCase))
                    tablebaseHit = true;

                var pvMatch = Regex.Match(line, @"pv\s+(.+)$");
                if (pvMatch.Success)
                {
                    pv = pvMatch.Groups[1].Value
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .ToList();
                }
            }
            else if (line.StartsWith("bestmove", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"bestmove\s+([0-9x\-]+)");
                if (match.Success)
                {
                    bestMove = match.Groups[1].Value;
                    break;
                }
            }
        }

        sw.Stop();

        if (string.IsNullOrEmpty(bestMove))
        {
            // If KingsRow CLI didn't reply in time, fallback to internal engine
            return await _fallbackEngine.SearchAsync(limits, cancellationToken);
        }

        return new EngineSearchResult(
            BestMove: bestMove,
            Pv: pv.Count > 0 ? pv : new List<string> { bestMove },
            ScoreOrWdl: score,
            Nodes: nodes > 0 ? nodes : 1000,
            Depth: depth,
            TablebaseHit: tablebaseHit,
            TimeMs: sw.ElapsedMilliseconds);
    }

    private async Task SendCommandAsync(string command, CancellationToken ct)
    {
        if (_stdin != null && _process != null && !_process.HasExited)
        {
            await _stdin.WriteLineAsync(command.AsMemory(), ct);
            await _stdin.FlushAsync(ct);
        }
    }

    private async Task<string?> ReadLineWithTimeoutAsync(int timeoutMs, CancellationToken ct)
    {
        if (_stdout == null) return null;

        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            return await _stdout.ReadLineAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _stdin?.WriteLine("quit");
            _stdin?.Flush();
            _process?.WaitForExit(300);
            _process?.Kill();
            _process?.Dispose();
        }
        catch
        {
            // Ignore termination errors
        }

        _fallbackEngine.Dispose();
    }
}
