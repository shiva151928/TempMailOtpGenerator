using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using TempMailOtp.Api.Configuration;

namespace TempMailOtp.Api.Providers.Demo;

/// <summary>
/// Demo provider for trying the UI without a real email service. It never receives real email:
/// addresses use the reserved ".invalid" top-level domain, and messages only appear when the
/// user explicitly asks for a simulated one. Everything is clearly labeled "Demo" in the API and UI.
/// Mailboxes live in memory and are lost on restart.
/// </summary>
public sealed class DemoEmailProvider(TimeProvider timeProvider) : IEmailProvider
{
    public const string Domain = "demo.tempmail.invalid";
    public const string SenderAddress = "no-reply@demo.tempmail.invalid";

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ProviderMessage>> _mailboxes =
        new(StringComparer.Ordinal);

    public string Name => EmailProviderNames.Demo;

    public string DisplayName => "Demo mode (no real email)";

    public bool IsDemo => true;

    public ProviderAttribution? Attribution => null;

    public Task<ProviderMailbox> CreateMailboxAsync(CancellationToken cancellationToken)
    {
        var accountId = Guid.NewGuid().ToString("N");
        var localPart = "demo-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        _mailboxes[accountId] = new ConcurrentDictionary<string, ProviderMessage>(StringComparer.Ordinal);
        return Task.FromResult(new ProviderMailbox($"{localPart}@{Domain}", accountId, Secret: null));
    }

    public Task<IReadOnlyList<ProviderMessageSummary>> ListMessagesAsync(ProviderMailbox mailbox, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderMessageSummary> result = _mailboxes.TryGetValue(mailbox.AccountId, out var messages)
            ? messages.Values
                .OrderByDescending(message => message.ReceivedAt)
                .Select(message => new ProviderMessageSummary(
                    message.Id,
                    message.FromAddress,
                    message.FromName,
                    message.Subject,
                    message.Preview,
                    message.ReceivedAt,
                    message.HasAttachments))
                .ToList()
            : [];

        return Task.FromResult(result);
    }

    public Task<ProviderMessage> GetMessageAsync(ProviderMailbox mailbox, string providerMessageId, CancellationToken cancellationToken)
    {
        if (_mailboxes.TryGetValue(mailbox.AccountId, out var messages)
            && messages.TryGetValue(providerMessageId, out var message))
        {
            return Task.FromResult(message);
        }

        throw new EmailProviderException(
            "The demo message no longer exists (demo data is kept in memory only).",
            EmailProviderErrorKind.MailboxNotFound);
    }

    public Task DeleteMailboxAsync(ProviderMailbox mailbox, CancellationToken cancellationToken)
    {
        _mailboxes.TryRemove(mailbox.AccountId, out _);
        return Task.CompletedTask;
    }

    /// <summary>Adds a simulated verification email to a demo mailbox and returns the code it contains.</summary>
    public string AddSimulatedOtpEmail(ProviderMailbox mailbox)
    {
        var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString(CultureInfo.InvariantCulture);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid().ToString("N");

        var text =
            $"[Demo] This is a simulated message. No real email was received.\n\n" +
            $"Your verification code is {code}.\n\n" +
            "It expires in 10 minutes. If you didn't request this code, you can ignore this message.";

        var html =
            "<div style=\"font-family:Arial,sans-serif;color:#0f1e3d\">" +
            "<p style=\"color:#a45a00\"><strong>[Demo]</strong> This is a simulated message. No real email was received.</p>" +
            "<p>Your verification code is</p>" +
            $"<p style=\"font-size:28px;letter-spacing:6px;font-weight:700\">{code}</p>" +
            "<p>It expires in 10 minutes. If you didn't request this code, you can ignore this message.</p>" +
            "<script>alert('this script is removed by the sanitizer')</script>" +
            "</div>";

        var message = new ProviderMessage(
            id,
            SenderAddress,
            "Demo sender",
            "[Demo] Your verification code",
            $"[Demo] Your verification code is {code}.",
            now,
            HasAttachments: false,
            text,
            html);

        var messages = _mailboxes.GetOrAdd(
            mailbox.AccountId,
            _ => new ConcurrentDictionary<string, ProviderMessage>(StringComparer.Ordinal));
        messages[id] = message;
        return code;
    }
}
