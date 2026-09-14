using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Crm.Extract.Http;

/// <summary>
/// Bounded retry with jittered exponential backoff for transient faults against a production server (§10).
/// Hand-rolled rather than a resilience package: this is the only place it is needed, and it is short enough to
/// read in one sitting.
/// </summary>
public sealed class RetryPolicy(int maxAttempts, TimeSpan baseDelay, Func<TimeSpan, CancellationToken, Task> delay, Func<double> jitter, ILogger logger)
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(120);

    private readonly int _maxAttempts = Math.Max(1, maxAttempts);

    public static RetryPolicy Standard(int maxAttempts, ILogger logger)
    {
        return new RetryPolicy(maxAttempts, TimeSpan.FromSeconds(2), Task.Delay, Random.Shared.NextDouble, logger);
    }

    /// <summary>
    /// 408, 429 and 5xx except 501. CRM reports SQL timeouts and deadlocks as 500, so 500 is retried — bounded,
    /// so a deterministic 500 costs a few backed-off seconds, not a hang.
    /// </summary>
    public static bool IsTransient(HttpStatusCode status)
    {
        int code = (int)status;
        return status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (code is >= 500 and <= 599 && status != HttpStatusCode.NotImplemented);
    }

    public async Task<CrmResponse> ExecuteAsync(Func<CancellationToken, Task<CrmResponse>> send, CancellationToken token)
    {
        for (int attempt = 1; ; attempt++)
        {
            CrmResponse response;
            try
            {
                response = await send(token);
            }
            catch (Exception error) when (attempt < _maxAttempts && IsTransientFault(error, token))
            {
                TimeSpan wait = Backoff(attempt);
                logger.LogWarning("Attempt {Attempt}/{Max} failed with {Fault}; retrying in {Wait}.",
                    attempt, _maxAttempts, error.GetType().Name, wait.ToString("c", CultureInfo.InvariantCulture));
                await delay(wait, token);
                continue;
            }

            if (!IsTransient(response.StatusCode) || attempt >= _maxAttempts)
            {
                return response;
            }
            TimeSpan pause = response.RetryAfter is TimeSpan retryAfter && retryAfter > TimeSpan.Zero
                ? (retryAfter < MaxRetryAfter ? retryAfter : MaxRetryAfter)
                : Backoff(attempt);
            logger.LogWarning("Attempt {Attempt}/{Max} for {Uri} returned {Status}; retrying in {Wait}.",
                attempt, _maxAttempts, response.RequestUri, (int)response.StatusCode, pause.ToString("c", CultureInfo.InvariantCulture));
            await delay(pause, token);
        }
    }

    private static bool IsTransientFault(Exception error, CancellationToken token)
    {
        // A TaskCanceledException that is not the caller's own cancellation is HttpClient's timeout.
        return error is HttpRequestException or IOException
            || (error is TaskCanceledException && !token.IsCancellationRequested);
    }

    private TimeSpan Backoff(int attempt)
    {
        double exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        double jittered = exponential * (0.5 + (jitter() * 0.5));
        return TimeSpan.FromMilliseconds(Math.Min(jittered, MaxBackoff.TotalMilliseconds));
    }
}
