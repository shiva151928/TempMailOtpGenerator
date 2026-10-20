using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace TempMailOtp.Api.Services.Content;

/// <summary>
/// Converts email HTML to plain text for OTP detection and previews.
/// Parsing is purely in-memory: no scripts run and no resources or links are fetched.
/// </summary>
public static class HtmlTextExtractor
{
    private const int MaxHtmlLength = 1_000_000;
    private const int MaxDepth = 200;

    private static readonly HashSet<string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "title", "noscript", "template", "svg", "math",
        "object", "embed", "iframe", "meta", "link",
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "caption", "center", "dd", "details", "div",
        "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4",
        "h5", "h6", "header", "hr", "li", "main", "nav", "ol", "p", "pre", "section", "summary",
        "table", "tbody", "tfoot", "thead", "tr", "ul",
    };

    /// <summary>Table cells are separated by a space so one-digit-per-cell codes stay on one line.</summary>
    private static readonly HashSet<string> CellElements = new(StringComparer.OrdinalIgnoreCase) { "td", "th" };

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        if (html.Length > MaxHtmlLength)
        {
            html = html[..MaxHtmlLength];
        }

        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        INode? root = (INode?)document.Body ?? document.DocumentElement;
        if (root is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(html.Length, 64_000));
        AppendChildren(root, builder, depth: 0);
        return TextNormalizer.Normalize(builder.ToString());
    }

    private static void AppendChildren(INode node, StringBuilder builder, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            if (child is IText text)
            {
                builder.Append(text.Data);
                continue;
            }

            if (child is not IElement element)
            {
                continue;
            }

            var name = element.LocalName;
            if (SkippedElements.Contains(name))
            {
                continue;
            }

            if (string.Equals(name, "br", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append('\n');
                continue;
            }

            var isBlock = BlockElements.Contains(name);
            var isCell = CellElements.Contains(name);
            if (isBlock)
            {
                builder.Append('\n');
            }
            else if (isCell)
            {
                builder.Append(' ');
            }

            AppendChildren(element, builder, depth + 1);

            if (isBlock)
            {
                builder.Append('\n');
            }
            else if (isCell)
            {
                builder.Append(' ');
            }
        }
    }
}
