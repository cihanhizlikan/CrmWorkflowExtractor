using System.Text.Json;
using Crm.Extract.Http;

namespace Crm.Extract.Metadata;

/// <summary>
/// Every record of Pensionsoft's stage entity, <c>ps_step</c>: the machine a case moves through.
///
/// <para>
/// Cases in this estate do not move through a Business Process Flow. Each stage is a record naming the stage that
/// follows on success, on failure and on cancellation, and the workflow fired on each — so the order a request is
/// handled in, and which workflows run along the way, is DATA, held nowhere in any workflow's XAML. A plan built from
/// the workflows alone could not say what a request goes through (chief analyst, 2026-10-09).
/// </para>
///
/// <para>
/// WITH EVERY FIELD, and with formatted values, so each lookup and option arrives with the label a reader knows it
/// by. The SLA, document, SMS and assignment rules on a stage are the stage's logic as much as its transitions are.
/// An organisation without the entity simply has nothing to read.
/// </para>
/// </summary>
public sealed class CaseStageRetriever(CrmHttpClient client, int pageSize)
{
    public const string EntitySet = "ps_steps";

    public const string IndexFile = Runs.RunPaths.Raw + "/talep-asamalari.json";

    /// <summary>The rows as CRM gave them, detached from the documents they were read out of.</summary>
    public async Task<IReadOnlyList<JsonElement>> RetrieveAsync(CancellationToken token)
    {
        ODataPager pager = new(client);
        List<JsonElement> rows = [];
        await foreach (ODataPage page in pager.GetPagesAsync(EntitySet, pageSize, token))
        {
            rows.AddRange(page.Records.Select(record => record.Clone()));
        }
        return rows;
    }
}
