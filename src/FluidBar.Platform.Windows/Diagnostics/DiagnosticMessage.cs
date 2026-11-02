using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Windows.Diagnostics;

/// <summary>Disabled traces skip interpolation. Text/object values are always redacted.</summary>
[InterpolatedStringHandler]
public ref struct DiagnosticMessage
{
    private readonly StringBuilder? _text;
    public bool IsEnabled => _text is not null;
    public DiagnosticMessage(int literalLength, int formattedCount, out bool shouldAppend)
    {
        shouldAppend = MediaDiagnostics.Enabled;
        _text = shouldAppend ? new StringBuilder(Math.Min(2048, literalLength + formattedCount * 12)) : null;
    }
    internal DiagnosticMessage(int literalLength, int formattedCount, DiagnosticTrace receiver, out bool shouldAppend)
    {
        shouldAppend = receiver.Enabled;
        _text = shouldAppend ? new StringBuilder(Math.Min(2048, literalLength + formattedCount * 12)) : null;
    }
    public void AppendLiteral(string text) => AppendBounded(text);
    public void AppendFormatted<T>(T value) => AppendFormatted(value, 0, null);
    public void AppendFormatted<T>(T value, string? format) => AppendFormatted(value, 0, format);
    public void AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, null);
    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
        if (_text is null) return;
        string safe;
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            or bool or Enum or TimeSpan or DateTime or DateTimeOffset)
        {
            try { safe = value is IFormattable number ? number.ToString(format, CultureInfo.InvariantCulture) : value.ToString()!; }
            catch (FormatException) { safe = "<unavailable>"; }
        }
        else safe = "<redacted>";
        var padding = (int)Math.Clamp(Math.Abs((long)alignment) - safe.Length, 0, 128);
        if (alignment > 0) AppendBounded(new string(' ', padding));
        AppendBounded(safe);
        if (alignment < 0) AppendBounded(new string(' ', padding));
    }
    private void AppendBounded(string value)
    {
        if (_text is null) return;
        var count = Math.Min(value.Length, 2048 - _text.Length);
        if (count > 0 && char.IsHighSurrogate(value[count - 1])) count--;
        _text.Append(value.AsSpan(0, count));
    }
    internal string GetText() => _text?.ToString() ?? string.Empty;
}
