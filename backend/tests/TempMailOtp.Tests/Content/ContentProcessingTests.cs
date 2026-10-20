using TempMailOtp.Api.Services.Content;

namespace TempMailOtp.Tests.Content;

public sealed class EmailHtmlSanitizerTests
{
    private readonly EmailHtmlSanitizer _sanitizer = new();

    [Fact]
    public void Removes_scripts_and_event_handlers()
    {
        var html = _sanitizer.Sanitize(
            "<p onclick=\"steal()\">Hello</p><script>alert(1)</script><img src=\"x\" onerror=\"alert(1)\">");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", html);
    }

    [Fact]
    public void Removes_javascript_links_and_hardens_safe_links()
    {
        var html = _sanitizer.Sanitize(
            "<a href=\"javascript:alert(1)\">bad</a> <a href=\"https://example.com/path\">good</a>");

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"https://example.com/path\"", html);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html);
        Assert.Contains("target=\"_blank\"", html);
    }

    [Fact]
    public void Blocks_remote_images()
    {
        var html = _sanitizer.Sanitize("<img src=\"https://tracker.example/pixel.gif\" alt=\"Company logo\">");

        Assert.DoesNotContain("tracker.example", html);
        Assert.Contains("Company logo", html);
    }

    [Fact]
    public void Removes_forms_and_frames_but_keeps_text()
    {
        var html = _sanitizer.Sanitize(
            "<form action=\"https://evil.example\"><input name=\"password\"></form>" +
            "<iframe src=\"https://evil.example\"></iframe><p>Your code is 482917</p>");

        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<input", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("482917", html);
    }

    [Fact]
    public void Returns_empty_string_for_empty_input()
    {
        Assert.Equal(string.Empty, _sanitizer.Sanitize(null));
        Assert.Equal(string.Empty, _sanitizer.Sanitize("   "));
    }
}

public sealed class HtmlTextExtractorTests
{
    [Fact]
    public void Converts_blocks_to_lines_and_skips_scripts_and_styles()
    {
        var text = HtmlTextExtractor.ToPlainText(
            "<html><head><style>p { color: red }</style><title>Title</title></head>" +
            "<body><p>Hello</p><div>Your code</div><script>var secret = 1;</script><p>4821</p></body></html>");

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { "Hello", "Your code", "4821" }, lines);
    }

    [Fact]
    public void Keeps_one_digit_per_cell_codes_on_one_line()
    {
        var text = HtmlTextExtractor.ToPlainText(
            "<table><tr><td>4</td><td>8</td><td>2</td><td>9</td></tr></table>");

        Assert.Equal("4 8 2 9", text);
    }

    [Fact]
    public void Decodes_entities_and_line_breaks()
    {
        var text = HtmlTextExtractor.ToPlainText("Code:&nbsp;482917<br>Expires&nbsp;soon &amp; safely");

        Assert.Equal("Code: 482917\nExpires soon & safely", text);
    }
}

public sealed class TextNormalizerTests
{
    [Fact]
    public void Removes_zero_width_characters_and_collapses_whitespace()
    {
        var text = TextNormalizer.Normalize("Code:  4​8​2\r\n\r\n\r\n\r\nNext   line\t ");

        Assert.Equal("Code: 482\n\nNext line", text);
    }

    [Fact]
    public void Builds_single_line_preview()
    {
        var preview = TextNormalizer.ToPreview("First line\nSecond line " + new string('x', 300), 40);

        Assert.True(preview.Length <= 40);
        Assert.StartsWith("First line Second line", preview);
        Assert.EndsWith("…", preview);
    }
}
