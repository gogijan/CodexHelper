using CodexHelper.Services;

namespace CodexHelper.Tests;

[TestClass]
public sealed class DiagnosticLogServiceTests
{
    [TestMethod]
    public void RedactSensitiveData_RemovesCommonSecrets()
    {
        const string text = """
            Authorization: Bearer abc.def-123
            {"api_key":"sk-testsecret123456","refresh_token":"refresh-secret","password":"pass"}
            token=plain-token secret='quoted-secret' github_pat_1234567890abcdef
            """;

        var redacted = DiagnosticLogService.RedactSensitiveData(text);

        StringAssert.Contains(redacted, "Bearer [redacted]");
        StringAssert.Contains(redacted, "\"api_key\":\"[redacted]\"");
        StringAssert.Contains(redacted, "\"refresh_token\":\"[redacted]\"");
        StringAssert.Contains(redacted, "\"password\":\"[redacted]\"");
        StringAssert.Contains(redacted, "secret='[redacted]'");
        StringAssert.Contains(redacted, "[redacted-github-token]");
        Assert.IsFalse(redacted.Contains("sk-testsecret123456", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("plain-token", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("quoted-secret", StringComparison.Ordinal));
    }
}
