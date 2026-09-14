using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Crm.Extract.Http;

/// <summary>
/// The ONLY type in the application that touches the network (§2.3). It can issue GET and nothing else, and only
/// beneath the configured organization's Web API root.
///
/// <para>
/// The guarantee is structural in three layers: <c>BannedSymbols.txt</c> makes constructing an <see cref="HttpClient"/>,
/// sending a request or naming a non-GET verb a build error everywhere except the two suppressed sites below; the
/// public surface takes a path, never a verb or a request; and <see cref="EnsureReadOnly"/> rejects any request that
/// is not GET immediately before it is sent.
/// </para>
/// </summary>
public sealed class CrmHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly RetryPolicy _retry;
    private readonly ILogger _logger;

    internal CrmHttpClient(CrmConnectionOptions options, HttpMessageHandler handler, RetryPolicy retry, ILogger logger)
    {
        _baseUri = options.WebApiBaseUri();
        _retry = retry;
        _logger = logger;
#pragma warning disable RS0030 // The one sanctioned HttpClient in the application; see the type's summary.
        _http = new HttpClient(handler, disposeHandler: true)
#pragma warning restore RS0030
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds))
        };
    }

    public Uri BaseUri
    {
        get { return _baseUri; }
    }

    /// <summary>Called with every final response (after retries), success or not — how raw/ keeps verbatim API payloads.</summary>
    public Action<CrmResponse>? ResponseObserver { get; set; }

    /// <summary>
    /// Builds the client with Windows credentials. Redirects are NOT followed automatically: a redirect is how an
    /// internet-facing deployment announces itself, and following it would hide exactly that evidence.
    /// </summary>
    public static CrmHttpClient Create(CrmConnectionOptions options, string? password, ILogger logger)
    {
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = false,
            PreAuthenticate = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        if (options.Authentication == CrmAuthenticationMode.Default)
        {
            handler.UseDefaultCredentials = true;
        }
        else
        {
            handler.Credentials = new NetworkCredential(options.UserName, password, options.Domain);
        }
        return new CrmHttpClient(options, handler, RetryPolicy.Standard(options.MaxAttempts, logger), logger);
    }

    /// <summary>
    /// GETs a path relative to the Web API root (or an absolute <c>@odata.nextLink</c> beneath it) and returns the
    /// fully-read response. Throws for any non-success outcome, classified: IFD, rejected credentials, or other.
    /// </summary>
    public async Task<CrmResponse> GetAsync(string pathAndQuery, CrmPreferences preferences, CancellationToken token)
    {
        Uri target = Resolve(pathAndQuery);
        CrmResponse response = await _retry.ExecuteAsync(attemptToken => SendGetAsync(target, preferences, attemptToken), token);
        ResponseObserver?.Invoke(response);
        if (response.IsSuccess)
        {
            _logger.LogDebug("GET {Uri} -> {Status}", target, (int)response.StatusCode);
            return response;
        }

        (CrmDeploymentKind kind, string evidence) = DeploymentClassifier.Classify(response);
        if (kind == CrmDeploymentKind.InternetFacing)
        {
            throw new CrmInternetFacingDeploymentException(response, evidence);
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized && kind == CrmDeploymentKind.OnPremises)
        {
            throw new CrmAuthenticationException(response);
        }
        if ((int)response.StatusCode is >= 300 and <= 399)
        {
            throw new CrmInternetFacingDeploymentException(response, evidence + " — an unexplained redirect is treated as a stop, not followed");
        }
        throw new CrmRequestException(response);
    }

    /// <summary>
    /// Resolves a relative path beneath the root, or accepts an absolute URI only when it is beneath the same root.
    /// A server-supplied <c>nextLink</c> pointing anywhere else is refused rather than followed with our credentials.
    /// </summary>
    internal Uri Resolve(string pathAndQuery)
    {
        if (string.IsNullOrWhiteSpace(pathAndQuery))
        {
            throw new CrmBoundaryViolationException("An empty path is not a Web API resource.");
        }
        if (Uri.TryCreate(pathAndQuery, UriKind.Absolute, out Uri? absolute) && absolute.IsAbsoluteUri
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            bool sameAuthority = string.Equals(absolute.Scheme, _baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(absolute.Authority, _baseUri.Authority, StringComparison.OrdinalIgnoreCase);
            bool beneathRoot = absolute.AbsolutePath.StartsWith(_baseUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            if (!sameAuthority || !beneathRoot)
            {
                throw new CrmBoundaryViolationException($"Refusing to request '{absolute}': it is outside the organization root '{_baseUri}'.");
            }
            return absolute;
        }
        if (pathAndQuery.StartsWith('/') || pathAndQuery.Contains("..", StringComparison.Ordinal) || pathAndQuery.Contains("://", StringComparison.Ordinal))
        {
            throw new CrmBoundaryViolationException($"Refusing path '{pathAndQuery}': relative paths must stay beneath the organization root.");
        }
        return new Uri(_baseUri, pathAndQuery);
    }

    /// <summary>The last check before anything reaches the wire. Internal so a test can prove it rejects every other verb.</summary>
    internal static void EnsureReadOnly(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get)
        {
            throw new CrmBoundaryViolationException($"Refusing {request.Method} {request.RequestUri}: this application is read-only against CRM.");
        }
    }

    private async Task<CrmResponse> SendGetAsync(Uri target, CrmPreferences preferences, CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, target);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        string? prefer = preferences.HeaderValue();
        if (prefer is not null)
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }

        EnsureReadOnly(request);
#pragma warning disable RS0030 // The one sanctioned send; EnsureReadOnly on the line above is its guard.
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
#pragma warning restore RS0030
        return await CrmResponse.ReadAsync(target, response, token);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
