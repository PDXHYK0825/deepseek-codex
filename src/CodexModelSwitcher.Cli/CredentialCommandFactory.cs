using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Cli;

internal static class CredentialCommandFactory
{
    public static CredentialCommandSpec Create()
    {
        var processPath = Environment.ProcessPath
                          ?? throw new InvalidOperationException("Cannot resolve the current executable path.");
        var entryAssembly = Environment.GetCommandLineArgs().FirstOrDefault();
        var isDotnetHost = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        if (isDotnetHost && !string.IsNullOrWhiteSpace(entryAssembly))
        {
            return new CredentialCommandSpec(processPath, [entryAssembly, "credential", "get", "deepseek"]);
        }

        return new CredentialCommandSpec(processPath, ["credential", "get", "deepseek"]);
    }
}
