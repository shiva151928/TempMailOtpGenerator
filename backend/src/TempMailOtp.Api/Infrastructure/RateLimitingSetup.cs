using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TempMailOtp.Api.Configuration;

namespace TempMailOtp.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string Generate = "generate";
    public const string Refresh = "refresh";
}

public static class RateLimitingSetup
{
    /// <summary>
    /// Per-IP fixed-window limits: a global limit for all API calls plus stricter limits for
    /// inbox generation and manual refresh. Behind a reverse proxy, enable forwarded headers so
    /// the client IP is used instead of the proxy's.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, ApiRateLimitOptions settings)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => Window(settings.GlobalPermitLimit, settings.GlobalWindowSeconds)));

            options.AddPolicy(RateLimitPolicies.Generate, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => Window(settings.GeneratePermitLimit, settings.GenerateWindowSeconds)));

            options.AddPolicy(RateLimitPolicies.Refresh, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => Window(settings.RefreshPermitLimit, settings.RefreshWindowSeconds)));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfterSeconds = 10;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                }

                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = $"You're sending requests too quickly. Try again in {retryAfterSeconds} seconds.",
                };
                problem.Extensions["code"] = "rate_limited";
                problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;

                var problemDetailsService = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = problem,
                });
            };
        });

        return services;
    }

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";

    private static FixedWindowRateLimiterOptions Window(int permitLimit, int windowSeconds) => new()
    {
        PermitLimit = Math.Max(1, permitLimit),
        Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds)),
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
