namespace TempMailOtp.Api.Contracts;

/// <summary>Public information about the configured provider (no secrets).</summary>
public sealed record ProviderInfoResponse(
    string Provider,
    string DisplayName,
    bool IsDemo,
    string? AttributionText,
    string? AttributionUrl,
    int ClientPollIntervalSeconds,
    int InboxLifetimeMinutes);

/// <summary>
/// Returned once when an inbox is generated. <see cref="AccessToken"/> must be sent in the
/// X-Inbox-Token header on every later request for this inbox; it is not stored in plain text.
/// </summary>
public sealed record GenerateInboxResponse(
    string InboxId,
    string AccessToken,
    string Address,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string Provider,
    bool IsDemo,
    int PollIntervalSeconds);

public sealed record InboxStatusResponse(
    string InboxId,
    string Address,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    bool IsExpired,
    int SecondsRemaining,
    int MessageCount,
    string Provider,
    bool IsDemo,
    DateTime? LastSyncedAt,
    string? LastSyncError,
    int PollIntervalSeconds,
    DateTime ServerTime);

public sealed record MessageSummaryDto(
    Guid Id,
    string FromAddress,
    string? FromName,
    string Subject,
    string Preview,
    DateTime ReceivedAt,
    bool IsRead,
    bool HasAttachments,
    string? OtpCode);

public sealed record MessageDetailDto(
    Guid Id,
    string FromAddress,
    string? FromName,
    string Subject,
    DateTime ReceivedAt,
    bool HasAttachments,
    string? TextBody,
    string? SanitizedHtml,
    OtpDto? Otp);

public sealed record OtpDto(
    string Code,
    double Confidence,
    Guid MessageId,
    string SenderAddress,
    string? SenderName,
    string Subject,
    DateTime ReceivedAt,
    DateTime DetectedAt,
    string Source,
    string? MatchedPhrase);

public sealed record InboxMessagesResponse(
    string InboxId,
    IReadOnlyList<MessageSummaryDto> Messages,
    OtpDto? LatestOtp,
    int NewMessageCount,
    DateTime? LastSyncedAt,
    string? SyncError,
    DateTime ExpiresAt,
    DateTime ServerTime);

public sealed record OtpResponse(OtpDto? Latest, IReadOnlyList<OtpDto> History);

public sealed record DemoSimulationResponse(string Message, string ExpectedCode);
