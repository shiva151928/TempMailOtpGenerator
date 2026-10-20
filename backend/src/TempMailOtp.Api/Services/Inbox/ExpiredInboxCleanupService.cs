using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TempMailOtp.Api.Configuration;
using TempMailOtp.Api.Data;
using TempMailOtp.Api.Providers;

namespace TempMailOtp.Api.Services.Inbox;

/// <summary>
/// Periodically deletes inboxes that expired more than <see cref="TempMailOptions.ExpiredRetentionMinutes"/> ago:
/// first the mailbox at the provider (best effort), then the inbox row, which cascades to its emails and OTPs.
/// </summary>
public sealed class ExpiredInboxCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<TempMailOptions> options,
    TimeProvider timeProvider,
    ILogger<ExpiredInboxCleanupService> logger) : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.CleanupEnabled)
        {
            logger.LogInformation("Expired inbox cleanup is disabled (TempMail:CleanupEnabled = false).");
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.CleanupIntervalMinutes), timeProvider);
            do
            {
                try
                {
                    await CleanupOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Expired inbox cleanup failed; retrying in {Minutes} minutes.", settings.CleanupIntervalMinutes);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>Runs one cleanup pass and returns the number of inboxes deleted.</summary>
    public async Task<int> CleanupOnceAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TempMailDbContext>();
            var provider = scope.ServiceProvider.GetRequiredService<IEmailProvider>();
            var protector = scope.ServiceProvider.GetRequiredService<IProviderSecretProtector>();
            var locks = scope.ServiceProvider.GetRequiredService<InboxSyncLocks>();

            var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-options.Value.ExpiredRetentionMinutes);
            var batch = await db.TemporaryInboxes
                .Where(inbox => inbox.ExpiresAt <= cutoff)
                .OrderBy(inbox => inbox.ExpiresAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var inbox in batch)
            {
                if (!string.Equals(inbox.ProviderName, provider.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    await provider.DeleteMailboxAsync(inbox.ToProviderMailbox(protector), cancellationToken);
                }
                catch (EmailProviderException ex)
                {
                    logger.LogWarning(ex, "Could not delete the provider mailbox of expired inbox {InboxId}; removing local data anyway.", inbox.PublicId);
                }
            }

            db.TemporaryInboxes.RemoveRange(batch);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var inbox in batch)
            {
                locks.Remove(inbox.Id);
            }

            total += batch.Count;
            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        if (total > 0)
        {
            logger.LogInformation("Deleted {Count} expired inboxes and their emails.", total);
        }

        return total;
    }
}
