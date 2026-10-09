using System.Text.Json;
using Crm.Extract.Http;

namespace Crm.Extract.Metadata;

/// <summary>A Business Process Flow stage (§3.4).</summary>
public sealed record ProcessStage(Guid StageId, Guid? ProcessId, string Name, int? Category, string? PrimaryEntity);

/// <summary>
/// Every <c>processstages</c> record, paged, WITH EVERY FIELD IT HAS.
///
/// <para>
/// It used to ask for five columns and throw the rest away, which was enough to name a stage and nothing else.
/// Dynamics works in stages, and a plan built from individual workflows does not describe the process they belong
/// to (chief analyst, 2026-10-09) — so the steps inside a stage are now wanted too, and in 8.2 those live in
/// <c>clientdata</c>. Rather than name the fields we think we need and find out after a twenty-minute production
/// export that we named them wrong, the <c>$select</c> is gone and the rows are kept verbatim in <c>ham/</c>. The
/// typed record stays as the index; what reads <c>clientdata</c> is written once its real shape has been seen.
/// </para>
/// </summary>
public sealed class ProcessStageRetriever(CrmHttpClient client, int pageSize)
{
    public const string IndexFile = Runs.RunPaths.Raw + "/surec-asamalari.json";

    /// <summary>The rows as CRM gave them, detached from the documents they were read out of.</summary>
    public async Task<IReadOnlyList<JsonElement>> RetrieveAsync(CancellationToken token)
    {
        ODataPager pager = new(client);
        List<JsonElement> rows = [];
        await foreach (ODataPage page in pager.GetPagesAsync("processstages", pageSize, token))
        {
            rows.AddRange(page.Records.Select(record => record.Clone()));
        }
        return rows;
    }

    /// <summary>The index these rows make: one named stage per row, in a stable order.</summary>
    public static IReadOnlyList<ProcessStage> Index(IEnumerable<JsonElement> rows)
    {
        return [.. rows.Select(Parse).OfType<ProcessStage>().OrderBy(stage => stage.ProcessId).ThenBy(stage => stage.StageId)];
    }

    /// <summary>One <c>processstages</c> record, or null when it has no id.</summary>
    public static ProcessStage? Parse(JsonElement row)
    {
        return Json.OptionalGuid(row, "processstageid") is Guid id
            ? new ProcessStage(id, Json.OptionalGuid(row, "_processid_value"), Json.OptionalString(row, "stagename") ?? "",
                Json.OptionalInt(row, "stagecategory"), Json.OptionalString(row, "primaryentitytypecode"))
            : null;
    }
}
