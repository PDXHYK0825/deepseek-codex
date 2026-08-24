using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Infrastructure;

public static class DeepSeekApiKey
{
    public const string SecretName = "deepseek-api-key";
    public const string EnvironmentVariable = "DEEPSEEK_API_KEY";

    public static string Validate(string value)
    {
        var normalized = value.Trim();
        if (!normalized.StartsWith("sk-", StringComparison.Ordinal) || normalized.Length <= 3)
        {
            throw new SwitcherException("invalid_api_key", "The DeepSeek API key must start with sk-.");
        }

        if (normalized.IndexOfAny(['\r', '\n', '"']) >= 0)
        {
            throw new SwitcherException("invalid_api_key", "The DeepSeek API key contains unsupported characters.");
        }

        return normalized;
    }
}
