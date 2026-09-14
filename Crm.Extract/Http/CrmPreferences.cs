using System.Globalization;

namespace Crm.Extract.Http;

/// <summary>The OData <c>Prefer</c> header options a request carries.</summary>
public sealed record CrmPreferences(int? MaxPageSize, bool IncludeFormattedValues)
{
    public static readonly CrmPreferences None = new(null, false);

    public static CrmPreferences Paged(int pageSize)
    {
        return new CrmPreferences(pageSize, true);
    }

    public string? HeaderValue()
    {
        List<string> parts = [];
        if (MaxPageSize is int size)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"odata.maxpagesize={size}"));
        }
        if (IncludeFormattedValues)
        {
            // The server's own option-set labels, used to check the handout's option-set tables against the live
            // system rather than trusting either (§3.1).
            parts.Add("odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");
        }
        return parts.Count == 0 ? null : string.Join(",", parts);
    }
}
