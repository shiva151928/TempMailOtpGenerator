using System.Globalization;
using System.Text.RegularExpressions;
using TempMailOtp.Api.Services.Content;

namespace TempMailOtp.Api.Services.Otp;

/// <summary>Raw email content handed to the detector. HTML is converted to text internally.</summary>
public sealed record EmailContent(string? Subject, string? TextBody, string? HtmlBody);

/// <param name="Code">Digits only, 4 to 8 characters.</param>
/// <param name="Confidence">Heuristic confidence between 0 and 1.</param>
/// <param name="Source">"subject" or "body".</param>
/// <param name="MatchedPhrase">Verification phrase nearest to the code, if any.</param>
/// <param name="Context">A short excerpt around the code, for diagnostics.</param>
public sealed record OtpDetectionResult(string Code, double Confidence, string Source, string? MatchedPhrase, string Context);

public interface IOtpDetector
{
    /// <summary>Finds the most likely OTP in an email, or returns null when there is no confident match.</summary>
    OtpDetectionResult? Detect(EmailContent content);
}

/// <summary>
/// Heuristic OTP detector. It finds 4–8 digit candidates (plus "123 456" / "123-456" and
/// one-digit-per-cell "1 2 3 4 5 6" layouts), scores each by its distance to verification
/// phrases and nearby cues, and rejects dates, years, phone numbers, amounts, order/reference
/// numbers, units and promo codes. Links in the email are removed before analysis and never followed.
/// </summary>
public sealed class OtpDetector : IOtpDetector
{
    public const string SourceSubject = "subject";
    public const string SourceBody = "body";

    private const int MaxAnalyzedLength = 20_000;
    private const double ProximityRange = 160;
    private const double KeywordMaxPoints = 60;
    private const double SubjectContextPoints = 20;
    private const double AcceptWithStrongKeyword = 45;
    private const double AcceptWithoutStrongKeyword = 55;
    private const double MinKeywordScoreForGroupedCodes = 30;
    private const double MinKeywordScoreForSpacedDigits = 35;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private enum CandidateKind
    {
        Plain,
        Grouped,
        SpacedDigits,
    }

    private sealed record Keyword(Regex Pattern, double Weight, bool Strong, string Label);

    private readonly record struct KeywordHit(Keyword Keyword, int Start, int End);

    private sealed record Candidate(string Code, double Score, string Source, string? Phrase, int Index, string Context);

    private static readonly Keyword[] Keywords =
    [
        new(Rx(@"\bverification\s+(?:code|pin|number)\b", ignoreCase: true), 1.0, true, "verification code"),
        new(Rx(@"\bone[\s-]?time\s+(?:password|passcode|pass\s?code|pin|code)\b", ignoreCase: true), 1.0, true, "one-time password"),
        new(Rx(@"\botp\b", ignoreCase: true), 1.0, true, "OTP"),
        new(Rx(@"\bsecurity\s+(?:code|pin)\b", ignoreCase: true), 1.0, true, "security code"),
        new(Rx(@"\bpass\s?code\b", ignoreCase: true), 0.95, true, "passcode"),
        new(Rx(@"\b(?:log[\s-]?in|sign[\s-]?in)\s+code\b", ignoreCase: true), 0.95, true, "login code"),
        new(Rx(@"\b(?:authentication|auth|authorization)\s+code\b", ignoreCase: true), 0.95, true, "authentication code"),
        new(Rx(@"\b(?:confirmation|activation|validation|access|approval)\s+code\b", ignoreCase: true), 0.9, true, "confirmation code"),
        new(Rx(@"\b(?:2fa|mfa|two[\s-]?factor|two[\s-]?step|2[\s-]?step)\b", ignoreCase: true), 0.9, true, "two-factor"),
        new(Rx(@"\bverification\b", ignoreCase: true), 0.85, true, "verification"),
        new(Rx(@"\bverify(?:ing)?\b", ignoreCase: true), 0.8, false, "verify"),
        new(Rx(@"\bcode\b", ignoreCase: true), 0.7, false, "code"),
        new(Rx(@"\bpin\b", ignoreCase: true), 0.7, false, "PIN"),
        new(Rx(@"\bconfirm(?:ation)?\b", ignoreCase: true), 0.5, false, "confirm"),
    ];

    // Candidate patterns. Boundaries reject digits that are part of words, amounts ($1234),
    // hashtags/order numbers (#1234), phone prefixes (+44), paths, decimals (12.3456),
    // times (12:3456), dates (20/10/2026, 2026-10-20) and longer digit runs.
    private static readonly Regex PlainCandidate = Rx(
        @"(?<![\p{L}\p{N}_$€£¥₹#+@&/\\=])(?<![\p{N}][.,:\-/])([0-9]{4,8})(?![\p{L}\p{N}_@%$€£¥₹])(?![.,:\-/][\p{N}])");

    private static readonly Regex GroupedCandidate = Rx(
        @"(?<![\p{L}\p{N}_$€£¥₹#+@&/\\=])(?<![\p{N}][.,:\-/\s])(?<!\)\s?)([0-9]{3,4})([ \-])([0-9]{3,4})(?![\p{L}\p{N}_@%$€£¥₹])(?![.,:\-/\s]?[\p{N}])");

    private static readonly Regex SpacedDigitsCandidate = Rx(
        @"(?<![\p{L}\p{N}_])(?<![\p{N}] )((?:[0-9] ){3,7}[0-9])(?![\p{L}\p{N}_])(?! [0-9])");

    // Pre-processing: links and email addresses are removed so their digits never count.
    private static readonly Regex UrlPattern = Rx(@"(?:https?://|www\.)\S+", ignoreCase: true);
    private static readonly Regex EmailAddressPattern = Rx(@"[\p{L}\p{N}._%+\-]+@[\p{L}\p{N}.\-]+\.\p{L}{2,}");

    // Context cues, evaluated on up to 40 characters before/after the candidate.
    private static readonly Regex ValueCueBefore = Rx(@"(?:\bis|[:：=])\s*$", ignoreCase: true);
    private static readonly Regex ActionBefore = Rx(@"\b(?:enter|type|input|paste)\b[^\n.]{0,25}$", ignoreCase: true);
    private static readonly Regex CueAfter = Rx(@"^\s*(?:is|was)\s+(?:your|the)\b", ignoreCase: true);

    private static readonly Regex NegativeBefore = Rx(
        @"(?:\b(?:order|invoice|receipt|tracking|ticket|ref|reference|transaction|txn|booking|reservation|serial|sku|zip|postal|pin\s?code|suite|apt|flight|phone|mobile|cell|tel|telephone|fax|call|whatsapp|amount|total|price|balance|paid|usd|inr|eur|gbp|aud|cad|rs|qty|quantity|points|miles|version|build|ext|extension|year|fy|batch|lot|employee|student|roll|policy|claim)\b\.?\s*(?:no\.?|number|num|#|id)?\s*[:#\-]?\s*$)" +
        @"|(?:\b(?:account|acct|customer|member|membership|user|client|application|registration|request|session|item|model|part|unit|room|box|route|emp)\s*(?:no\.?|number|num|#|id)\s*[:#\-]?\s*$)" +
        @"|(?:\b(?:id|no\.|number|num)\s*[:#\-]?\s*$)" +
        @"|(?:\b(?:ending|ends)\s+(?:in|with)\s*$)" +
        @"|(?:[x*•]{2,}\s*$)" +
        @"|(?:\(\s*[0-9]{2,4}\s*\)\s*$)" +
        @"|(?:\+\s?[0-9]{1,3}[\s.\-]?$)",
        ignoreCase: true);

    private static readonly Regex NegativeAfter = Rx(
        @"^\s?(?:%|(?:percent|km|mi|kg|lbs?|g|mb|gb|kb|tb|px|pt|pts|points|miles|usd|inr|eur|gbp|dollars?|rupees?|rs|items?|users?|people|views|followers|likes|times|years?|yrs|months?|days?|hours?|hrs|minutes?|mins?|seconds?|secs?|am|pm|calories|kcal|steps)\b)" +
        @"|^\s\p{Lu}\p{L}+\s+(?:street|st|avenue|ave|road|rd|boulevard|blvd|lane|ln|drive|dr|parkway|pkwy|way|court|ct|place|pl)\b",
        ignoreCase: true);

    private static readonly Regex AntiKeywordBefore = Rx(
        @"\b(?:promo(?:tion(?:al)?)?|coupon|discount|voucher|gift\s*card|referral|offer|reward|tracking|order|invoice|reference|booking)\s+(?:code|number|no\.?|#)",
        ignoreCase: true);

    private static readonly Regex DigitSpaceBefore = Rx(@"[0-9] $");
    private static readonly Regex SpaceDigitAfter = Rx(@"^ [0-9]");

    private static readonly Regex DateContext = Rx(
        @"\b(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?|mon(?:day)?|tue(?:s(?:day)?)?|wed(?:nesday)?|thu(?:rs(?:day)?)?|fri(?:day)?|saturday|sunday|years?|fy|since|copyright|rights\s+reserved|established|founded)\b|©|\(c\)",
        ignoreCase: true);

    private static readonly Regex DateBefore = Rx(@"(?:\bmay|\bq[1-4]|\b[0-9]{1,2}(?:st|nd|rd|th)?,?)\s*$", ignoreCase: true);

    public OtpDetectionResult? Detect(EmailContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            return DetectCore(content);
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological input: treat as "no OTP found" rather than failing the sync.
            return null;
        }
    }

    private static OtpDetectionResult? DetectCore(EmailContent content)
    {
        var segments = new List<(string Text, string Source)>(3);

        var subject = Prepare(content.Subject);
        if (subject.Length > 0)
        {
            segments.Add((subject, SourceSubject));
        }

        var text = Prepare(content.TextBody);
        if (text.Length > 0)
        {
            segments.Add((text, SourceBody));
        }

        if (!string.IsNullOrWhiteSpace(content.HtmlBody))
        {
            var htmlText = Prepare(HtmlTextExtractor.ToPlainText(content.HtmlBody));
            if (htmlText.Length > 0 && !string.Equals(htmlText, text, StringComparison.Ordinal))
            {
                segments.Add((htmlText, SourceBody));
            }
        }

        if (segments.Count == 0)
        {
            return null;
        }

        var hitsPerSegment = segments.Select(segment => FindKeywords(segment.Text)).ToList();
        if (hitsPerSegment.All(hits => hits.Count == 0))
        {
            return null;
        }

        var strongKeywordPresent = hitsPerSegment.Any(hits => hits.Any(hit => hit.Keyword.Strong));

        // Keywords in the subject give weak context to codes that stand alone in the body.
        KeywordHit? bestSubjectHit = null;
        if (segments[0].Source == SourceSubject && hitsPerSegment[0].Count > 0)
        {
            bestSubjectHit = hitsPerSegment[0].MaxBy(hit => hit.Keyword.Weight);
        }

        var bestByCode = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var sourcesByCode = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        for (var i = 0; i < segments.Count; i++)
        {
            var (segmentText, source) = segments[i];
            var subjectContext = source == SourceBody ? bestSubjectHit : null;

            foreach (var candidate in FindCandidates(segmentText, source, hitsPerSegment[i], subjectContext))
            {
                if (!sourcesByCode.TryGetValue(candidate.Code, out var sources))
                {
                    sources = new HashSet<string>(StringComparer.Ordinal);
                    sourcesByCode[candidate.Code] = sources;
                }

                sources.Add(candidate.Source);

                if (!bestByCode.TryGetValue(candidate.Code, out var existing) || candidate.Score > existing.Score)
                {
                    bestByCode[candidate.Code] = candidate;
                }
            }
        }

        if (bestByCode.Count == 0)
        {
            return null;
        }

        var ranked = bestByCode.Values
            .Select(candidate => sourcesByCode[candidate.Code].Count > 1
                ? candidate with { Score = candidate.Score + 6 } // same code in subject and body
                : candidate)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Source == SourceSubject ? 0 : 1)
            .ThenBy(candidate => candidate.Index)
            .ToList();

        var winner = ranked[0];
        var threshold = strongKeywordPresent ? AcceptWithStrongKeyword : AcceptWithoutStrongKeyword;
        if (winner.Score < threshold)
        {
            return null;
        }

        var confidence = Math.Round(Math.Clamp(winner.Score / 90.0, 0, 1), 2);
        return new OtpDetectionResult(winner.Code, confidence, winner.Source, winner.Phrase, winner.Context);
    }

    private static IEnumerable<Candidate> FindCandidates(
        string text, string source, IReadOnlyList<KeywordHit> hits, KeywordHit? subjectContext)
    {
        foreach (Match match in PlainCandidate.Matches(text))
        {
            var candidate = Score(text, match.Index, match.Length, match.Groups[1].Value, CandidateKind.Plain, source, hits, subjectContext);
            if (candidate is not null)
            {
                yield return candidate;
            }
        }

        foreach (Match match in GroupedCandidate.Matches(text))
        {
            var code = match.Groups[1].Value + match.Groups[3].Value;
            if (code.Length is < 6 or > 8)
            {
                continue;
            }

            var candidate = Score(text, match.Index, match.Length, code, CandidateKind.Grouped, source, hits, subjectContext);
            if (candidate is not null)
            {
                yield return candidate;
            }
        }

        foreach (Match match in SpacedDigitsCandidate.Matches(text))
        {
            var code = match.Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
            var candidate = Score(text, match.Index, match.Length, code, CandidateKind.SpacedDigits, source, hits, subjectContext);
            if (candidate is not null)
            {
                yield return candidate;
            }
        }
    }

    private static Candidate? Score(
        string text,
        int start,
        int length,
        string code,
        CandidateKind kind,
        string source,
        IReadOnlyList<KeywordHit> hits,
        KeywordHit? subjectContext)
    {
        var end = start + length;

        // 1. Proximity to verification phrases (codes usually follow the phrase, sometimes precede it).
        double keywordScore = 0;
        string? phrase = null;
        foreach (var hit in hits)
        {
            int distance;
            double direction;
            if (hit.End <= start)
            {
                distance = start - hit.End;
                direction = 1.0;
            }
            else if (hit.Start >= end)
            {
                distance = hit.Start - end;
                direction = 0.85;
            }
            else
            {
                continue;
            }

            if (distance > ProximityRange)
            {
                continue;
            }

            var points = KeywordMaxPoints * hit.Keyword.Weight * (1 - (distance / ProximityRange)) * direction;
            if (points > keywordScore)
            {
                keywordScore = points;
                phrase = hit.Keyword.Label;
            }
        }

        if (subjectContext is { } subjectHit)
        {
            var subjectPoints = SubjectContextPoints * subjectHit.Keyword.Weight;
            if (subjectPoints > keywordScore)
            {
                keywordScore = subjectPoints;
                phrase = subjectHit.Keyword.Label;
            }
        }

        if (kind == CandidateKind.Grouped && keywordScore < MinKeywordScoreForGroupedCodes)
        {
            return null;
        }

        if (kind == CandidateKind.SpacedDigits && keywordScore < MinKeywordScoreForSpacedDigits)
        {
            return null;
        }

        var beforeStart = Math.Max(0, start - 40);
        var before = text[beforeStart..start];
        var after = text[end..Math.Min(text.Length, end + 40)];

        // 2. Years and dates are never codes when date words are nearby.
        if (code.Length == 4
            && int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1900 and <= 2099)
        {
            var window = text[Math.Max(0, start - 30)..Math.Min(text.Length, end + 30)];
            if (DateContext.IsMatch(window) || DateBefore.IsMatch(before))
            {
                return null;
            }

            keywordScore -= 12;
        }

        var score = keywordScore + LengthBonus(code.Length);

        // 3. Positive layout and phrasing cues.
        if (IsStandaloneLine(text, start, end))
        {
            score += 20;
        }

        if (ValueCueBefore.IsMatch(before))
        {
            score += 10;
        }

        if (CueAfter.IsMatch(after))
        {
            score += 15;
        }

        if (ActionBefore.IsMatch(before))
        {
            score += 10;
        }

        if (source == SourceSubject)
        {
            score += 5;
        }

        // 4. Negative cues: identifiers, money, units, phone groups, promo codes.
        if (NegativeBefore.IsMatch(before))
        {
            score -= 45;
        }

        if (NegativeAfter.IsMatch(after))
        {
            score -= 40;
        }

        if (AntiKeywordBefore.IsMatch(before))
        {
            score -= 40;
        }

        if (kind == CandidateKind.Plain && (DigitSpaceBefore.IsMatch(before) || SpaceDigitAfter.IsMatch(after)))
        {
            score -= 50;
        }

        return new Candidate(code, score, source, phrase, start, BuildContext(text, start, end));
    }

    private static double LengthBonus(int length) => length switch
    {
        6 => 12,
        4 or 5 or 8 => 6,
        7 => 4,
        _ => 0,
    };

    private static bool IsStandaloneLine(string text, int start, int end)
    {
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var line = text[lineStart..lineEnd].Trim().TrimEnd('.', '!', '。');
        return line.Length == end - start;
    }

    private static string BuildContext(string text, int start, int end)
    {
        var from = Math.Max(0, start - 40);
        var to = Math.Min(text.Length, end + 40);
        return text[from..to].Replace('\n', ' ').Trim();
    }

    private static List<KeywordHit> FindKeywords(string text)
    {
        var hits = new List<KeywordHit>();
        foreach (var keyword in Keywords)
        {
            foreach (Match match in keyword.Pattern.Matches(text))
            {
                hits.Add(new KeywordHit(keyword, match.Index, match.Index + match.Length));
            }
        }

        return hits;
    }

    private static string Prepare(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Length > MaxAnalyzedLength)
        {
            text = text[..MaxAnalyzedLength];
        }

        var normalized = TextNormalizer.Normalize(text);
        normalized = UrlPattern.Replace(normalized, " ");
        normalized = EmailAddressPattern.Replace(normalized, " ");
        return normalized;
    }

    private static Regex Rx(string pattern, bool ignoreCase = false)
    {
        var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(pattern, options, RegexTimeout);
    }
}
