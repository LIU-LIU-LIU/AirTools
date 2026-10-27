using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AirTools.Services;

public static class SearchRanking
{
    public static int Score(string value, string query)
    {
        if (query.Length == 0) return 1;
        if (value.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000;
        if (value.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 800;
        if (value.Contains(query, StringComparison.OrdinalIgnoreCase)) return 600;
        int p = 0;
        foreach (char c in value) if (char.ToUpperInvariant(c) == char.ToUpperInvariant(query[p]) && ++p == query.Length) return 100;
        return 0;
    }
}

public sealed class FileSearchService : IDisposable
{
    private readonly ConcurrentDictionary<string, byte> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private CancellationTokenSource _cancel = new();
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj", "dist", ".venv", "venv", "target" };
    public string Status { get; private set; } = "";
    public void Rebuild(IEnumerable<string> roots)
    {
        _cancel.Cancel(); _cancel.Dispose(); _cancel = new();
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear(); _files.Clear();
        var token = _cancel.Token;
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                watcher.Created += (_, e) => { if (!token.IsCancellationRequested) { if (File.Exists(e.FullPath) && Allowed(e.FullPath)) _files[e.FullPath] = 0; else if (Directory.Exists(e.FullPath)) _ = Task.Run(() => Scan(e.FullPath, token)); } };
                watcher.Deleted += (_, e) => Remove(e.FullPath);
                watcher.Renamed += (_, e) => { Remove(e.OldFullPath); if (!token.IsCancellationRequested) _ = Task.Run(() => ScanPath(e.FullPath, token)); };
                watcher.Error += (_, _) => { if (!token.IsCancellationRequested) _ = Task.Run(() => Scan(root, token)); };
                watcher.EnableRaisingEvents = true; _watchers.Add(watcher);
            }
            catch (Exception ex) { Status = ex.Message; }
            _ = Task.Run(() => Scan(root, token));
        }
    }
    private static bool Allowed(string path) => !path.Split(Path.DirectorySeparatorChar).Any(Excluded.Contains);
    private void Remove(string path) { foreach (var key in _files.Keys) if (key.Equals(path, StringComparison.OrdinalIgnoreCase) || key.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) _files.TryRemove(key, out _); }
    private void ScanPath(string path, CancellationToken token) { if (Directory.Exists(path)) Scan(path, token); else if (File.Exists(path) && Allowed(path) && !token.IsCancellationRequested) _files[path] = 0; }
    private void Scan(string root, CancellationToken token)
    {
        if (!Allowed(root)) return;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0 && !token.IsCancellationRequested)
        {
            var dir = pending.Pop();
            try
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var path in Directory.EnumerateFiles(dir)) { if (token.IsCancellationRequested) return; _files[path] = 0; }
                foreach (var child in Directory.EnumerateDirectories(dir)) if (!Excluded.Contains(Path.GetFileName(child))) pending.Push(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "部分目录无权限，已跳过"; }
        }
    }
    public string[] Search(string query, LauncherSettings settings, CancellationToken token)
    {
        return settings.Favorites.Concat(settings.RecentFiles).Concat(_files.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => { token.ThrowIfCancellationRequested(); return (Path: path, Score: SearchRanking.Score(path, query) + (settings.Favorites.Contains(path) ? 100 : settings.RecentFiles.Contains(path) ? 50 : 0)); })
            .Where(x => SearchRanking.Score(x.Path, query) > 0 && File.Exists(x.Path)).OrderByDescending(x => x.Score).ThenBy(x => x.Path.Length).Take(50).Select(x => x.Path).ToArray();
    }
    public void Dispose() { _cancel.Cancel(); foreach (var watcher in _watchers) watcher.Dispose(); _cancel.Dispose(); }
}
