using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TempMailOtp.Api.Data;

namespace TempMailOtp.Tests.Support;

/// <summary>
/// Runs the real API in memory with the demo provider and an in-memory SQLite database
/// (SQL Server is used in production; the EF model is provider-neutral apart from the migrations).
/// </summary>
public sealed class TempMailApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:;Foreign Keys=True");

    public MutableTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Testing");
        builder.UseSetting("EmailProvider:Provider", "Demo");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("TempMail:CleanupEnabled", "false");
        builder.UseSetting("TempMail:SyncIntervalSeconds", "1");
        builder.UseSetting("TempMail:ManualRefreshMinIntervalSeconds", "0");
        builder.UseSetting("RateLimiting:GeneratePermitLimit", "1000");
        builder.UseSetting("RateLimiting:RefreshPermitLimit", "1000");

        builder.ConfigureTestServices(services =>
        {
            // Replace the SQL Server registration (EF Core keeps options in DbContextOptions<T>
            // and, since EF Core 9, IDbContextOptionsConfiguration<T>).
            var efRegistrations = services
                .Where(descriptor => descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GenericTypeArguments.Contains(typeof(TempMailDbContext))
                    && descriptor.ServiceType.Name.Contains("DbContextOptions", StringComparison.Ordinal))
                .ToList();
            foreach (var descriptor in efRegistrations)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<TempMailDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TempMailDbContext>().Database.EnsureCreated();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

/// <summary>A clock tests can move forward (and back) to exercise expiration.</summary>
public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now = _now.Add(amount);
}
