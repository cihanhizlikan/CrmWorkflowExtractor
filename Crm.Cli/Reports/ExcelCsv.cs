using System.Globalization;
using System.Text;

namespace Crm.Cli.Reports;

/// <summary>
/// CSV the Business Architects can double-click open in Turkish-locale Excel: <c>;</c> between fields (tr-TR's list
/// separator — a comma file opens as one column), <c>,</c> as the decimal mark, and a UTF-8 byte-order mark so
/// Turkish characters survive. The machine-readable twin of every CSV is its JSON file.
/// </summary>
public sealed class ExcelCsv
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly StringBuilder _text = new();

    public ExcelCsv(params string[] headers)
    {
        Row(headers);
    }

    public void Row(params object?[] fields)
    {
        _text.Append(string.Join(";", fields.Select(Field))).Append("\r\n");
    }

    public byte[] ToBytes()
    {
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(_text.ToString())];
    }

    private static string Field(object? value)
    {
        string text = value switch
        {
            null => "",
            double number => number.ToString("0.####", Turkish),
            bool flag => flag ? "yes" : "no",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        bool quote = text.Contains(';', StringComparison.Ordinal) || text.Contains('"', StringComparison.Ordinal)
            || text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal);
        return quote ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }
}
