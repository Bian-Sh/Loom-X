using LoomX.CredentialProtection;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 脱敏幂等性与引用保护：
/// 随机 placeholder 后缀有约 2% 概率被内置形态规则（如 IBAN）识别，
/// 顺序替换会让后一条规则在前一条刚生成的 placeholder 上再次命中，
/// 产出不可还原的嵌套引用。本组用例把该行为固化为契约。
/// </summary>
public sealed class CredentialProtectionIdempotencyTests : IDisposable
{
    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-credential-idem-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果。
        }
    }

    private CredentialEngine CreateEngine()
    {
        Directory.CreateDirectory(dataDirectory);
        return new CredentialEngine(new SensitiveRuleStore(dataDirectory));
    }

    /// <summary>
    /// 根因用例：约 2.28% 的随机后缀会被内置形态规则（如 IBAN）识别。
    /// 已签发的引用必须受保护、逐字符透传，绝不二次 token 化。
    /// 2000 次随机采样把漏检概率压到 0.977^2000 ≈ 1.5e-20。
    /// </summary>
    [Fact]
    public void Sanitize_NeverRemasksIssuedReference()
    {
        var engine = CreateEngine();
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var random = new Random(20260910);

        for (var iteration = 0; iteration < 2000; iteration++)
        {
            var suffix = new string(
                Enumerable.Range(0, 20).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var issued = "{{LOOMX_CREDENTIAL_" + suffix + "}}";

            var sanitized = engine.Sanitize(issued, out var changed, out var replacementCount);

            Assert.False(changed);
            Assert.Equal(0, replacementCount);
            Assert.Equal(issued, sanitized);
        }
    }

    /// <summary>一段明文只签发一个引用，输出永不含嵌套。</summary>
    [Fact]
    public void Sanitize_MasksSecretExactlyOnce()
    {
        var engine = CreateEngine();
        const string payload = """{"messages":[{"role":"user","content":"sk-observe-abcdefghij0123456789"}]}""";

        var sanitized = engine.Sanitize(payload, out var changed, out var replacementCount);

        Assert.True(changed);
        Assert.Equal(1, replacementCount);
        Assert.DoesNotContain("{{LOOMX_CREDENTIAL_{{", sanitized, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sanitized, CredentialEngine.PlaceholderPrefix));
    }

    /// <summary>已存在的历史引用逐字符透传，不再被二次 token 化。</summary>
    [Fact]
    public void Sanitize_LeavesExistingPlaceholderUntouched()
    {
        var engine = CreateEngine();

        var first = engine.Sanitize("sk-observe-abcdefghij0123456789", out _, out var firstCount);
        Assert.Equal(1, firstCount);

        // 模拟旧会话：入参里已带着上一轮签发的引用。
        var second = engine.Sanitize(first, out var changed, out var secondCount);

        Assert.False(changed);
        Assert.Equal(0, secondCount);
        Assert.Equal(first, second);
    }

    /// <summary>嵌套防护同样覆盖纯文本路径（不经 JSON 解析），且恢复后逐字还原。</summary>
    [Fact]
    public void Sanitize_Text_RoundTripsExactly()
    {
        var engine = CreateEngine();
        const string original = "id=sk-observe-abcdefghij0123456789 mail=a@b.com ip=10.1.2.3";
        var current = engine.Sanitize(original, out _, out _);

        Assert.DoesNotContain("{{LOOMX_CREDENTIAL_{{", current, StringComparison.Ordinal);

        var restored = engine.Restore(current, out var restoredChanged);
        Assert.True(restoredChanged);
        Assert.Equal(original, restored);
    }

    /// <summary>敏感字段的值若本身是历史引用，原样透传而不是再包一层。</summary>
    [Fact]
    public void Sanitize_SensitiveFieldHoldingReferenceIsNotWrappedAgain()
    {
        var engine = CreateEngine();
        var first = engine.Sanitize("sk-observe-abcdefghij0123456789", out _, out _);
        var payload = $$"""{"api_key":"{{first}}"}""";

        var second = engine.Sanitize(payload, out var changed, out var replacementCount);

        Assert.False(changed);
        Assert.Equal(0, replacementCount);
        Assert.Equal(first, System.Text.RegularExpressions.Regex.Match(
            second, @"\{\{LOOMX_CREDENTIAL_[A-Z2-7]{20}\}\}").Value);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var position = text.IndexOf(needle, StringComparison.Ordinal);
        while (position >= 0)
        {
            count++;
            position = text.IndexOf(needle, position + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}