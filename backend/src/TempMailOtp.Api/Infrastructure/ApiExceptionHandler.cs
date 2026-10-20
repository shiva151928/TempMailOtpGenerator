using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TempMailOtp.Api.Providers;
using TempMailOtp.Api.Services.Inbox;

namespace TempMailOtp.Api.Infrastructure;

/// <summary>
/// Maps domain and provider exceptions to RFC 7807 problem details with a stable "code" extension
/// the frontend can switch on. Unexpected errors return a generic 500 without internal details.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing useful to send.
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var (status, title, code) = exception switch
        {
            InboxTokenMissingException => (StatusCodes.Status401Unauthorized, "Inbox token required", "inbox_token_missing"),
            InboxNotFoundException => (StatusCodes.Status404NotFound, "Inbox not found", "inbox_not_found"),
            InboxExpiredException => (StatusCodes.Status410Gone, "Inbox expired", "inbox_expired"),
            MessageNotFoundException => (StatusCodes.Status404NotFound, "Message not found", "message_not_found"),
            DemoModeRequiredException => (StatusCodes.Status404NotFound, "Not available", "demo_mode_required"),
            EmailProviderException { Kind: EmailProviderErrorKind.RateLimited } =>
                (StatusCodes.Status503ServiceUnavailable, "Email provider is busy", "provider_rate_limited"),
            EmailProviderException { Kind: EmailProviderErrorKind.CredentialsUnavailable } =>
                (StatusCodes.Status502BadGateway, "Inbox credentials unavailable", "provider_credentials_unavailable"),
            EmailProviderException { Kind: EmailProviderErrorKind.MailboxNotFound } =>
                (StatusCodes.Status502BadGateway, "Mailbox missing at the provider", "provider_mailbox_not_found"),
            EmailProviderException =>
                (StatusCodes.Status502BadGateway, "Email provider error", "provider_unavailable"),
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Bad request", "bad_request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "internal_error"),
        };

        if (status >= 500 && exception is not EmailProviderException)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // Domain and provider messages are written for users; anything else stays generic.
            Detail = status == StatusCodes.Status500InternalServerError
                ? "Something went wrong on the server. Try again."
                : exception.Message,
        };
        problem.Extensions["code"] = code;

        if (exception is InboxExpiredException expired)
        {
            problem.Extensions["expiresAt"] = expired.ExpiresAt;
        }

        if (exception is EmailProviderException { RetryAfter: { } retryAfter })
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            problem.Extensions["retryAfterSeconds"] = seconds;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
