using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TempMailOtp.Api.Data.Entities;

namespace TempMailOtp.Api.Data;

public class TempMailDbContext(DbContextOptions<TempMailDbContext> options) : DbContext(options)
{
    /// <summary>
    /// SQL Server's datetime2 has no time zone. All timestamps are written as UTC and
    /// read back with <see cref="DateTimeKind.Utc"/> so they serialize with a trailing "Z".
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public DbSet<TemporaryInbox> TemporaryInboxes => Set<TemporaryInbox>();

    public DbSet<ReceivedEmail> ReceivedEmails => Set<ReceivedEmail>();

    public DbSet<DetectedOtp> DetectedOtps => Set<DetectedOtp>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TemporaryInbox>(inbox =>
        {
            inbox.ToTable("TemporaryInboxes");
            inbox.HasKey(x => x.Id);
            inbox.Property(x => x.Id).ValueGeneratedNever();

            inbox.Property(x => x.PublicId).HasMaxLength(32).IsUnicode(false).IsRequired();
            inbox.Property(x => x.AccessTokenHash).HasMaxLength(32).IsRequired();
            inbox.Property(x => x.EmailAddress).HasMaxLength(320).IsRequired();
            inbox.Property(x => x.ProviderName).HasMaxLength(50).IsUnicode(false).IsRequired();
            inbox.Property(x => x.ProviderAccountId).HasMaxLength(200).IsRequired();
            inbox.Property(x => x.ProtectedProviderSecret);
            inbox.Property(x => x.LastSyncError).HasMaxLength(500);

            inbox.HasIndex(x => x.PublicId).IsUnique();
            inbox.HasIndex(x => x.ExpiresAt);

            inbox.HasMany(x => x.Emails)
                .WithOne(x => x.Inbox)
                .HasForeignKey(x => x.InboxId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReceivedEmail>(email =>
        {
            email.ToTable("ReceivedEmails");
            email.HasKey(x => x.Id);
            email.Property(x => x.Id).ValueGeneratedNever();

            email.Property(x => x.ProviderMessageId).HasMaxLength(200).IsRequired();
            email.Property(x => x.FromAddress).HasMaxLength(320).IsRequired();
            email.Property(x => x.FromName).HasMaxLength(200);
            email.Property(x => x.Subject).HasMaxLength(1000).IsRequired();
            email.Property(x => x.Preview).HasMaxLength(500).IsRequired();
            email.Property(x => x.TextBody);
            email.Property(x => x.SanitizedHtmlBody);

            // Duplicate protection: a provider message is stored at most once per inbox.
            email.HasIndex(x => new { x.InboxId, x.ProviderMessageId }).IsUnique();
            // Inbox listing, newest first.
            email.HasIndex(x => new { x.InboxId, x.ReceivedAt });

            email.HasOne(x => x.DetectedOtp)
                .WithOne(x => x.ReceivedEmail)
                .HasForeignKey<DetectedOtp>(x => x.ReceivedEmailId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DetectedOtp>(otp =>
        {
            otp.ToTable("DetectedOtps");
            otp.HasKey(x => x.Id);
            otp.Property(x => x.Id).ValueGeneratedNever();

            otp.Property(x => x.Code).HasMaxLength(16).IsUnicode(false).IsRequired();
            otp.Property(x => x.MatchedPhrase).HasMaxLength(100);
            otp.Property(x => x.Source).HasMaxLength(16).IsUnicode(false).IsRequired();

            otp.HasIndex(x => x.ReceivedEmailId).IsUnique();
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(UtcConverter);
                }
            }
        }
    }
}
