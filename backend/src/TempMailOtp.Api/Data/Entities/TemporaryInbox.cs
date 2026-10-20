namespace TempMailOtp.Api.Data.Entities;

/// <summary>
/// A temporary mailbox created through the configured email provider.
/// </summary>
public class TemporaryInbox
{
    /// <summary>Internal primary key. Never exposed to clients.</summary>
    public Guid Id { get; set; }

    /// <summary>Unguessable public identifier (128 random bits, base64url, 22 chars).</summary>
    public string PublicId { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the per-inbox access token. The token itself is only returned once.</summary>
    public byte[] AccessTokenHash { get; set; } = [];

    public string EmailAddress { get; set; } = string.Empty;

    /// <summary>Name of the provider that owns the mailbox (for example "MailTm" or "Demo").</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the mailbox/account.</summary>
    public string ProviderAccountId { get; set; } = string.Empty;

    /// <summary>Provider credentials for this mailbox, encrypted with ASP.NET Core Data Protection.</summary>
    public string? ProtectedProviderSecret { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    public string? LastSyncError { get; set; }

    public ICollection<ReceivedEmail> Emails { get; set; } = new List<ReceivedEmail>();
}
