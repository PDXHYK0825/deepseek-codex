using CodexModelSwitcher.Infrastructure;

namespace CodexModelSwitcher.CredentialBridge;

internal static class Program
{
    public static int Main(string[] args)
    {
        var isDirectInvocation = args.Length == 2 &&
                                 args[0].Equals("get", StringComparison.OrdinalIgnoreCase) &&
                                 args[1].Equals("deepseek", StringComparison.OrdinalIgnoreCase);
        var isLegacyInvocation = args.Length == 3 &&
                                 args[0].Equals("credential", StringComparison.OrdinalIgnoreCase) &&
                                 args[1].Equals("get", StringComparison.OrdinalIgnoreCase) &&
                                 args[2].Equals("deepseek", StringComparison.OrdinalIgnoreCase);
        if (!isDirectInvocation && !isLegacyInvocation)
        {
            Console.Error.WriteLine("Usage: codex-model-switcher-credential get deepseek");
            return 1;
        }

        try
        {
            var value = new WindowsCredentialStore().Read(DeepSeekApiKey.SecretName);
            if (string.IsNullOrEmpty(value))
            {
                Console.Error.WriteLine("DeepSeek credential is not available.");
                return 2;
            }

            Console.Out.Write(value);
            return 0;
        }
        catch
        {
            Console.Error.WriteLine("DeepSeek credential could not be read.");
            return 3;
        }
    }
}
