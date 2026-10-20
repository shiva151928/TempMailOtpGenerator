using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TempMailOtp.Api.Contracts;
using TempMailOtp.Api.Infrastructure;
using TempMailOtp.Api.Services.Inbox;

namespace TempMailOtp.Api.Controllers;

/// <summary>
/// Temporary inbox API. Every inbox-specific endpoint requires the access token returned by
/// POST /api/temp-mail/generate in the X-Inbox-Token header.
/// </summary>
[ApiController]
[Route("api/temp-mail")]
[Produces("application/json")]
public sealed class TempMailController(ITempMailService service) : ControllerBase
{
    public const string InboxTokenHeader = "X-Inbox-Token";

    /// <summary>Returns the configured email provider (and whether it is demo mode) plus client settings.</summary>
    [HttpGet("provider")]
    [ProducesResponseType<ProviderInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<ProviderInfoResponse> GetProvider() => Ok(service.GetProviderInfo());

    /// <summary>Creates a new temporary inbox at the configured provider.</summary>
    [HttpPost("generate")]
    [EnableRateLimiting(RateLimitPolicies.Generate)]
    [ProducesResponseType<GenerateInboxResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GenerateInboxResponse>> Generate(CancellationToken cancellationToken)
    {
        var result = await service.GenerateAsync(cancellationToken);
        return CreatedAtAction(nameof(GetStatus), new { inboxId = result.InboxId }, result);
    }

    /// <summary>Lists messages, newest first. Syncs with the provider when the last sync is stale.</summary>
    [HttpGet("{inboxId}/messages")]
    [ProducesResponseType<InboxMessagesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public Task<InboxMessagesResponse> GetMessages(
        string inboxId,
        [FromHeader(Name = InboxTokenHeader)] string? inboxToken,
        CancellationToken cancellationToken) =>
        service.GetMessagesAsync(inboxId, inboxToken, cancellationToken);

    /// <summary>Returns one message with sanitized HTML and plain text.</summary>
    [HttpGet("{inboxId}/messages/{messageId:guid}")]
    [ProducesResponseType<MessageDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public Task<MessageDetailDto> GetMessage(
        string inboxId,
        Guid messageId,
        [FromHeader(Name = InboxTokenHeader)] string? inboxToken,
        CancellationToken cancellationToken) =>
        service.GetMessageAsync(inboxId, inboxToken, messageId, cancellationToken);

    /// <summary>Returns the latest detected OTP and the OTP history for the inbox.</summary>
    [HttpGet("{inboxId}/otp")]
    [ProducesResponseType<OtpResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public Task<OtpResponse> GetOtp(
        string inboxId,
        [FromHeader(Name = InboxTokenHeader)] string? inboxToken,
        CancellationToken cancellationToken) =>
        service.GetOtpsAsync(inboxId, inboxToken, cancellationToken);

    /// <summary>Forces a sync with the provider. Returns 502/503 if the provider fails.</summary>
    [HttpPost("{inboxId}/refresh")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [ProducesResponseType<InboxMessagesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<InboxMessagesResponse> Refresh(
        string inboxId,
        [FromHeader(Name = InboxTokenHeader)] string? inboxToken,
        CancellationToken cancellationToken) =>
        service.RefreshAsync(inboxId, inboxToken, cancellationToken);

    /// <summary>Deletes the inbox at the provider and all of its stored data.</summary>
    [HttpDelete("{inboxId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string inboxId,
        [FromHeader(Name = InboxTokenHeader)] string? inboxToken,
        CancellationToken cancellationToken)
    {
        await service.DeleteAsync(inboxId, inboxToken, cancellationToken);
        return NoContent();
    }

    /// <summary>Returns the inbox address, expiration and sync status. Works for recently expired inboxes too.</summary>
    [HttpGet("{inboxId}/status")]
    [ProducesResponseType<InboxStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<InboxStatusResponse> GetStatus(
        string inboxId,
        [FromHeader(Name = InboxTokenHeader)] string? inboxToken,
        CancellationToken cancellationToken) =>
        service.GetStatusAsync(inboxId, inboxToken, cancellationToken);

    /// <summary>Demo mode only: adds a simulated verification email to the inbox.</summary>
    [HttpPost("{inboxId}/demo/simulate-otp")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [ProducesResponseType<DemoSimulationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<DemoSimulationResponse> SimulateDemoOtp(
        string inboxId,
        [FromHeader(Name = InboxTokenHeader)] string? inboxToken,
        CancellationToken cancellationToken) =>
        service.SimulateDemoOtpAsync(inboxId, inboxToken, cancellationToken);
}
