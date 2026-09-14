using System.Net;
using System.Text;

namespace Crm.Tests.Fakes;

/// <summary>A request as the fake server saw it: the verb, the URI and the Prefer header.</summary>
internal sealed record SeenRequest(HttpMethod Method, Uri Uri, string? Prefer);

/// <summary>
/// An in-memory stand-in for the Web API, answering by path prefix beneath the organization root. Its payload shapes
/// are SYNTHETIC — written from the Web API documentation, never recorded from a real server — so a test passing
/// here proves the tool's logic, not the server's behaviour.
/// </summary>
internal sealed class FakeCrmServer : HttpMessageHandler
{
    public const string BaseUrl = "https://crm.test/Org/api/data/v8.2/";

    private readonly List<(string Prefix, Func<Uri, HttpResponseMessage> Respond)> _routes = [];

    public List<SeenRequest> Requests { get; } = [];

    public FakeCrmServer On(string pathPrefix, Func<Uri, HttpResponseMessage> respond)
    {
        _routes.Add((pathPrefix, respond));
        return this;
    }

    public FakeCrmServer OnJson(string pathPrefix, string body)
    {
        return On(pathPrefix, _ => Json(body));
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    public static HttpResponseMessage Text(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri ?? throw new InvalidOperationException("Request without a URI.");
        string? prefer = request.Headers.TryGetValues("Prefer", out IEnumerable<string>? values) ? string.Join(",", values) : null;
        Requests.Add(new SeenRequest(request.Method, uri, prefer));

        string relative = Uri.UnescapeDataString(uri.PathAndQuery)[new Uri(BaseUrl).AbsolutePath.Length..];
        (string Prefix, Func<Uri, HttpResponseMessage> Respond) route = _routes
            .Where(candidate => relative.StartsWith(candidate.Prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Prefix.Length)
            .FirstOrDefault();
        HttpResponseMessage response = route.Respond is null
            ? Json($"{{\"error\":{{\"message\":\"No fake route for '{relative}'\"}}}}", HttpStatusCode.NotFound)
            : route.Respond(uri);
        return Task.FromResult(response);
    }
}
