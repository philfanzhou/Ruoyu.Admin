using System.Buffers;
using System.Globalization;
using System.Text;

namespace Admin.WebApi;

/// <summary>
/// The single sanitization entry for user-controlled values before they reach <c>ILogger</c>
/// (#91): every control, format, line-separator, and paragraph-separator rune is replaced with
/// a space — the same normalization the ServiceMantle sink pipeline applies downstream — so a
/// request value can never inject a fake log line even if the sink configuration changes.
/// Everything else, including CJK text, whitespace, and surrogate pairs, passes through
/// unchanged; <see langword="null"/> passes through untouched.
/// </summary>
/// <remarks>
/// This is the data-flow barrier CodeQL's <c>cs/log-forging</c> query is taught to recognize
/// (see <c>.github/codeql/log-models</c>); wrapping a value here is what closes an alert.
/// Secret masking stays with <c>StartupDiagnosticsFormatter</c>, which remains the only exit
/// for startup diagnostics.
/// </remarks>
public static class AdminLogValue
{
    public static string? Sanitize(string? value)
    {
        if (value is null) return null;
        StringBuilder? builder = null;
        for (var index = 0; index < value.Length;)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed);
            var category = status == OperationStatus.Done
                ? Rune.GetUnicodeCategory(rune)
                : UnicodeCategory.Surrogate;
            if (status != OperationStatus.Done
                || category is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
            {
                builder ??= new StringBuilder(value);
                consumed = Math.Max(consumed, 1);
                for (var offset = 0; offset < consumed; offset++) builder[index + offset] = ' ';
            }
            index += Math.Max(consumed, 1);
        }
        return builder?.ToString() ?? value;
    }
}
