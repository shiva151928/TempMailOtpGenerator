using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TempMailOtp.Api.Configuration;
using TempMailOtp.Api.Contracts;
using TempMailOtp.Api.Data;
using TempMailOtp.Api.Data.Entities;
using TempMailOtp.Api.Providers;
using TempMailOtp.Api.Providers.Demo;
using TempMailOtp.Api.Services.Content;
using TempMailOtp.Api.Services.Otp;

namespace TempMailOtp.Api.Services.Inbox;

public interface ITempMailService
{
    ProviderInfoResponse GetProviderInfo();

    Task<GenerateInboxResponse> GenerateAsync(CancellationToken cancellationToken);

    /// <summary>Returns stored messages, syncing with the provider first if the last sync is stale.</summary>
    Task<InboxMessagesResponse> GetMessagesAsync(string inboxId, string? accessToken, CancellationToken cancellationToken);

    /// <summary>Forces a provider sync (throws if the provider fails) and returns the messages.</summary>
    Task<InboxMessagesResponse> RefreshAsync(string inboxId, string? accessToken, CancellationToken cancellationToken);

    Task<MessageDetailDto> GetMessageAsync(string inboxId, string? accessToken, Guid messageId, CancellationToken cancellationToken);

    Task<OtpResponse> GetOtpsAsync(string inboxId, string? accessToken, CancellationToken cancellationToken);

    Task<InboxStatusResponse> GetStatusAsync(string inboxId, string? accessToken, CancellationToken cancellationToken);

    Task DeleteAsync(string inboxId, string? accessToken, CancellationToken cancellationToken);

    Task<DemoSimulationResponse> SimulateDemoOtpAsync(string inboxId, string? accessToken, CancellationToken cancellationToken);
}

public sealed class TempMailService : ITempMailService
{
    private const int OtpHistoryLimit = 50;
    private const int MaxStoredBodyLength = 200_000;

    private readonly TempMailDbContext _db;
    private readonly IEmailProvider _provider;
    private readonly IOtpDetector _otpDetector;
    private readonly IEmailHtmlSanitizer _sanitizer;
    private readonly IProviderSecretProtector _protector;
    private readonly InboxSyncLocks _locks;
    private readonly TimeProvider _time;
    private readonly TempMailOptions _options;
    private readonly ILogger<TempMailService> _logger;

    public TempMailService(
        TempMailDbContext db,
        IEmailProvider provider,
        IOtpDetector otpDetector,
        IEmailHtmlSanitizer sanitizer,
        IProviderSecretProtector protector,
        InboxSyncLocks locks,
        TimeProvider time,
        IOptions<TempMailOptions> options,
        ILogger<TempMailService> logger)
    {
        _db = db;
        _provider = provider;
        _otpDetector = otpDetector;
        _sanitizer = sanitizer;
        _protector = protector;
        _locks = locks;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public ProviderInfoResponse GetProviderInfo() => new(
        _provider.Name,
        _provider.DisplayName,
        _provider.IsDemo,
        _provider.Attribution?.Text,
        _provider.Attribution?.Url,
        _options.ClientPollIntervalSeconds,
        _options.InboxLifetimeMinutes);

    public async Task<GenerateInboxResponse> GenerateAsync(CancellationToken cancellationToken)
    {
        var mailbox = await _provider.CreateMailboxAsync(cancellationToken);

        if (mailbox.Address.Length > 320 || mailbox.AccountId.Length > 200)
        {
            throw new EmailProviderException(
                "The email provider returned an address or account id that is too long.",
                EmailProviderErrorKind.InvalidResponse);
        }

        var now = UtcNow;
        var accessToken = InboxTokens.CreateAccessToken();
        var inbox = new TemporaryInbox
        {
            Id = Guid.CreateVersion7(),
            PublicId = InboxTokens.CreatePublicId(),
            AccessTokenHash = InboxTokens.HashToken(accessToken),
            EmailAddress = mailbox.Address,
            ProviderName = _provider.Name,
            ProviderAccountId = mailbox.AccountId,
            ProtectedProviderSecret = _protector.Protect(mailbox.Secret),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.InboxLifetimeMinutes),
        };

        _db.TemporaryInboxes.Add(inbox);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Do not leave an orphaned mailbox at the provider if we could not record it.
            await TryDeleteProviderMailboxAsync(mailbox, inbox.PublicId);
            throw;
        }

        _logger.LogInformation(
            "Generated inbox {InboxId} via {Provider}, expires {ExpiresAt:o}.", inbox.PublicId, _provider.Name, inbox.ExpiresAt);

        return new GenerateInboxResponse(
            inbox.PublicId,
            accessToken,
            inbox.EmailAddress,
            inbox.CreatedAt,
            inbox.ExpiresAt,
            _provider.Name,
            _provider.IsDemo,
            _options.ClientPollIntervalSeconds);
    }

    public async Task<InboxMessagesResponse> GetMessagesAsync(string inboxId, string? accessToken, CancellationToken cancellationToken)
    {
        var inbox = await GetAuthorizedInboxAsync(inboxId, accessToken, allowExpired: false, cancellationToken);
        var sync = await SyncAsync(inbox, manual: false, cancellationToken);
        return await BuildMessagesResponseAsync(inbox, sync, cancellationToken);
    }

    public async Task<InboxMessagesResponse> RefreshAsync(string inboxId, string? accessToken, CancellationToken cancellationToken)
    {
        var inbox = await GetAuthorizedInboxAsync(inboxId, accessToken, allowExpired: false, cancellationToken);
        var sync = await SyncAsync(inbox, manual: true, cancellationToken);
        return await BuildMessagesResponseAsync(inbox, sync, cancellationToken);
    }

    public async Task<MessageDetailDto> GetMessageAsync(
        string inboxId, string? accessToken, Guid messageId, CancellationToken cancellationToken)
    {
        var inbox = await GetAuthorizedInboxAsync(inboxId, accessToken, allowExpired: false, cancellationToken);

        var email = await _db.ReceivedEmails
            .Include(e => e.DetectedOtp)
            .SingleOrDefaultAsync(e => e.Id == messageId && e.InboxId == inbox.Id, cancellationToken)
            ?? throw new MessageNotFoundException();

        if (!email.IsRead)
        {
            email.IsRead = true;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var otp = email.DetectedOtp is { } detected
            ? new OtpDto(
                detected.Code,
                detected.Confidence,
                email.Id,
                email.FromAddress,
                email.FromName,
                email.Subject,
                email.ReceivedAt,
                detected.DetectedAt,
                detected.Source,
                detected.MatchedPhrase)
            : null;

        return new MessageDetailDto(
            email.Id,
            email.FromAddress,
            email.FromName,
            email.Subject,
            email.ReceivedAt,
            email.HasAttachments,
            email.TextBody,
            email.SanitizedHtmlBody,
            otp);
    }

    public async Task<OtpResponse> GetOtpsAsync(string inboxId, string? accessToken, CancellationToken cancellationToken)
    {
        var inbox = await GetAuthorizedInboxAsync(inboxId, accessToken, allowExpired: false, cancellationToken);
        await SyncAsync(inbox, manual: false, cancellationToken);

        var history = await QueryOtps(inbox.Id).Take(OtpHistoryLimit).ToListAsync(cancellationToken);
        return new OtpResponse(history.FirstOrDefault(), history);
    }

    public async Task<InboxStatusResponse> GetStatusAsync(string inboxId, string? accessToken, CancellationToken cancellationToken)
    {
        var inbox = await GetAuthorizedInboxAsync(inboxId, accessToken, allowExpired: true, cancellationToken);
        var now = UtcNow;
        var remaining = inbox.ExpiresAt - now;
        var messageCount = await _db.ReceivedEmails.CountAsync(e => e.InboxId == inbox.Id, cancellationToken);

        return new InboxStatusResponse(
            inbox.PublicId,
            inbox.EmailAddress,
            inbox.CreatedAt,
            inbox.ExpiresAt,
            IsExpired: remaining <= TimeSpan.Zero,
            SecondsRemaining: (int)Math.Max(0, Math.Ceiling(remaining.TotalSeconds)),
            messageCount,
            inbox.ProviderName,
            EmailProviderNames.IsDemo(inbox.ProviderName),
            inbox.LastSyncedAt,
            inbox.LastSyncError,
            _options.ClientPollIntervalSeconds,
            now);
    }

    public async Task DeleteAsync(string inboxId, string? accessToken, CancellationToken cancellationToken)
    {
        var inbox = await GetAuthorizedInboxAsync(inboxId, accessToken, allowExpired: true, cancellationToken);

        if (IsOwnedByCurrentProvider(inbox))
        {
            try
            {
                await _provider.DeleteMailboxAsync(inbox.ToProviderMailbox(_protector), cancellationToken);
            }
            catch (EmailProviderException ex)
            {
                // The local data is removed regardless; the provider mailbox is unreachable through this app.
                _logger.LogWarning(ex, "Could not delete the provider mailbox for inbox {InboxId}.", inbox.PublicId);
            }
        }

        _db.TemporaryInboxes.Remove(inbox);
        await _db.SaveChangesAsync(cancellationToken);
        _locks.Remove(inbox.Id);
        _logger.LogInformation("Deleted inbox {InboxId}.", inbox.PublicId);
    }

    public async Task<DemoSimulationResponse> SimulateDemoOtpAsync(
        string inboxId, string? accessToken, CancellationToken cancellationToken)
    {
        if (_provider is not DemoEmailProvider demoProvider)
        {
            throw new DemoModeRequiredException();
        }

        var inbox = await GetAuthorizedInboxAsync(inboxId, accessToken, allowExpired: false, cancellationToken);
        if (!IsOwnedByCurrentProvider(inbox))
        {
            throw new DemoModeRequiredException();
        }

        var code = demoProvider.AddSimulatedOtpEmail(inbox.ToProviderMailbox(_protector));

        // Make the next poll pick the message up immediately.
        inbox.LastSyncedAt = null;
        await _db.SaveChangesAsync(cancellationToken);

        return new DemoSimulationResponse(
            "A simulated verification email was added to this demo inbox. It appears on the next refresh.",
            code);
    }

    private async Task<TemporaryInbox> GetAuthorizedInboxAsync(
        string inboxId, string? accessToken, bool allowExpired, CancellationToken cancellationToken)
    {
        if (!InboxTokens.IsValidPublicId(inboxId))
        {
            throw new InboxNotFoundException();
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InboxTokenMissingException();
        }

        var inbox = await _db.TemporaryInboxes.SingleOrDefaultAsync(i => i.PublicId == inboxId, cancellationToken);

        // A wrong token is reported exactly like a missing inbox, so ids cannot be probed.
        if (inbox is null || !InboxTokens.VerifyToken(accessToken, inbox.AccessTokenHash))
        {
            throw new InboxNotFoundException();
        }

        if (!allowExpired && inbox.ExpiresAt <= UtcNow)
        {
            throw new InboxExpiredException(inbox.ExpiresAt);
        }

        return inbox;
    }

    private async Task<SyncOutcome> SyncAsync(TemporaryInbox inbox, bool manual, CancellationToken cancellationToken)
    {
        var minInterval = TimeSpan.FromSeconds(manual ? _options.ManualRefreshMinIntervalSeconds : _options.SyncIntervalSeconds);
        if (WasSyncedWithin(inbox, minInterval))
        {
            return new SyncOutcome(0, inbox.LastSyncError);
        }

        if (!IsOwnedByCurrentProvider(inbox))
        {
            var message = $"This inbox was created with the '{inbox.ProviderName}' provider, which is no longer configured on the server.";
            if (manual)
            {
                throw new EmailProviderException(message, EmailProviderErrorKind.Unavailable);
            }

            return new SyncOutcome(0, message);
        }

        using var syncLock = await _locks.AcquireAsync(inbox.Id, cancellationToken);

        // Another request may have completed a sync while this one waited for the lock.
        await _db.Entry(inbox).ReloadAsync(cancellationToken);
        if (WasSyncedWithin(inbox, minInterval))
        {
            return new SyncOutcome(0, inbox.LastSyncError);
        }

        var now = UtcNow;
        var newMessages = 0;
        try
        {
            var mailbox = inbox.ToProviderMailbox(_protector);
            var summaries = await _provider.ListMessagesAsync(mailbox, cancellationToken);

            if (summaries.Count > 0)
            {
                var knownIds = await _db.ReceivedEmails
                    .Where(e => e.InboxId == inbox.Id)
                    .Select(e => e.ProviderMessageId)
                    .ToListAsync(cancellationToken);
                var known = knownIds.ToHashSet(StringComparer.Ordinal);

                var fresh = summaries
                    .Where(summary => !known.Contains(Truncate(summary.Id, 200)))
                    .DistinctBy(summary => summary.Id)
                    .OrderBy(summary => summary.ReceivedAt)
                    .Take(_options.MaxMessagesPerSync)
                    .ToList();

                foreach (var summary in fresh)
                {
                    ProviderMessage message;
                    try
                    {
                        message = await _provider.GetMessageAsync(mailbox, summary.Id, cancellationToken);
                    }
                    catch (EmailProviderException ex) when (ex.Kind == EmailProviderErrorKind.MailboxNotFound)
                    {
                        continue; // Deleted at the provider between listing and reading.
                    }

                    _db.ReceivedEmails.Add(CreateEmail(inbox, summary.Id, message, now));
                    newMessages++;
                }
            }

            inbox.LastSyncedAt = now;
            inbox.LastSyncError = null;
            await _db.SaveChangesAsync(cancellationToken);
            return new SyncOutcome(newMessages, null);
        }
        catch (EmailProviderException ex)
        {
            _logger.LogWarning(ex, "Provider sync failed for inbox {InboxId}.", inbox.PublicId);

            // Keep messages fetched before the failure and remember the error for the UI.
            inbox.LastSyncedAt = now;
            inbox.LastSyncError = Truncate(ex.Message, 500);
            try
            {
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            catch (DbUpdateException saveException)
            {
                _logger.LogWarning(saveException, "Could not record the sync failure for inbox {InboxId}.", inbox.PublicId);
                _db.ChangeTracker.Clear();
            }

            if (manual)
            {
                throw;
            }

            return new SyncOutcome(newMessages, ex.Message);
        }
        catch (DbUpdateException ex)
        {
            // Another instance stored the same provider messages first (unique index). The next sync catches up.
            _logger.LogInformation(ex, "Concurrent sync detected for inbox {InboxId}.", inbox.PublicId);
            _db.ChangeTracker.Clear();
            return new SyncOutcome(0, null);
        }
    }

    private ReceivedEmail CreateEmail(TemporaryInbox inbox, string providerMessageId, ProviderMessage message, DateTime now)
    {
        var hasText = !string.IsNullOrWhiteSpace(message.TextBody);
        var textBody = hasText ? message.TextBody!.Replace("\r\n", "\n").Trim() : HtmlTextExtractor.ToPlainText(message.HtmlBody);
        var sanitizedHtml = string.IsNullOrWhiteSpace(message.HtmlBody) ? null : _sanitizer.Sanitize(message.HtmlBody);
        var previewSource = string.IsNullOrWhiteSpace(message.Preview) ? textBody : message.Preview;

        var email = new ReceivedEmail
        {
            Id = Guid.CreateVersion7(),
            InboxId = inbox.Id,
            ProviderMessageId = Truncate(providerMessageId, 200),
            FromAddress = Truncate(string.IsNullOrWhiteSpace(message.FromAddress) ? "unknown" : message.FromAddress, 320),
            FromName = TruncateOrNull(message.FromName, 200),
            Subject = Truncate(message.Subject, 1000),
            Preview = TextNormalizer.ToPreview(previewSource, 200),
            TextBody = string.IsNullOrEmpty(textBody) ? null : Truncate(textBody, MaxStoredBodyLength),
            SanitizedHtmlBody = string.IsNullOrEmpty(sanitizedHtml) ? null : Truncate(sanitizedHtml, MaxStoredBodyLength),
            HasAttachments = message.HasAttachments,
            ReceivedAt = EnsureUtc(message.ReceivedAt),
            FetchedAt = now,
        };

        var detection = _otpDetector.Detect(new EmailContent(message.Subject, message.TextBody, message.HtmlBody));
        if (detection is not null)
        {
            email.DetectedOtp = new DetectedOtp
            {
                Id = Guid.CreateVersion7(),
                Code = detection.Code,
                Confidence = detection.Confidence,
                MatchedPhrase = TruncateOrNull(detection.MatchedPhrase, 100),
                Source = detection.Source,
                DetectedAt = now,
            };

            // The code itself is deliberately not logged.
            _logger.LogInformation(
                "Detected an OTP in a message for inbox {InboxId} (confidence {Confidence}).", inbox.PublicId, detection.Confidence);
        }

        return email;
    }

    private async Task<InboxMessagesResponse> BuildMessagesResponseAsync(
        TemporaryInbox inbox, SyncOutcome sync, CancellationToken cancellationToken)
    {
        var messages = await _db.ReceivedEmails
            .AsNoTracking()
            .Where(e => e.InboxId == inbox.Id)
            .OrderByDescending(e => e.ReceivedAt)
            .ThenByDescending(e => e.FetchedAt)
            .Select(e => new MessageSummaryDto(
                e.Id,
                e.FromAddress,
                e.FromName,
                e.Subject,
                e.Preview,
                e.ReceivedAt,
                e.IsRead,
                e.HasAttachments,
                e.DetectedOtp != null ? e.DetectedOtp.Code : null))
            .ToListAsync(cancellationToken);

        var latestOtp = await QueryOtps(inbox.Id).FirstOrDefaultAsync(cancellationToken);

        return new InboxMessagesResponse(
            inbox.PublicId,
            messages,
            latestOtp,
            sync.NewMessages,
            inbox.LastSyncedAt,
            sync.Error,
            inbox.ExpiresAt,
            UtcNow);
    }

    private IQueryable<OtpDto> QueryOtps(Guid inboxId) =>
        _db.DetectedOtps
            .AsNoTracking()
            .Where(o => o.ReceivedEmail.InboxId == inboxId)
            .OrderByDescending(o => o.ReceivedEmail.ReceivedAt)
            .ThenByDescending(o => o.DetectedAt)
            .Select(o => new OtpDto(
                o.Code,
                o.Confidence,
                o.ReceivedEmailId,
                o.ReceivedEmail.FromAddress,
                o.ReceivedEmail.FromName,
                o.ReceivedEmail.Subject,
                o.ReceivedEmail.ReceivedAt,
                o.DetectedAt,
                o.Source,
                o.MatchedPhrase));

    private bool WasSyncedWithin(TemporaryInbox inbox, TimeSpan interval) =>
        inbox.LastSyncedAt is { } lastSynced && UtcNow - lastSynced < interval;

    private bool IsOwnedByCurrentProvider(TemporaryInbox inbox) =>
        string.Equals(inbox.ProviderName, _provider.Name, StringComparison.OrdinalIgnoreCase);

    private async Task TryDeleteProviderMailboxAsync(ProviderMailbox mailbox, string inboxId)
    {
        try
        {
            await _provider.DeleteMailboxAsync(mailbox, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up the provider mailbox for unsaved inbox {InboxId}.", inboxId);
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static string Truncate(string? value, int maxLength)
    {
        value ??= string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? TruncateOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

    private readonly record struct SyncOutcome(int NewMessages, string? Error);
}
