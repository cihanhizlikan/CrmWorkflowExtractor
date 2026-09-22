using System.Globalization;

namespace Crm.Cli.Reports;

/// <summary>
/// One table on its way to a workbook sheet. Values keep their type — a number stays a number so Excel sorts it as
/// one — and only the yes/no wording is decided here, because a Turkish reader filters on <c>evet</c>, not on TRUE.
/// </summary>
public static class SheetNames
{
    public const string Plan = "Taşıma planı";
    public const string Usage = "Kullanım";
    public const string CallGraph = "Çağrı ağacı";
    public const string Diagrams = "BPMN dizini";
    public const string Families = "Aileler";
    public const string Pairs = "Çiftler";
    public const string Drafts = "Taslaklar";
    public const string Supplied = "Ürünle gelenler";
    public const string DataFootprint = "Veri ayak izi";
    public const string Cascades = "Tetikleme zincirleri";
}

/// <summary>
/// One table on its way to a workbook sheet. Values keep their type — a number stays a number so Excel sorts it as
/// one — and only the yes/no wording is decided here, because a Turkish reader filters on <c>evet</c>, not on TRUE.
/// </summary>
public sealed class Sheet(string name, params string[] headers)
{
    /// <summary>Excel refuses a sheet name over 31 characters or containing <c>[ ] : * ? / \</c>.</summary>
    public const int MaxNameLength = 31;

    private readonly List<IReadOnlyList<object?>> _rows = [];

    public string Name { get; } = name.Length <= MaxNameLength ? name : name[..MaxNameLength];

    public IReadOnlyList<string> Headers { get; } = headers;

    public IReadOnlyList<IReadOnlyList<object?>> Rows
    {
        get { return _rows; }
    }

    public void Row(params object?[] fields)
    {
        _rows.Add(fields);
    }

    /// <summary>The cell as the workbook writes it: a number, or text. Booleans become <c>evet</c> / <c>hayır</c>.</summary>
    public static object? Cell(object? value)
    {
        return value switch
        {
            null => null,
            bool flag => flag ? "evet" : "hayır",
            double number => number,
            int number => number,
            long number => number,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }
}
