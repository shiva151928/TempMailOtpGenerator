using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TempMailOtp.Api.Configuration;

namespace TempMailOtp.Api.Providers.MailTm;

/// <summary>
/// Real temporary mailboxes through the public mail.tm API (https://docs.mail.tm).
/// Each inbox is a mail.tm account with a random password; the password and bearer token are
/// returned as an opaque secret that the caller stores encrypted. No API key is required.
/// </summary>
public sealed class MailTmEmailProvider : IEmailProvider
{
    private const string LocalPartAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_";
    private const int MaxAddressAttempts = 4;
    private const string DomainsCacheKey = "mailtm:domains";

    private static readonly TimeSpan TokenCacheLifetime = TimeSpan.FromMinutes(50);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly MailTmOptions _options;
    private readonly ILogger<MailTmEmailProvider> _logger;

    public MailTmEmailProvider(
        HttpClient http,
        IMemoryCache cache,
        IOptions<EmailProviderOptions> options,
        ILogger<MailTmEmailProvider> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value.MailTm;
        _logger = logger;
    }

    public string Name => EmailProviderNames.MailTm;

    public string DisplayName => _options.DisplayName;

    public bool IsDemo => false;

    public ProviderAttribution? Attribution =>
        string.IsNullOrWhiteSpace(_options.AttributionUrl)
            ? null
            : new ProviderAttribution(_options.AttributionText, _options.AttributionUrl);

    public async Task<ProviderMailbox> CreateMailboxAsync(CancellationToken cancellationToken)
    {
        var domains = await GetDomainsAsync(cancellationToken);
        if (domains.Count == 0)
        {
            throw new EmailProviderException(
                "mail.tm has no public domains available right now. Try again later.",
                EmailProviderErrorKind.Unavailable);
        }

        for (var attempt = 1; attempt <= MaxAddressAttempts; attempt++)
        {
            var domain = domains[RandomNumberGenerator.GetInt32(domains.Count)];
            var address = $"{RandomString(LocalPartAlphabet, 12)}@{domain}";
            var password = RandomString(PasswordAlphabet, 24);

            using var response = await SendAsync(HttpMethod.Post, "accounts", new { address, password }, token: null, cancellationToken);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // 422: the address is taken or was rejected; try another random local part.
                _logger.LogInformation("mail.tm rejected address on attempt {Attempt}; retrying with a new one.", attempt);
                continue;
            }

            await EnsureSuccessAsync(response, "create the mailbox", cancellationToken);

            using var document = await ReadJsonAsync(response, cancellationToken);
            var accountId = GetString(document.RootElement, "id")
                ?? throw InvalidResponse("mail.tm did not return an account id.");
            var createdAddress = GetString(document.RootElement, "address") ?? address;

            var token = await RequestTokenAsync(createdAddress, password, cancellationToken);
            _cache.Set(TokenCacheKey(accountId), token, TokenCacheLifetime);

            var secret = JsonSerializer.Serialize(new MailTmSecret(password, token), JsonOptions);
            _logger.LogInformation("Created mail.tm mailbox {AccountId}.", accountId);
            return new ProviderMailbox(createdAddress, accountId, secret);
        }

        throw new EmailProviderException(
            "mail.tm could not create a unique address. Try again.",
            EmailProviderErrorKind.InvalidResponse);
    }

    public async Task<IReadOnlyList<ProviderMessageSummary>> ListMessagesAsync(
        ProviderMailbox mailbox, CancellationToken cancellationToken)
    {
        using var document = await GetAuthorizedJsonAsync(mailbox, "messages?page=1", "list messages", cancellationToken);

        var messages = new List<ProviderMessageSummary>();
        foreach (var item in EnumerateCollection(document.RootElement))
        {
            if (GetBool(item, "isDeleted") == true)
            {
                continue;
            }

            var id = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var (fromAddress, fromName) = GetFrom(item);
            messages.Add(new ProviderMessageSummary(
                id,
                fromAddress,
                fromName,
                GetString(item, "subject") ?? string.Empty,
                GetString(item, "intro") ?? string.Empty,
                GetDate(item, "createdAt"),
                GetBool(item, "hasAttachments") ?? false));
        }

        return messages;
    }

    public async Task<ProviderMessage> GetMessageAsync(
        ProviderMailbox mailbox, string providerMessageId, CancellationToken cancellationToken)
    {
        var path = "messages/" + Uri.EscapeDataString(providerMessageId);
        using var document = await GetAuthorizedJsonAsync(mailbox, path, "read a message", cancellationToken);
        var item = document.RootElement;

        var (fromAddress, fromName) = GetFrom(item);
        return new ProviderMessage(
            GetString(item, "id") ?? providerMessageId,
            fromAddress,
            fromName,
            GetString(item, "subject") ?? string.Empty,
            GetString(item, "intro") ?? string.Empty,
            GetDate(item, "createdAt"),
            GetBool(item, "hasAttachments") ?? false,
            GetString(item, "text"),
            GetHtml(item));
    }

    public async Task DeleteMailboxAsync(ProviderMailbox mailbox, CancellationToken cancellationToken)
    {
        var path = "accounts/" + Uri.EscapeDataString(mailbox.AccountId);
        using var response = await SendAuthorizedAsync(mailbox, HttpMethod.Delete, path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return; // Already gone.
        }

        await EnsureSuccessAsync(response, "delete the mailbox", cancellationToken);
        _cache.Remove(TokenCacheKey(mailbox.AccountId));
    }

    private async Task<IReadOnlyList<string>> GetDomainsAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(DomainsCacheKey, out IReadOnlyList<string>? cached) && cached is { Count: > 0 })
        {
            return cached;
        }

        using var response = await SendAsync(HttpMethod.Get, "domains?page=1", body: null, token: null, cancellationToken);
        await EnsureSuccessAsync(response, "list available domains", cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);

        var domains = new List<string>();
        foreach (var item in EnumerateCollection(document.RootElement))
        {
            var domain = GetString(item, "domain");
            var isActive = GetBool(item, "isActive") ?? true;
            var isPrivate = GetBool(item, "isPrivate") ?? false;
            if (!string.IsNullOrWhiteSpace(domain) && isActive && !isPrivate)
            {
                domains.Add(domain.Trim());
            }
        }

        if (domains.Count > 0)
        {
            _cache.Set(DomainsCacheKey, (IReadOnlyList<string>)domains, TimeSpan.FromMinutes(Math.Max(1, _options.DomainCacheMinutes)));
        }

        return domains;
    }

    private async Task<string> RequestTokenAsync(string address, string password, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "token", new { address, password }, token: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new EmailProviderException(
                "mail.tm rejected the stored mailbox credentials.",
                EmailProviderErrorKind.CredentialsUnavailable);
        }

        await EnsureSuccessAsync(response, "sign in to the mailbox", cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        return GetString(document.RootElement, "token")
            ?? throw InvalidResponse("mail.tm did not return a token.");
    }

    private async Task<JsonDocument> GetAuthorizedJsonAsync(
        ProviderMailbox mailbox, string path, string action, CancellationToken cancellationToken)
    {
        using var response = await SendAuthorizedAsync(mailbox, HttpMethod.Get, path, cancellationToken);
        await EnsureSuccessAsync(response, action, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    /// <summary>Sends an authenticated request, signing in again once if the cached token was rejected.</summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        ProviderMailbox mailbox, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var secret = ParseSecret(mailbox);
        var token = _cache.TryGetValue(TokenCacheKey(mailbox.AccountId), out string? cachedToken) && !string.IsNullOrEmpty(cachedToken)
            ? cachedToken
            : secret.Token;

        if (string.IsNullOrEmpty(token))
        {
            token = await RefreshTokenAsync(mailbox, secret, cancellationToken);
        }

        var response = await SendAsync(method, path, body: null, token, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        token = await RefreshTokenAsync(mailbox, secret, cancellationToken);
        response = await SendAsync(method, path, body: null, token, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new EmailProviderException(
                "mail.tm rejected the mailbox credentials.",
                EmailProviderErrorKind.CredentialsUnavailable);
        }

        return response;
    }

    private async Task<string> RefreshTokenAsync(ProviderMailbox mailbox, MailTmSecret secret, CancellationToken cancellationToken)
    {
        var token = await RequestTokenAsync(mailbox.Address, secret.Password, cancellationToken);
        _cache.Set(TokenCacheKey(mailbox.AccountId), token, TokenCacheLifetime);
        return token;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, string? token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new EmailProviderException(
                "mail.tm could not be reached. Check the server's internet connection and try again.",
                EmailProviderErrorKind.Unavailable,
                ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmailProviderException(
                "mail.tm did not respond in time. Try again.",
                EmailProviderErrorKind.Unavailable,
                ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            body = string.Empty;
        }

        var technical = new HttpRequestException(
            $"mail.tm returned HTTP {status} while trying to {action}: {Truncate(body, 500)}",
            inner: null,
            statusCode: response.StatusCode);

        throw response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => new EmailProviderException(
                "mail.tm is rate-limiting requests. Wait a few seconds and try again.",
                EmailProviderErrorKind.RateLimited,
                technical)
            {
                RetryAfter = GetRetryAfter(response) ?? TimeSpan.FromSeconds(5),
            },
            HttpStatusCode.NotFound => new EmailProviderException(
                "The mailbox or message no longer exists at mail.tm.",
                EmailProviderErrorKind.MailboxNotFound,
                technical),
            _ when status >= 500 => new EmailProviderException(
                $"mail.tm is having problems (HTTP {status}). Try again shortly.",
                EmailProviderErrorKind.Unavailable,
                technical),
            _ => new EmailProviderException(
                $"mail.tm could not {action} (HTTP {status}).",
                EmailProviderErrorKind.InvalidResponse,
                technical),
        };
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new EmailProviderException(
                "mail.tm returned a response that could not be read.",
                EmailProviderErrorKind.InvalidResponse,
                ex);
        }
    }

    private static MailTmSecret ParseSecret(ProviderMailbox mailbox)
    {
        if (!string.IsNullOrWhiteSpace(mailbox.Secret))
        {
            try
            {
                var secret = JsonSerializer.Deserialize<MailTmSecret>(mailbox.Secret, JsonOptions);
                if (secret is not null && !string.IsNullOrEmpty(secret.Password))
                {
                    return secret;
                }
            }
            catch (JsonException)
            {
                // Fall through to the error below.
            }
        }

        throw new EmailProviderException(
            "The stored credentials for this inbox are missing or unreadable.",
            EmailProviderErrorKind.CredentialsUnavailable);
    }

    /// <summary>Accepts both plain JSON arrays and Hydra/JSON-LD collections.</summary>
    private static IEnumerable<JsonElement> EnumerateCollection(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "hydra:member", "member" })
            {
                if (root.TryGetProperty(name, out var members) && members.ValueKind == JsonValueKind.Array)
                {
                    return members.EnumerateArray();
                }
            }
        }

        throw InvalidResponse("mail.tm returned an unexpected collection format.");
    }

    private static (string Address, string? Name) GetFrom(JsonElement item)
    {
        if (item.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object)
        {
            var name = GetString(from, "name");
            return (GetString(from, "address") ?? "unknown", string.IsNullOrWhiteSpace(name) ? null : name);
        }

        return ("unknown", null);
    }

    /// <summary>mail.tm returns "html" as an array of strings; a single string is also accepted.</summary>
    private static string? GetHtml(JsonElement item)
    {
        if (!item.TryGetProperty("html", out var html))
        {
            return null;
        }

        switch (html.ValueKind)
        {
            case JsonValueKind.String:
                return html.GetString();
            case JsonValueKind.Array:
                var builder = new StringBuilder();
                foreach (var part in html.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(part.GetString());
                    }
                }

                return builder.Length == 0 ? null : builder.ToString();
            default:
                return null;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static DateTime GetDate(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        return raw is not null
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : DateTime.UtcNow;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1);
        }

        return null;
    }

    private static string RandomString(string alphabet, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(chars);
    }

    private static string TokenCacheKey(string accountId) => "mailtm:token:" + accountId;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static EmailProviderException InvalidResponse(string message) =>
        new(message, EmailProviderErrorKind.InvalidResponse);
}

/// <summary>Per-mailbox mail.tm credentials. Serialized to JSON and stored encrypted with Data Protection.</summary>
internal sealed record MailTmSecret(string Password, string? Token);
