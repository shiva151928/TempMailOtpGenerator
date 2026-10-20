# OTP detection harness

`detector.mjs` is a line-for-line JavaScript port of
`backend/src/TempMailOtp.Api/Services/Otp/OtpDetector.cs` (same regular expressions, weights
and thresholds). It exists so the heuristic can be exercised without the .NET SDK:

```bash
node tools/otp-harness/run.mjs
```

`cases.json` holds the shared test cases; the xUnit suite
(`backend/tests/TempMailOtp.Tests/Otp/OtpDetectorTests.cs`) contains the same cases.
The C# tests are the source of truth. One known difference: the harness uses a simplified
regex-based HTML-to-text step instead of AngleSharp, so HTML cases are approximations.
If you change the detector, update both or regenerate the xUnit data from `cases.json`.
