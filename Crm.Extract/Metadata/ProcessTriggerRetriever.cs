using System.Text.Json;
using Crm.Extract.Http;

namespace Crm.Extract.Metadata;

/// <summary>
/// Every <c>processtriggers</c> record: what a Business Process Flow runs when a stage is entered or left.
///
/// <para>
/// This is the half of a stage that no workflow record mentions. A stage says which fields it asks for; the
/// workflow it fires on the way in or out is a record of its own, and nothing in this tool had ever read the
/// table — so a diagram of a process could not say what running it actually does.
/// </para>
///
/// <para>
/// WITH EVERY FIELD, and tolerant of the set not being there at all: the entity exists in 8.2 but its columns
/// differ between versions, and a name guessed here would only be found wrong after a production export. The rows
/// are kept verbatim in <c>ham/</c> and read once their real shape has been seen. An organisation with no
/// business process flows has nothing in this table, which is not a failure.
/// </para>
/// </summary>
public sealed class ProcessTriggerRetriever(CrmHttpClient client, int pageSize)
{
    public const string IndexFile = Runs.RunPaths.Raw + "/surec-tetikleyicileri.json";

    /// <summary>The rows as CRM gave them, detached from the documents they were read out of.</summary>
    public async Task<IReadOnlyList<JsonElement>> RetrieveAsync(CancellationToken token)
    {
        ODataPager pager = new(client);
        List<JsonElement> rows = [];
        await foreach (ODataPage page in pager.GetPagesAsync("processtriggers", pageSize, token))
        {
            rows.AddRange(page.Records.Select(record => record.Clone()));
        }
        return rows;
    }
}
