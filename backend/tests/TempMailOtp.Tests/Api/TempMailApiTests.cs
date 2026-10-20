using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TempMailOtp.Api.Configuration;
using TempMailOtp.Api.Contracts;
using TempMailOtp.Api.Providers.Demo;
using TempMailOtp.Api.Services.Inbox;
using TempMailOtp.Tests.Support;

namespace TempMailOtp.Tests.Api;

/// <summary>End-to-end API tests: HTTP pipeline, EF Core, OTP detection and sanitization with the demo provider.</summary>
public sealed class TempMailApiTests(TempMailApiFactory factory) : IClassFixture<TempMailApiFactory>
{
    private const string TokenHeader = "X-Inbox-Token";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Provider_endpoint_reports_demo_mode()
    {
        var info = await _client.GetFromJsonAsync<ProviderInfoResponse>("/api/temp-mail/provider", Json);

        Assert.NotNull(info);
        Assert.Equal("Demo", info.Provider);
        Assert.True(info.IsDemo);
        Assert.True(info.ClientPollIntervalSeconds > 0);
    }

    [Fact]
    public async Task Generate_returns_address_token_and_expiry()
    {
        var response = await _client.PostAsync("/api/temp-mail/generate", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var inbox = await response.Content.ReadFromJsonAsync<GenerateInboxResponse>(Json);
        Assert.NotNull(inbox);
        Assert.EndsWith("@" + DemoEmailProvider.Domain, inbox.Address);
        Assert.Equal(InboxTokens.PublicIdLength, inbox.InboxId.Length);
        Assert.False(string.IsNullOrWhiteSpace(inbox.AccessToken));
        Assert.True(inbox.IsDemo);
        Assert.Equal(TimeSpan.FromMinutes(60), inbox.ExpiresAt - inbox.CreatedAt);
    }

    [Fact]
    public async Task Inbox_endpoints_require_the_matching_access_token()
    {
        var inbox = await GenerateAsync();

        var missing = await _client.GetAsync($"/api/temp-mail/{inbox.InboxId}/messages");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("inbox_token_missing", await ProblemCodeAsync(missing));

        using var wrongRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/temp-mail/{inbox.InboxId}/messages");
        wrongRequest.Headers.Add(TokenHeader, "not-the-right-token");
        var wrong = await _client.SendAsync(wrongRequest);
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        Assert.Equal("inbox_not_found", await ProblemCodeAsync(wrong));
    }

    [Fact]
    public async Task Unknown_inbox_id_returns_not_found()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/temp-mail/AAAAAAAAAAAAAAAAAAAAAA/status");
        request.Headers.Add(TokenHeader, "some-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task New_inbox_is_empty()
    {
        var inbox = await GenerateAsync();

        var messages = await SendAsync<InboxMessagesResponse>(HttpMethod.Get, $"{inbox.InboxId}/messages", inbox.AccessToken);
        var otp = await SendAsync<OtpResponse>(HttpMethod.Get, $"{inbox.InboxId}/otp", inbox.AccessToken);

        Assert.Empty(messages.Messages);
        Assert.Null(messages.LatestOtp);
        Assert.Null(messages.SyncError);
        Assert.Null(otp.Latest);
        Assert.Empty(otp.History);
    }

    [Fact]
    public async Task Simulated_email_is_received_once_with_its_otp_and_sanitized_html()
    {
        var inbox = await GenerateAsync();

        var simulation = await SendAsync<DemoSimulationResponse>(HttpMethod.Post, $"{inbox.InboxId}/demo/simulate-otp", inbox.AccessToken);
        var refreshed = await SendAsync<InboxMessagesResponse>(HttpMethod.Post, $"{inbox.InboxId}/refresh", inbox.AccessToken);

        var message = Assert.Single(refreshed.Messages);
        Assert.Equal(1, refreshed.NewMessageCount);
        Assert.Equal(simulation.ExpectedCode, message.OtpCode);
        Assert.Equal(DemoEmailProvider.SenderAddress, message.FromAddress);
        Assert.NotNull(refreshed.LatestOtp);
        Assert.Equal(simulation.ExpectedCode, refreshed.LatestOtp.Code);
        Assert.Equal("Demo sender", refreshed.LatestOtp.SenderName);

        // Refreshing again must not store the same provider message twice.
        var again = await SendAsync<InboxMessagesResponse>(HttpMethod.Post, $"{inbox.InboxId}/refresh", inbox.AccessToken);
        Assert.Single(again.Messages);
        Assert.Equal(0, again.NewMessageCount);

        var otp = await SendAsync<OtpResponse>(HttpMethod.Get, $"{inbox.InboxId}/otp", inbox.AccessToken);
        Assert.Single(otp.History);
        Assert.Equal(simulation.ExpectedCode, otp.Latest?.Code);

        var detail = await SendAsync<MessageDetailDto>(HttpMethod.Get, $"{inbox.InboxId}/messages/{message.Id}", inbox.AccessToken);
        Assert.Contains(simulation.ExpectedCode, detail.TextBody);
        Assert.NotNull(detail.SanitizedHtml);
        Assert.Contains(simulation.ExpectedCode, detail.SanitizedHtml);
        Assert.DoesNotContain("<script", detail.SanitizedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(simulation.ExpectedCode, detail.Otp?.Code);

        var afterOpen = await SendAsync<InboxMessagesResponse>(HttpMethod.Get, $"{inbox.InboxId}/messages", inbox.AccessToken);
        Assert.True(Assert.Single(afterOpen.Messages).IsRead);
    }

    [Fact]
    public async Task Multiple_otp_emails_keep_history_newest_first()
    {
        var inbox = await GenerateAsync();

        var first = await SendAsync<DemoSimulationResponse>(HttpMethod.Post, $"{inbox.InboxId}/demo/simulate-otp", inbox.AccessToken);
        await SendAsync<InboxMessagesResponse>(HttpMethod.Post, $"{inbox.InboxId}/refresh", inbox.AccessToken);

        factory.Time.Advance(TimeSpan.FromSeconds(30));
        var second = await SendAsync<DemoSimulationResponse>(HttpMethod.Post, $"{inbox.InboxId}/demo/simulate-otp", inbox.AccessToken);
        var refreshed = await SendAsync<InboxMessagesResponse>(HttpMethod.Post, $"{inbox.InboxId}/refresh", inbox.AccessToken);
        factory.Time.Advance(TimeSpan.FromSeconds(-30));

        Assert.Equal(2, refreshed.Messages.Count);
        Assert.Equal(second.ExpectedCode, refreshed.LatestOtp?.Code);

        var otp = await SendAsync<OtpResponse>(HttpMethod.Get, $"{inbox.InboxId}/otp", inbox.AccessToken);
        Assert.Equal(new[] { second.ExpectedCode, first.ExpectedCode }, otp.History.Select(h => h.Code));
    }

    [Fact]
    public async Task Unknown_message_returns_not_found()
    {
        var inbox = await GenerateAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/temp-mail/{inbox.InboxId}/messages/{Guid.NewGuid()}");
        request.Headers.Add(TokenHeader, inbox.AccessToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("message_not_found", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Deleted_inbox_can_no_longer_be_used()
    {
        var inbox = await GenerateAsync();

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/temp-mail/{inbox.InboxId}");
        delete.Headers.Add(TokenHeader, inbox.AccessToken);
        var deleteResponse = await _client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var status = new HttpRequestMessage(HttpMethod.Get, $"/api/temp-mail/{inbox.InboxId}/status");
        status.Headers.Add(TokenHeader, inbox.AccessToken);
        var statusResponse = await _client.SendAsync(status);
        Assert.Equal(HttpStatusCode.NotFound, statusResponse.StatusCode);
    }

    [Fact]
    public async Task Expired_inbox_is_rejected_and_then_cleaned_up()
    {
        var inbox = await GenerateAsync();
        factory.Time.Advance(TimeSpan.FromMinutes(61));
        try
        {
            using var messagesRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/temp-mail/{inbox.InboxId}/messages");
            messagesRequest.Headers.Add(TokenHeader, inbox.AccessToken);
            var messagesResponse = await _client.SendAsync(messagesRequest);
            Assert.Equal(HttpStatusCode.Gone, messagesResponse.StatusCode);
            Assert.Equal("inbox_expired", await ProblemCodeAsync(messagesResponse));

            var status = await SendAsync<InboxStatusResponse>(HttpMethod.Get, $"{inbox.InboxId}/status", inbox.AccessToken);
            Assert.True(status.IsExpired);
            Assert.Equal(0, status.SecondsRemaining);

            // After the retention period the cleanup job removes the inbox and its data.
            factory.Time.Advance(TimeSpan.FromMinutes(16));
            var cleanup = new ExpiredInboxCleanupService(
                factory.Services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new TempMailOptions { ExpiredRetentionMinutes = 15 }),
                factory.Time,
                NullLogger<ExpiredInboxCleanupService>.Instance);
            var deleted = await cleanup.CleanupOnceAsync(CancellationToken.None);
            Assert.True(deleted >= 1);

            using var afterCleanup = new HttpRequestMessage(HttpMethod.Get, $"/api/temp-mail/{inbox.InboxId}/status");
            afterCleanup.Headers.Add(TokenHeader, inbox.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(afterCleanup)).StatusCode);
        }
        finally
        {
            factory.Time.Advance(TimeSpan.FromMinutes(-77));
        }
    }

    private async Task<GenerateInboxResponse> GenerateAsync()
    {
        var response = await _client.PostAsync("/api/temp-mail/generate", content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GenerateInboxResponse>(Json))!;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativePath, string token)
    {
        using var request = new HttpRequestMessage(method, "/api/temp-mail/" + relativePath);
        request.Headers.Add(TokenHeader, token);
        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{method} {relativePath} returned {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
