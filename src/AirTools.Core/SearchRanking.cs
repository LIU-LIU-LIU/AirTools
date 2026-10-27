namespace AirTools.Core;

public static class SearchRanking
{
    public static int Score(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 1;
        if (text.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000;
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index >= 0) return 800 - Math.Min(index, 300);
        var matched = 0;
        foreach (var c in text)
            if (char.ToUpperInvariant(c) == char.ToUpperInvariant(query[matched]) && ++matched == query.Length) return 100;
        return 0;
    }
}

public sealed class LatestQuery : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private bool _disposed;
    public CancellationToken Begin()
    {
        lock (_gate)
        {
            if (_disposed) return new CancellationToken(true);
            _current?.Cancel(); _current?.Dispose();
            _current = new(); return _current.Token;
        }
    }
    public bool TryPublish(CancellationToken token, Action publish)
    {
        lock (_gate)
        {
            if (_disposed || token.IsCancellationRequested || _current == null || token != _current.Token) return false;
            publish(); return true;
        }
    }
    public void Dispose() { lock (_gate) { _disposed = true; _current?.Cancel(); _current?.Dispose(); _current = null; } }
}
