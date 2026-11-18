using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AirTools.Tools.Clipboard.Services;

/// <summary>
/// Shelf of recent texts. Newest first; pinned entries sit at the front (nearest the button) and are never
/// evicted by new copies. Preview limits never change the original text.
/// </summary>
public sealed class RecentTextRack
{
    private readonly ObservableCollection<RackEntry> _items = new();
    private int _capacity = 5;
    public ReadOnlyObservableCollection<RackEntry> Items { get; }
    /// <summary>Raised when a pin is added, removed or lost, so the host can persist pins and views can redraw.</summary>
    public event Action? PinsChanged;
    public RecentTextRack() => Items = new(_items);
    public int Capacity
    {
        get => _capacity;
        set { _capacity = Math.Clamp(value, 3, 5); Trim(); }
    }
    public int PinnedCount => _items.Count(x => x.Pinned);
    public IReadOnlyList<string> PinnedTexts => _items.Where(x => x.Pinned).Select(x => x.Text).ToArray();

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        int firstFree = PinnedCount;
        var existing = _items.FirstOrDefault(x => x.Text == text);
        if (existing != null)
        {
            // A pinned entry keeps its place; an unpinned one moves to the front of the unpinned part.
            int index = _items.IndexOf(existing);
            if (!existing.Pinned && index > firstFree) _items.Move(index, firstFree);
            return;
        }
        // Only reachable after the capacity was lowered below the pin count: the last pin gives way.
        if (_items.Count >= _capacity && _items.All(x => x.Pinned)) { _items.Remove(_items[^1]); PinsChanged?.Invoke(); }
        _items.Insert(Math.Min(firstFree, _items.Count), new RackEntry(Guid.NewGuid(), text));
        Trim();
    }

    /// <summary>Pins or unpins an entry. Refuses when no free slot would remain for new copies.</summary>
    public bool SetPinned(Guid id, bool pinned)
    {
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item == null) return false;
        if (item.Pinned == pinned) return true;
        if (pinned && PinnedCount + 1 >= _capacity) return false;
        int from = _items.IndexOf(item);
        item.Pinned = pinned;
        // Pinned entries gather at the front in the order they were pinned; an unpinned one goes right behind them.
        int to = pinned ? PinnedCount - 1 : PinnedCount;
        if (from != to) _items.Move(from, Math.Clamp(to, 0, _items.Count - 1));
        PinsChanged?.Invoke();
        return true;
    }

    /// <summary>Restores pinned texts saved by a previous session (first saved stays first).</summary>
    public void LoadPinned(IEnumerable<string> texts)
    {
        var wanted = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(Math.Max(0, _capacity - 1)).ToList();
        for (int i = wanted.Count - 1; i >= 0; i--) _items.Insert(0, new RackEntry(Guid.NewGuid(), wanted[i]) { Pinned = true });
    }

    public void Remove(Guid id)
    {
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item == null) return;
        _items.Remove(item);
        if (item.Pinned) PinsChanged?.Invoke();
    }

    private void Trim()
    {
        while (_items.Count > _capacity)
        {
            var last = _items.LastOrDefault(x => !x.Pinned);
            if (last == null) { last = _items[^1]; PinsChanged?.Invoke(); } // capacity shrank below the pin count
            _items.Remove(last);
        }
    }
}

public sealed record RackEntry(Guid Id, string Text)
{
    public bool Pinned { get; set; }
    public string Preview => Text.Length > 180 ? Text[..180] + "…" : Text;
    public bool IsLong => Text.Length > 80 || Text.Count(c => c == '\n') > 3;
}
