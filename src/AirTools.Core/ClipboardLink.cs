namespace AirTools.Core;

public static class ClipboardLink
{
    public static bool TryParse(string? text, out Uri uri)
    {
        uri = null!;
        var value = text?.Trim();
        return !string.IsNullOrEmpty(value) && value.Contains("://", StringComparison.Ordinal)
            && !value.Any(char.IsWhiteSpace) && !value.Contains('\\')
            && Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.Scheme.Length > 1;
    }
}
