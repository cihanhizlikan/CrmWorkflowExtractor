using System.Text.Json;
using Crm.Extract.Runs;

namespace Crm.Extract.Xaml;

/// <summary>
/// One XAML file in <c>raw/xaml/</c>: which record it belongs to, the version it was taken at, its hash, and whether
/// this run fetched it or copied it from an earlier sealed run. Written as <c>raw/xaml/index.json</c>.
/// </summary>
public sealed record XamlEntry(Guid WorkflowId, string Kind, long? VersionNumber, string File, long Bytes, string Sha256, string Source)
{
    public const string IndexFile = "raw/xaml/index.json";
    public const string KindDefinition = "definition";
    public const string KindActivation = "activation";
    public const string SourceFetched = "fetched";

    public static string FileFor(Guid workflowId)
    {
        return $"raw/xaml/{workflowId:D}.xaml";
    }

    public static IReadOnlyList<XamlEntry> ReadIndex(string runRoot)
    {
        string path = Path.Combine(runRoot, IndexFile.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(path))
        {
            return [];
        }
        return JsonSerializer.Deserialize<List<XamlEntry>>(System.IO.File.ReadAllText(path), RunFolder.JsonOptions) ?? [];
    }
}
