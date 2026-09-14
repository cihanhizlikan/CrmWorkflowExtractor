using System.Globalization;
using Crm.Extract.Http;
using Microsoft.Extensions.Logging;

namespace Crm.Extract.Inventory;

public sealed record InventoryPass(int ApiCount, IReadOnlyList<WorkflowInventoryRecord> Records);

/// <summary>
/// First pass of §3.5: <c>$count</c>, then every workflow without <c>xaml</c>, paged. The verbatim page bodies are
/// kept by the caller through <see cref="CrmHttpClient.ResponseObserver"/>, so this type holds parsed records only.
/// </summary>
public sealed class WorkflowInventoryRetriever(CrmHttpClient client, int pageSize, ILogger logger)
{
    public static async Task<int> CountAsync(CrmHttpClient crm, CancellationToken token)
    {
        CrmResponse response = await crm.GetAsync("workflows/$count", CrmPreferences.None, token);
        string text = response.Body.Trim().Trim('\uFEFF');
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            throw new InvalidDataException($"workflows/$count returned a body that is not a number: '{text}'.");
        }
        return count;
    }

    public async Task<InventoryPass> RetrieveAsync(IReadOnlyList<string> columns, CancellationToken token)
    {
        if (!columns.Contains("workflowid", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The inventory cannot run without workflowid.");
        }

        int apiCount = await CountAsync(client, token);
        logger.LogInformation("workflows/$count = {Count}", apiCount);

        // No $filter: every category and type is fetched and classified here, so the count covers everything (plan §3.3).
        // $orderby on the key keeps page boundaries stable while paging.
        string firstPath = "workflows?$select=" + string.Join(",", columns) + "&$orderby=workflowid";
        ODataPager pager = new(client);
        List<WorkflowInventoryRecord> records = [];
        await foreach (ODataPage page in pager.GetPagesAsync(firstPath, pageSize, token))
        {
            records.AddRange(page.Records.Select(WorkflowInventoryRecord.Parse));
            logger.LogInformation("Inventory page {Page}: {Records} record(s), {Total} so far", page.Index, page.Records.Count, records.Count);
        }
        return new InventoryPass(apiCount, records);
    }
}
