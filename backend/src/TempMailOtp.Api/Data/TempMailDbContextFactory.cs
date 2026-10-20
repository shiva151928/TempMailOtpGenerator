using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TempMailOtp.Api.Data;

/// <summary>
/// Used only by the EF Core command-line tools (dotnet ef migrations / database update),
/// so they do not need to start the web application.
/// </summary>
public sealed class TempMailDbContextFactory : IDesignTimeDbContextFactory<TempMailDbContext>
{
    public TempMailDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<TempMailDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("TempMailDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:TempMailDb is not configured. Set it in appsettings, user secrets, " +
                "or the ConnectionStrings__TempMailDb environment variable.");
        }

        var options = new DbContextOptionsBuilder<TempMailDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TempMailDbContext(options);
    }
}
