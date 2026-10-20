// Test cases are shared with the offline JavaScript harness used during development (tools/otp-harness).
using TempMailOtp.Api.Services.Otp;

namespace TempMailOtp.Tests.Otp;

public sealed class OtpDetectorTests
{
    private readonly OtpDetector _detector = new();

    public static TheoryData<string, string?, string?, string?, string> CodeEmails => new()
    {
        { "phrase before code", "Your verification code", "Your verification code is 482917. It expires in 10 minutes.", null, "482917" },
        { "code first in subject", "482917 is your Instagram code", "", null, "482917" },
        { "standalone line", "Sign in to Acme", "Use the code below to sign in.\n\n731905\n\nThis code expires in 15 minutes. If you didn't request it, ignore this email.", null, "731905" },
        { "one-time password 4 digits", "Your one-time password", "Dear customer, your One-Time Password (OTP) for login is 5823. Do not share it with anyone.", null, "5823" },
        { "microsoft style", "Microsoft account security code", "Microsoft account\nSecurity code\nPlease use the following security code for the Microsoft account ab***@example.com.\nSecurity code: 4729\nIf you don't recognize the Microsoft account ab***@example.com, you can click here to remove your email address from that account.", null, "4729" },
        { "enter code to verify", "Verify your email", "Enter 482917 to verify your email address.", null, "482917" },
        { "grouped with space", "Your login code", "Your login code: 482 917", null, "482917" },
        { "grouped with hyphen", "Security code", "Your security code is 482-917.", null, "482917" },
        { "bank otp with amount", "Transaction OTP", "OTP for txn of INR 5,000.00 at AMAZON is 123456. Valid for 10 mins. Do not share.", null, "123456" },
        { "phone number nearby", "Your code", "Call us at 1800 123 4567 for help. Your OTP is 4821.", null, "4821" },
        { "html table with footer", "Confirm your account", null, "<html><head><style>.x{color:red}</style></head><body><table><tr><td><p>Your verification code</p></td></tr><tr><td style='font-size:32px'>604918</td></tr><tr><td>&copy; 2026 Acme Inc, 1600 Main Street, CA 94043</td></tr></table></body></html>", "604918" },
        { "zero width spaces", "Code", "Your verification code is 4\u200b8\u200b2\u200b9\u200b1\u200b7", null, "482917" },
        { "one digit per cell", "Verify", null, "<p>Your verification code:</p><table><tr><td>4</td><td>8</td><td>2</td><td>9</td><td>1</td><td>7</td></tr></table><p>It expires soon.</p>", "482917" },
        { "code before phrase in body", "", "123456 is your verification code for Acme.", null, "123456" },
        { "google prefix", "", "G-482917 is your Google verification code.", null, "482917" },
        { "apple id code", "Your Apple ID Code is: 482917.", "Your Apple ID Code is: 482917. Don't share it with anyone.", null, "482917" },
        { "eight digits", "Your verification code", "Your verification code is 48291735", null, "48291735" },
        { "pin medium keyword", "Hello", "Your PIN is 4821", null, "4821" },
        { "reference and phone distractors", "Verify", "Your verification code is 482917. Reference number: 99887766. Call 1-800-555-0199.", null, "482917" },
        { "year-like code with strong phrase", "OTP", "Your OTP is 2024", null, "2024" },
        { "order distractor before code", "Verify", "Your verification code for order 12345 is 482917.", null, "482917" },
        { "passcode", "Passcode", "Here is your passcode: 90817", null, "90817" },
        { "2fa code html", "Two-factor authentication", null, "<div>Your two-factor authentication code</div><div><strong>338120</strong></div><div>Expires in 5 minutes</div>", "338120" },
        { "otp before phrase with amount", "OTP", "Dear Customer, 482917 is the OTP for your transaction of INR 2,500.00 at Flipkart. OTP valid for 10 mins.", null, "482917" },
        { "code with colon", "Uber", "Your Uber code: 4821. Never share this code.", null, "4821" },
        { "use code as verification code", "", "Use 482917 as your verification code.", null, "482917" },
        { "sign-in code on own line", "Your Netflix sign-in code", "Enter this code to sign in\n4829\nThis code expires in 15 minutes", null, "4829" },
        { "html with address footer", "Welcome! Confirm your email", null, "<h1>Welcome aboard</h1><p>Confirm your email with this code:</p><h2>482917</h2><p>Questions? Call 1-800-555-0199. Acme, 500 Howard St, San Francisco, CA 94105.</p>", "482917" },
    };

    public static TheoryData<string, string?, string?, string?> NonCodeEmails => new()
    {
        { "newsletter", "October deals", "Save 20% on orders over $50 until Oct 31, 2026. Visit our store at 1600 Main Street, Springfield. Call 555-123-4567.", null },
        { "order shipped", "Order confirmation", "Thanks for your order! Order #12345678 has shipped. Tracking number: 940011189922. Confirm delivery details below.", null },
        { "date and time", "Your verification code", "Your verification code was sent on 20/10/2026 at 05:55. Please check your phone.", null },
        { "promo code", "Weekend sale", "Use promo code 4500 at checkout for $10 off your next order.", null },
        { "copyright year", "Verify your email", "Click the button to verify your email.\n\u00a9 2026 Acme Inc. All rights reserved.", null },
        { "phone numbers", "Contact", "Need help verifying? Call +1 (555) 123-4567 or +91 98765 43210.", null },
        { "amount and transaction id", "Your receipt", "Your verification is complete. Amount paid: Rs 4999. Transaction ID 55667788.", null },
        { "digits only in link", "Verify your account", "Please verify: https://example.com/verify?token=482917&id=1234", null },
        { "time units", "Your code", "Your verification link expires in 1440 minutes.", null },
        { "no keywords", "Hello", "We have 2500 new users and 1200 posts this week.", null },
        { "address far from keyword", "Your verification code", "Hi there,\n\nThanks for signing up. We will send your verification code shortly. In the meantime, take a look around, set up your profile, invite your teammates and explore the templates gallery to get started quickly with your first project.\n\nAcme Inc, 4821 Market Street, San Francisco 94107", null },
        { "card ending", "Payment", "Your verification of card ending in 4821 is complete.", null },
        { "date with month name", "Verification", "Your verification was completed on October 20, 2026.", null },
        { "account number", "Verify", "Please verify account number 48291735 before continuing.", null },
        { "order number verified", "Shipped", "Your order 482917 has been verified and shipped.", null },
        { "meeting id and passcode", "Zoom", "Join Zoom Meeting. Meeting ID: 8472 9921 0034 Passcode: abc123", null },
        { "long phone number", "Verify", "Verify your identity by calling 18001234567.", null },
        { "bonus points", "Your code", "Get 1500 bonus points when you confirm your subscription.", null },
        { "join date", "Verify your email", "Thanks for joining on 12 March 2026. Verify your email to continue.", null },
    };

    [Theory]
    [MemberData(nameof(CodeEmails))]
    public void Detects_code(string name, string? subject, string? text, string? html, string expected)
    {
        var result = _detector.Detect(new EmailContent(subject, text, html));

        Assert.True(result is not null, $"{name}: expected {expected} but no OTP was detected.");
        Assert.Equal(expected, result.Code);
        Assert.InRange(result.Confidence, 0.0, 1.0);
    }

    [Theory]
    [MemberData(nameof(NonCodeEmails))]
    public void Ignores_numbers_that_are_not_codes(string name, string? subject, string? text, string? html)
    {
        var result = _detector.Detect(new EmailContent(subject, text, html));

        Assert.True(result is null, $"{name}: expected no OTP but detected {result?.Code}.");
    }

    [Fact]
    public void Returns_null_for_empty_email()
    {
        Assert.Null(_detector.Detect(new EmailContent(null, null, null)));
        Assert.Null(_detector.Detect(new EmailContent("", "   ", "")));
    }

    [Fact]
    public void Reports_source_and_phrase()
    {
        var result = _detector.Detect(new EmailContent("Welcome", "Your verification code is 482917.", null));

        Assert.NotNull(result);
        Assert.Equal(OtpDetector.SourceBody, result.Source);
        Assert.Equal("verification code", result.MatchedPhrase);
    }

    [Fact]
    public void Ignores_digits_inside_links()
    {
        var result = _detector.Detect(new EmailContent(
            "Your verification code",
            null,
            "<p>Your verification code is <a href=\"https://example.com/v?code=111111\">here</a>.</p><p>Code: 482917</p>"));

        Assert.NotNull(result);
        Assert.Equal("482917", result.Code);
    }

    [Fact]
    public void Handles_very_large_input_without_throwing()
    {
        var noise = string.Concat(Enumerable.Repeat("Lorem ipsum 12 dolor sit amet 3.14 ", 5000));
        var result = _detector.Detect(new EmailContent("Your OTP", noise + " Your OTP is 482917", null));

        // The code sits beyond the analyzed window, so nothing (and no exception) is the correct outcome.
        Assert.Null(result);
    }
}
