using System.Text.Json;
using Crm.Extract.Http;

namespace Crm.Extract.Metadata;

/// <summary>A Business Process Flow stage (§3.4).</summary>
public sealed record ProcessStage(Guid StageId, Guid? ProcessId, string Name, int? Category, string? PrimaryEntity);

/// <summary>Retrieves every <c>processstages</c> record, paged. Used to name BPF stages in BPMN.</summary>
public sealed class ProcessStageRetriever(CrmHttpClient client, int pageSize)
{
    public const string IndexFile = "raw/processstages.json";

    public async Task<IReadOnlyList<ProcessStage>> RetrieveAsync(CancellationToken token)
    {
        ODataPager pager = new(client);
        List<ProcessStage> stages = [];
        await foreach (ODataPage page in pager.GetPagesAsync("processstages?$select=processstageid,stagename,stagecategory,_processid_value,primaryentitytypecode", pageSize, token))
        {
            stages.AddRange(page.Records.Select(Parse).OfType<ProcessStage>());
        }
        return [.. stages.OrderBy(stage => stage.ProcessId).ThenBy(stage => stage.StageId)];
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
