namespace TempMailOtp.Api.Data.Entities;

/// <summary>
/// An email fetched from the provider for a temporary inbox.
/// Only display-safe content is stored: plain text and server-sanitized HTML.
/// </summary>
public class ReceivedEmail
{
    public Guid Id { get; set; }

    public Guid InboxId { get; set; }

    public TemporaryInbox Inbox { get; set; } = null!;

    /// <summary>The provider's message id. Unique per inbox, used to skip duplicates.</summary>
    public string ProviderMessageId { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string? FromName { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Preview { get; set; } = string.Empty;

    public string? TextBody { get; set; }

    public string? SanitizedHtmlBody { get; set; }

    public bool HasAttachments { get; set; }

    public bool IsRead { get; set; }

    /// <summary>When the provider received the message (UTC).</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>When this application fetched the message (UTC).</summary>
    public DateTime FetchedAt { get; set; }

    public DetectedOtp? DetectedOtp { get; set; }
}
