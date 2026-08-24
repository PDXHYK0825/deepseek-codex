namespace CodexModelSwitcher.Domain;

public enum ModelProfile
{
    OpenAI = 0,
    DeepSeekFlash = 1,
    DeepSeekPro = 2,
    DeepSeekVision = 3
}

public static class ModelProfileExtensions
{
    public static bool IsDeepSeek(this ModelProfile profile) => profile is
        ModelProfile.DeepSeekFlash or ModelProfile.DeepSeekPro or ModelProfile.DeepSeekVision;

    public static string ToModelSlug(this ModelProfile profile) => profile switch
    {
        ModelProfile.DeepSeekFlash => "deepseek-v4-flash",
        ModelProfile.DeepSeekPro => "deepseek-v4-pro",
        ModelProfile.DeepSeekVision => "deepseek-v4-flash-vision-exp",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "OpenAI is restored from the baseline and has no fixed model slug.")
    };

    public static string ToDisplayName(this ModelProfile profile) => profile switch
    {
        ModelProfile.OpenAI => "OpenAI / GPT (original configuration)",
        ModelProfile.DeepSeekFlash => "DeepSeek V4 Flash",
        ModelProfile.DeepSeekPro => "DeepSeek V4 Pro",
        ModelProfile.DeepSeekVision => "DeepSeek V4 Flash Vision Experimental",
        _ => profile.ToString()
    };

    public static bool TryParseCliName(string value, out ModelProfile profile)
    {
        profile = value.Trim().ToLowerInvariant() switch
        {
            "flash" or "1" or "deepseek-v4-flash" => ModelProfile.DeepSeekFlash,
            "pro" or "2" or "deepseek-v4-pro" => ModelProfile.DeepSeekPro,
            "vision" or "3" or "deepseek-v4-flash-vision-exp" => ModelProfile.DeepSeekVision,
            "openai" or "gpt" or "9" => ModelProfile.OpenAI,
            _ => (ModelProfile)(-1)
        };

        return Enum.IsDefined(profile);
    }
}
