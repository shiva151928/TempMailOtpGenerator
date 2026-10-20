using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TempMailOtp.Api.Configuration;
using TempMailOtp.Api.Providers;
using TempMailOtp.Api.Providers.MailTm;

namespace TempMailOtp.Tests.Providers;

/// <summary>
/// Exercises the mail.tm integration against recorded-shape responses (no network access).
/// </summary>
public sealed class MailTmEmailProviderTests
{
    private const string Secret = "{\"password\":\"stored-password\",\"token\":\"jwt-1\"}";
    private static readonly ProviderMailbox Mailbox = new("abc@mail.example", "acc-1", Secret);

    [Fact]
    public async Task CreateMailbox_uses_a_public_domain_and_returns_credentials()
    {
        var handler = new FakeHttpHandler((request, body) => (request.Method.Method, request.RequestUri!.AbsolutePath) switch
        {
            ("GET", "/domains") => Json(HttpStatusCode.OK,
                """{"hydra:member":[{"id":"d1","domain":"private.example","isActive":true,"isPrivate":true},{"id":"d2","domain":"mail.example","isActive":true,"isPrivate":false}]}"""),
            ("POST", "/accounts") => Json(HttpStatusCode.Created,
                $$"""{"id":"acc-1","address":"{{ReadAddress(body)}}"}"""),
            ("POST", "/token") => Json(HttpStatusCode.OK, """{"id":"acc-1","token":"jwt-1"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var mailbox = await CreateProvider(handler).CreateMailboxAsync(CancellationToken.None);

        Assert.Equal("acc-1", mailbox.AccountId);
        Assert.EndsWith("@mail.example", mailbox.Address);
        Assert.Contains("jwt-1", mailbox.Secret);

        var accountRequest = Assert.Single(handler.Requests, r => r.Path == "/accounts");
        Assert.Contains("\"password\"", accountRequest.Body);
    }

    [Fact]
    public async Task CreateMailbox_retries_when_the_address_is_taken()
    {
        var accountAttempts = 0;
        var handler = new FakeHttpHandler((request, body) => request.RequestUri!.AbsolutePath switch
        {
            "/domains" => Json(HttpStatusCode.OK, """[{"id":"d2","domain":"mail.example","isActive":true,"isPrivate":false}]"""),
            "/accounts" => ++accountAttempts == 1
                ? Json(HttpStatusCode.UnprocessableEntity, """{"detail":"address: This value is already used."}""")
                : Json(HttpStatusCode.Created, $$"""{"id":"acc-2","address":"{{ReadAddress(body)}}"}"""),
            "/token" => Json(HttpStatusCode.OK, """{"token":"jwt-2"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var mailbox = await CreateProvider(handler).CreateMailboxAsync(CancellationToken.None);

        Assert.Equal(2, accountAttempts);
        Assert.Equal("acc-2", mailbox.AccountId);
    }

    [Fact]
    public async Task ListMessages_parses_plain_arrays_and_sends_the_bearer_token()
    {
        var handler = new FakeHttpHandler((_, _) => Json(HttpStatusCode.OK, """
            [
              {"id":"m1","from":{"address":"no-reply@acme.test","name":"Acme"},"subject":"Your code",
               "intro":"Your code is 123456","createdAt":"2026-10-20T00:30:00+00:00","hasAttachments":false,"isDeleted":false},
              {"id":"m2","from":{"address":"x@y.test","name":""},"subject":"Deleted","intro":"","createdAt":"2026-10-20T00:31:00+00:00","isDeleted":true}
            ]
            """));

        var messages = await CreateProvider(handler).ListMessagesAsync(Mailbox, CancellationToken.None);

        var message = Assert.Single(messages);
        Assert.Equal("m1", message.Id);
        Assert.Equal("no-reply@acme.test", message.FromAddress);
        Assert.Equal("Acme", message.FromName);
        Assert.Equal(new DateTime(2026, 10, 20, 0, 30, 0, DateTimeKind.Utc), message.ReceivedAt);
        Assert.Equal(DateTimeKind.Utc, message.ReceivedAt.Kind);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/messages?page=1", request.PathAndQuery);
        Assert.Equal("Bearer jwt-1", request.Authorization);
    }

    [Fact]
    public async Task GetMessage_joins_html_parts()
    {
        var handler = new FakeHttpHandler((_, _) => Json(HttpStatusCode.OK, """
            {"id":"m1","from":{"address":"no-reply@acme.test","name":"Acme"},"subject":"Your code","intro":"Intro",
             "text":"Your verification code is 482917","html":["<p>Part 1</p>","<p>Part 2</p>"],
             "createdAt":"2026-10-20T00:30:00Z","hasAttachments":true}
            """));

        var message = await CreateProvider(handler).GetMessageAsync(Mailbox, "m1", CancellationToken.None);

        Assert.Equal("<p>Part 1</p><p>Part 2</p>", message.HtmlBody);
        Assert.Equal("Your verification code is 482917", message.TextBody);
        Assert.True(message.HasAttachments);
        Assert.Equal("/messages/m1", handler.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task Rejected_token_is_refreshed_once_with_the_stored_password()
    {
        var messageCalls = 0;
        var handler = new FakeHttpHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/messages" => ++messageCalls == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json(HttpStatusCode.OK, "[]"),
            "/token" => Json(HttpStatusCode.OK, """{"token":"jwt-2"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var messages = await CreateProvider(handler).ListMessagesAsync(Mailbox, CancellationToken.None);

        Assert.Empty(messages);
        Assert.Equal(new[] { "/messages", "/token", "/messages" }, handler.Requests.Select(r => r.Path));
        Assert.Contains("stored-password", handler.Requests[1].Body);
        Assert.Equal("Bearer jwt-2", handler.Requests[2].Authorization);
    }

    [Fact]
    public async Task Rate_limiting_is_reported_with_retry_after()
    {
        var handler = new FakeHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });

        var error = await Assert.ThrowsAsync<EmailProviderException>(
            () => CreateProvider(handler).ListMessagesAsync(Mailbox, CancellationToken.None));

        Assert.Equal(EmailProviderErrorKind.RateLimited, error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
    }

    [Fact]
    public async Task Server_errors_and_network_failures_are_reported_as_unavailable()
    {
        var serverError = new FakeHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var networkError = new FakeHttpHandler((_, _) => throw new HttpRequestException("connection refused"));

        var first = await Assert.ThrowsAsync<EmailProviderException>(
            () => CreateProvider(serverError).ListMessagesAsync(Mailbox, CancellationToken.None));
        var second = await Assert.ThrowsAsync<EmailProviderException>(
            () => CreateProvider(networkError).CreateMailboxAsync(CancellationToken.None));

        Assert.Equal(EmailProviderErrorKind.Unavailable, first.Kind);
        Assert.Equal(EmailProviderErrorKind.Unavailable, second.Kind);
    }

    [Fact]
    public async Task DeleteMailbox_treats_missing_account_as_deleted()
    {
        var handler = new FakeHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        await CreateProvider(handler).DeleteMailboxAsync(Mailbox, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("DELETE", request.Method);
        Assert.Equal("/accounts/acc-1", request.Path);
    }

    [Fact]
    public async Task Missing_credentials_are_reported_clearly()
    {
        var handler = new FakeHttpHandler((_, _) => Json(HttpStatusCode.OK, "[]"));
        var mailbox = Mailbox with { Secret = null };

        var error = await Assert.ThrowsAsync<EmailProviderException>(
            () => CreateProvider(handler).ListMessagesAsync(mailbox, CancellationToken.None));

        Assert.Equal(EmailProviderErrorKind.CredentialsUnavailable, error.Kind);
        Assert.Empty(handler.Requests);
    }

    private static MailTmEmailProvider CreateProvider(FakeHttpHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.mail.tm/") };
        return new MailTmEmailProvider(
            http,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new EmailProviderOptions()),
            NullLogger<MailTmEmailProvider>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string ReadAddress(string? body)
    {
        using var document = JsonDocument.Parse(body ?? "{}");
        return document.RootElement.GetProperty("address").GetString()!;
    }

    private sealed record RecordedRequest(string Method, string Path, string PathAndQuery, string? Authorization, string? Body);

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.PathAndQuery,
                request.Headers.Authorization?.ToString(),
                body));
            return responder(request, body);
        }
    }
}
