using System;

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
