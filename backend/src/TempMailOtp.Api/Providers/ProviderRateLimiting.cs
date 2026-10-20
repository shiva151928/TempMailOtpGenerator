using System.Threading.RateLimiting;

namespace TempMailOtp.Api.Providers;

/// <summary>
/// Process-wide token bucket that keeps outbound provider traffic under the provider's limit.
/// </summary>
public sealed class ProviderRequestLimiter : IDisposable
{
    public ProviderRequestLimiter(int requestsPerSecond)
    {
        var permits = Math.Clamp(requestsPerSecond, 1, 50);
        Limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = permits,
            TokensPerPeriod = permits,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 200,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    }

    public RateLimiter Limiter { get; }

    public void Dispose() => Limiter.Dispose();
}

/// <summary>HttpClient handler that waits for a permit before each provider request.</summary>
public sealed class ProviderRateLimitingHandler(ProviderRequestLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await limiter.Limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            throw new EmailProviderException(
                "Too many requests are waiting for the email provider. Try again in a few seconds.",
                EmailProviderErrorKind.RateLimited)
            {
                RetryAfter = TimeSpan.FromSeconds(2),
            };
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
