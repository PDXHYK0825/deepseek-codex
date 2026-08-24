using CodexModelSwitcher.Abstractions;
using CodexModelSwitcher.Configuration;
using CodexModelSwitcher.Infrastructure;

namespace CodexModelSwitcher.Application;

public sealed class BackendRuntime : IDisposable
{
    private readonly HttpClient _httpClient;

    private BackendRuntime(
        HttpClient httpClient,
        ISecretStore secretStore,
        IChatGptLifecycle chatGptLifecycle,
        SwitchOrchestrator switcher,
        CodexStatusService status)
    {
        _httpClient = httpClient;
        SecretStore = secretStore;
        ChatGptLifecycle = chatGptLifecycle;
        Switcher = switcher;
        Status = status;
    }

    public ISecretStore SecretStore { get; }
    public IChatGptLifecycle ChatGptLifecycle { get; }
    public SwitchOrchestrator Switcher { get; }
    public CodexStatusService Status { get; }

    public static BackendRuntime CreateDefault()
    {
        var writer = new AtomicFileWriter();
        var secretStore = new WindowsCredentialStore();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var stateStore = new ManagedStateStore(writer);
        var chatGptLifecycle = new WindowsChatGptLifecycle();
        var switcher = new SwitchOrchestrator(
            secretStore,
            new OfficialDeepSeekCatalogProvider(httpClient, writer),
            chatGptLifecycle,
            writer,
            new BackupService(writer),
            stateStore,
            new TomlConfigEditor(),
            new DeepSeekConfigurationValidator());
        var status = new CodexStatusService(secretStore, stateStore, new TomlConfigInspector());
        return new BackendRuntime(httpClient, secretStore, chatGptLifecycle, switcher, status);
    }

    public void Dispose() => _httpClient.Dispose();
}
