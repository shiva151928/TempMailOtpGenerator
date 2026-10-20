namespace TempMailOtp.Api.Providers;

/// <summary>
/// Abstraction over a temporary-mailbox / inbound-email service.
/// Add a new provider by implementing this interface and registering it in
/// <see cref="EmailProviderServiceCollectionExtensions"/>.
/// </summary>
public interface IEmailProvider
{
    /// <summary>Stable name stored with each inbox (for example "MailTm").</summary>
    string Name { get; }

    /// <summary>Human-readable name shown in the UI.</summary>
    string DisplayName { get; }

    /// <summary>True only for the built-in demo provider, which never receives real email.</summary>
    bool IsDemo { get; }

    /// <summary>Attribution the provider's terms require the UI to show, if any.</summary>
    ProviderAttribution? Attribution { get; }

    Task<ProviderMailbox> CreateMailboxAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderMessageSummary>> ListMessagesAsync(ProviderMailbox mailbox, CancellationToken cancellationToken);

    Task<ProviderMessage> GetMessageAsync(ProviderMailbox mailbox, string providerMessageId, CancellationToken cancellationToken);

    /// <summary>Deletes the mailbox at the provider. Must succeed silently if it no longer exists.</summary>
    Task DeleteMailboxAsync(ProviderMailbox mailbox, CancellationToken cancellationToken);
}

/// <param name="Address">The full email address.</param>
/// <param name="AccountId">The provider's id for the mailbox.</param>
/// <param name="Secret">Opaque provider credentials (stored encrypted), or null if none are needed.</param>
public sealed record ProviderMailbox(string Address, string AccountId, string? Secret);

public sealed record ProviderAttribution(string Text, string Url);

public sealed record ProviderMessageSummary(
    string Id,
    string FromAddress,
    string? FromName,
    string Subject,
    string Preview,
    DateTime ReceivedAt,
    bool HasAttachments);

public sealed record ProviderMessage(
    string Id,
    string FromAddress,
    string? FromName,
    string Subject,
    string Preview,
    DateTime ReceivedAt,
    bool HasAttachments,
    string? TextBody,
    string? HtmlBody);
