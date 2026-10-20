using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using TempMailOtp.Api.Configuration;
using TempMailOtp.Api.Providers.Demo;
using TempMailOtp.Api.Providers.MailTm;

namespace TempMailOtp.Api.Providers;

public static class EmailProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers all providers and resolves <see cref="IEmailProvider"/> from "EmailProvider:Provider".
    /// To add a provider: implement <see cref="IEmailProvider"/>, register it here and add its name to
    /// <see cref="EmailProviderNames"/>.
    /// </summary>
    public static IServiceCollection AddEmailProviders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailProviderOptions>()
            .Bind(configuration.GetSection(EmailProviderOptions.SectionName))
            .Validate(
                options => EmailProviderNames.IsKnown(options.Provider),
                $"EmailProvider:Provider must be '{EmailProviderNames.MailTm}' or '{EmailProviderNames.Demo}'.")
            .Validate(
                options => Uri.TryCreate(options.MailTm.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                "EmailProvider:MailTm:BaseUrl must be an absolute https URL.")
            .Validate(
                options => options.MailTm.MaxRequestsPerSecond is >= 1 and <= 8,
                "EmailProvider:MailTm:MaxRequestsPerSecond must be between 1 and 8 (mail.tm allows 8 per second per IP).")
            .Validate(
                options => options.MailTm.TimeoutSeconds is >= 2 and <= 120,
                "EmailProvider:MailTm:TimeoutSeconds must be between 2 and 120.")
            .ValidateOnStart();

        services.AddMemoryCache();

        services.AddSingleton(serviceProvider => new ProviderRequestLimiter(
            serviceProvider.GetRequiredService<IOptions<EmailProviderOptions>>().Value.MailTm.MaxRequestsPerSecond));
        services.AddTransient<ProviderRateLimitingHandler>();

        services.AddHttpClient<MailTmEmailProvider>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<EmailProviderOptions>>().Value.MailTm;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TempMailOtpGenerator/1.0");
            })
            .AddHttpMessageHandler<ProviderRateLimitingHandler>();

        services.AddSingleton<DemoEmailProvider>();

        services.AddScoped<IEmailProvider>(serviceProvider =>
        {
            var selected = serviceProvider.GetRequiredService<IOptions<EmailProviderOptions>>().Value.Provider;
            return EmailProviderNames.IsDemo(selected)
                ? (IEmailProvider)serviceProvider.GetRequiredService<DemoEmailProvider>()
                : serviceProvider.GetRequiredService<MailTmEmailProvider>();
        });

        return services;
    }
}
