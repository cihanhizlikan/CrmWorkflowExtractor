using System.Globalization;

namespace Crm.Cli.Reports;

/// <summary>
/// One table on its way to a workbook sheet. Values keep their type — a number stays a number so Excel sorts it as
/// one — and only the yes/no wording is decided here, because a Turkish reader filters on <c>evet</c>, not on TRUE.
/// </summary>
public static class SheetNames
{
    /// <summary>One row per stage map: which request subjects there are, how big each process is, which map to open.</summary>
    public const string Topics = "Talep konuları";

    /// <summary>
    /// The stages of the requests. "Talep" in front, so they cannot be mistaken for a business process flow's stages
    /// and so the sheet's name matches its folder, <c>bpmn/talep-asamalari/</c>.
    /// </summary>
    public const string Stages = "Talep aşamaları";

    /// <summary>The workflows. It was "Taşıma planı", which said nothing of what a row is.</summary>
    public const string Plan = "İş akışları";
    public const string Excluded = "Kapsam dışı";
    public const string DataFootprint = "Veri ayak izi";
    public const string ExternalDependencies = "Dış bağımlılıklar";
    public const string Addresses = "Adresler";
    public const string Plugins = "Eklentiler";
    public const string Guide = "Nasıl okunur";
    public const string Trees = "İş akışı ağaçları";
    public const string Cascades = "Tetikleme zincirleri";
    public const string RunRoles = "Çalıştırma yetkisi";
    public const string RunAuthority = "Kim çalıştırabilir";
    public const string StageEntries = "Aşama girişleri";
    public const string Steps = "Geçişler";
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

    /// <summary>
    /// Several names in one cell. Capped, because nobody reads the four hundredth name in a cell and Excel will
    /// not hold more than 32,767 characters in one — a longer cell makes it "repair" the file on open, which means
    /// throwing the content away. The count that belongs beside such a list is its own column.
    /// </summary>
    public static string List(IEnumerable<string> values, int most = 40)
    {
        List<string> all = [.. values];
        return all.Count <= most
            ? string.Join(" | ", all)
            : string.Join(" | ", all.Take(most)) + string.Create(CultureInfo.InvariantCulture, $" | …ve {all.Count - most} tane daha");
    }

    /// <summary>
    /// One row, which must have exactly one value per column.
    ///
    /// <para>
    /// It used to take any number. On 2026-10-02 three columns were removed from the plan's header and their three
    /// values were left in the row, and every column of the delivered plan after <c>rol</c> sat two to the right of
    /// its name. Nothing failed: the values were mostly empty, the writer leaves an empty cell out, and the test
    /// reader of the day counted cells instead of reading their references, which shifted them back. A row that
    /// does not fit its header is a programming error, and it now stops the run where it happens rather than reaching
    /// an analyst's desk as a sheet that reads plausibly and means something else.
    /// </para>
    /// </summary>
    public void Row(params object?[] fields)
    {
        if (fields.Length != Headers.Count)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"'{Name}': a row of {fields.Length} values under {Headers.Count} columns."));
        }
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
