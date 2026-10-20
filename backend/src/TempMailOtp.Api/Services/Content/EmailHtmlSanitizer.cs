using AngleSharp.Dom;
using Ganss.Xss;

namespace TempMailOtp.Api.Services.Content;

public interface IEmailHtmlSanitizer
{
    /// <summary>Returns HTML that is safe to display: no scripts, event handlers, forms, frames or remote images.</summary>
    string Sanitize(string? html);
}

/// <summary>
/// Server-side sanitizer for received email HTML (allow-list based, built on HtmlSanitizer/AngleSharp).
/// The frontend additionally renders the result inside a sandboxed iframe with a restrictive CSP.
/// </summary>
public sealed class EmailHtmlSanitizer : IEmailHtmlSanitizer
{
    private const int MaxHtmlLength = 1_000_000;

    private static readonly string[] RemovedTags =
    [
        "form", "input", "button", "select", "option", "optgroup", "textarea", "datalist", "keygen",
        "output", "map", "area", "audio", "video", "source", "track", "picture", "object", "embed",
        "iframe", "frame", "frameset", "base", "link", "meta", "style", "script",
    ];

    private readonly HtmlSanitizer _sanitizer;

    public EmailHtmlSanitizer()
    {
        _sanitizer = new HtmlSanitizer();

        foreach (var tag in RemovedTags)
        {
            _sanitizer.AllowedTags.Remove(tag);
        }

        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("mailto");
        // Needed so inline data:image/* images survive; data: links are stripped in post-processing.
        _sanitizer.AllowedSchemes.Add("data");

        _sanitizer.KeepChildNodes = true;
        _sanitizer.PostProcessNode += OnPostProcessNode;
    }

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        if (html.Length > MaxHtmlLength)
        {
            html = html[..MaxHtmlLength];
        }

        return _sanitizer.Sanitize(html);
    }

    private static void OnPostProcessNode(object? sender, PostProcessNodeEventArgs e)
    {
        if (e.Node is not IElement element)
        {
            return;
        }

        switch (element.LocalName)
        {
            case "a":
            {
                var href = element.GetAttribute("href");
                if (href is not null && href.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    element.RemoveAttribute("href");
                }

                // Links are only ever opened by an explicit user click, in a new tab, without referrer.
                element.SetAttribute("target", "_blank");
                element.SetAttribute("rel", "noopener noreferrer nofollow");
                break;
            }

            case "img":
            {
                var src = element.GetAttribute("src");
                var isInlineImage = src is not null
                    && src.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);

                if (!isInlineImage)
                {
                    // Remote images are blocked: they are commonly used as tracking pixels.
                    element.RemoveAttribute("src");
                    element.RemoveAttribute("srcset");
                    var alt = element.GetAttribute("alt");
                    element.SetAttribute("alt", string.IsNullOrWhiteSpace(alt) ? "[image blocked]" : alt);
                }

                break;
            }
        }
    }
}
