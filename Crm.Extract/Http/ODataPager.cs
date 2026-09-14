using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Crm.Extract.Http;

/// <summary>One page of an OData collection: the verbatim body (evidence) and its records.</summary>
public sealed record ODataPage(int Index, string Body, IReadOnlyList<JsonElement> Records, string? NextLink);

/// <summary>Follows <c>@odata.nextLink</c> to exhaustion (§3.5), refusing a link that repeats.</summary>
public sealed class ODataPager(CrmHttpClient client)
{
    /// <summary>A collection of ~300 at page size 20 is 15 pages; a runaway server loop stops well before this.</summary>
    private const int MaxPages = 100_000;

    public async IAsyncEnumerable<ODataPage> GetPagesAsync(string firstPath, int pageSize, [EnumeratorCancellation] CancellationToken token)
    {
        string? next = firstPath;
        HashSet<string> seen = new(StringComparer.Ordinal);
        int index = 0;
        while (next is not null)
        {
            if (!seen.Add(next))
            {
                throw new InvalidOperationException($"The server returned the same @odata.nextLink twice: '{next}'. Paging aborted.");
            }
            if (index >= MaxPages)
            {
                throw new InvalidOperationException($"Paging exceeded {MaxPages} pages starting from '{firstPath}'.");
            }

            CrmResponse response = await client.GetAsync(next, CrmPreferences.Paged(pageSize), token);
            index++;
            using JsonDocument document = JsonDocument.Parse(response.Body);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Page {index} of '{firstPath}' has no 'value' array.");
            }
            List<JsonElement> records = [.. value.EnumerateArray().Select(record => record.Clone())];
            next = root.TryGetProperty("@odata.nextLink", out JsonElement link) && link.ValueKind == JsonValueKind.String
                ? link.GetString()
                : null;
            yield return new ODataPage(index, response.Body, records, next);
        }
    }
}
