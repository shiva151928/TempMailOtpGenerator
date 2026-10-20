using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TempMailOtp.Api.Configuration;
using TempMailOtp.Api.Controllers;
using TempMailOtp.Api.Data;
using TempMailOtp.Api.Infrastructure;
using TempMailOtp.Api.Providers;
using TempMailOtp.Api.Services.Content;
using TempMailOtp.Api.Services.Inbox;
using TempMailOtp.Api.Services.Otp;

const string CorsPolicyName = "AngularFrontend";

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ---------- Configuration ----------
builder.Services.AddOptions<TempMailOptions>()
    .Bind(configuration.GetSection(TempMailOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var tempMailSettings = configuration.GetSection(TempMailOptions.SectionName).Get<TempMailOptions>() ?? new TempMailOptions();
var rateLimitSettings = configuration.GetSection(ApiRateLimitOptions.SectionName).Get<ApiRateLimitOptions>() ?? new ApiRateLimitOptions();

// ---------- Database (SQL Server via EF Core) ----------
var connectionString = configuration.GetConnectionString("TempMailDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:TempMailDb is not configured. Set it in appsettings, user secrets, " +
        "or the ConnectionStrings__TempMailDb environment variable.");
}

builder.Services.AddDbContext<TempMailDbContext>(options =>
{
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(10),
        errorNumbersToAdd: null));

    // A model/migration mismatch is logged instead of blocking startup; check it with
    // "dotnet ef migrations has-pending-model-changes".
    options.ConfigureWarnings(warnings => warnings.Log(RelationalEventId.PendingModelChangesWarning));
});

// ---------- Secrets at rest ----------
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("TempMailOtp");
var keysPath = configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    // Persist keys outside the container/app folder so stored provider credentials survive restarts.
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

// ---------- Application services ----------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEmailProviders(configuration);
builder.Services.AddSingleton<IOtpDetector, OtpDetector>();
builder.Services.AddSingleton<IEmailHtmlSanitizer, EmailHtmlSanitizer>();
builder.Services.AddSingleton<IProviderSecretProtector, ProviderSecretProtector>();
builder.Services.AddSingleton<InboxSyncLocks>();
builder.Services.AddScoped<ITempMailService, TempMailService>();
builder.Services.AddHostedService<ExpiredInboxCleanupService>();

// ---------- Web API ----------
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddApiRateLimiting(rateLimitSettings);

builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy
    .WithOrigins(tempMailSettings.AllowedOrigins)
    .WithMethods("GET", "POST", "DELETE")
    .WithHeaders("Content-Type", "Accept", TempMailController.InboxTokenHeader)
    .WithExposedHeaders("Retry-After", "Location")
    .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

// ---------- Database migrations ----------
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<TempMailDbContext>();
    app.Logger.LogInformation("Applying database migrations...");
    await db.Database.MigrateAsync();
}

// ---------- HTTP pipeline ----------
if (app.Configuration.GetValue("ReverseProxy:Enabled", false))
{
    // Only proxies on loopback are trusted by default; add KnownProxies for others.
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsProduction())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "DENY";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        // Responses contain access tokens and private email content.
        headers.CacheControl = "no-store";
    }

    await next(context);
});

if (!app.Environment.IsProduction() || app.Configuration.GetValue("OpenApi:EnableInProduction", false))
{
    app.MapOpenApi(); // /openapi/v1.json
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "TempMail OTP API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "TempMail OTP API";
    });
}

app.UseRouting();
app.UseCors(CorsPolicyName);
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health").DisableRateLimiting();

app.Run();

/// <summary>Entry point; partial so integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program
{
}
