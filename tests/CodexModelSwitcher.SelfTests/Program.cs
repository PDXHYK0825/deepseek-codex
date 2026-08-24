using CodexModelSwitcher.Abstractions;
using CodexModelSwitcher.Application;
using CodexModelSwitcher.Configuration;
using CodexModelSwitcher.Domain;
using CodexModelSwitcher.Infrastructure;

namespace CodexModelSwitcher.SelfTests;

internal static class Program
{
    public static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<Task> Run)>
        {
            ("TOML editor preserves unrelated settings", TestTomlEditorAsync),
            ("GPT configuration retains an inactive DeepSeek provider", TestOpenAiCompatibilityConfigurationAsync),
            ("DeepSeek switch and GPT restore preserve baseline settings", TestSwitchAndRestoreAsync),
            ("Accepted external settings survive GPT restore", TestExternalSettingsSurviveRestoreAsync),
            ("External configuration changes are blocked", TestExternalChangesAsync),
            ("Official-script backup can be adopted without copying its plaintext key", TestVendorAdoptionAsync),
            ("Unmanaged DeepSeek config is not mistaken for an OpenAI baseline", TestUnmanagedDeepSeekAsync),
            ("Official catalog extraction is data-only and allowlisted", TestCatalogExtractionAsync)
        };
        if (Environment.GetEnvironmentVariable("RUN_LIVE_TESTS") == "1")
        {
            tests.Add(("Live DeepSeek script catalog is compatible", TestLiveCatalogAsync));
        }

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"FAIL {test.Name}");
                Console.WriteLine($"     {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static Task TestTomlEditorAsync()
    {
        const string original = """"
            # keep this comment
            model = "old-model"
            model_provider = "old-provider"
            preferred_auth_method = "chatgpt"
            forced_login_method = "chatgpt"
            service_tier = "default"
            custom_setting = "keep"
            text = """
            model = "this is string content"
            """

            [features]
            multi_agent = true

            [model_providers.deepseek]
            base_url = "https://old.invalid"
            experimental_bearer_token = "sk-must-disappear"

            [model_providers.deepseek.auth]
            command = "old-helper"

            [mcp_servers.example]
            command = "example"
            """";

        var editor = new TomlConfigEditor();
        var result = editor.ComposeDeepSeekConfiguration(
            original,
            ModelProfile.DeepSeekPro,
            Path.Combine(Path.GetTempPath(), "switcher", "models.json"),
            new CredentialCommandSpec("credential-helper.exe", ["get", "deepseek"]));

        AssertContains(result, "custom_setting = \"keep\"");
        AssertContains(result, "model = \"this is string content\"");
        AssertContains(result, "[mcp_servers.example]");
        AssertContains(result, "model = \"deepseek-v4-pro\"");
        AssertContains(result, "[model_providers.deepseek.auth]");
        AssertContains(result, "args = [\"get\", \"deepseek\"]");
        AssertDoesNotContain(result, "preferred_auth_method");
        AssertDoesNotContain(result, "forced_login_method");
        AssertDoesNotContain(result, "service_tier");
        AssertDoesNotContain(result, "sk-must-disappear");
        AssertEqual(1, Count(result, "[model_providers.deepseek]"), "DeepSeek provider section count");
        return Task.CompletedTask;
    }

    private static Task TestOpenAiCompatibilityConfigurationAsync()
    {
        const string current = "model = \"deepseek-v4-pro\"\nmodel_provider = \"deepseek\"\nmodel_catalog_json = \"models.json\"\ncurrent_runtime = \"keep\"\n[features]\nmulti_agent = true\n[model_providers.deepseek]\nbase_url = \"https://old.invalid\"\n[model_providers.deepseek.auth]\ncommand = \"old-helper\"\n";
        const string baseline = "model = \"gpt-original\"\nservice_tier = \"default\"\nbaseline_runtime = \"obsolete\"\n[features]\nmulti_agent = false\n";
        var editor = new TomlConfigEditor();
        var result = editor.ComposeOpenAiCompatibilityConfiguration(
            current,
            baseline,
            new CredentialCommandSpec("credential-helper.exe", ["get", "deepseek"]));

        AssertContains(result, "model = \"gpt-original\"");
        AssertContains(result, "service_tier = \"default\"");
        AssertContains(result, "current_runtime = \"keep\"");
        AssertDoesNotContain(result, "baseline_runtime = \"obsolete\"");
        AssertContains(result, "[features]");
        AssertContains(result, "multi_agent = true");
        AssertContains(result, "[model_providers.deepseek]");
        AssertContains(result, "base_url = \"https://api.deepseek.com/\"");
        AssertContains(result, "args = [\"get\", \"deepseek\"]");
        AssertDoesNotContain(result, "https://old.invalid");
        AssertDoesNotContain(result, "old-helper");
        AssertEqual(1, Count(result, "[model_providers.deepseek]"), "inactive DeepSeek provider section count");
        return Task.CompletedTask;
    }

    private static async Task TestExternalSettingsSurviveRestoreAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await File.WriteAllTextAsync(
            fixture.Paths.ConfigPath,
            "model = \"gpt-original\"\nservice_tier = \"default\"\n[desktop]\nruntime = \"old\"\n");
        await fixture.Orchestrator.SwitchToDeepSeekAsync(
            fixture.Paths,
            ModelProfile.DeepSeekPro,
            "sk-test-key",
            fixture.CredentialCommand,
            new SwitchOptions(RestartChatGpt: false));

        var externallyUpdated = (await File.ReadAllTextAsync(fixture.Paths.ConfigPath))
            .Replace("runtime = \"old\"", "runtime = \"new\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.Paths.ConfigPath, externallyUpdated);

        await fixture.Orchestrator.RestoreOpenAiAsync(
            fixture.Paths,
            fixture.CredentialCommand,
            new SwitchOptions(RestartChatGpt: false, AcceptExternalChanges: true));

        var restored = await File.ReadAllTextAsync(fixture.Paths.ConfigPath);
        AssertContains(restored, "model = \"gpt-original\"");
        AssertContains(restored, "service_tier = \"default\"");
        AssertContains(restored, "runtime = \"new\"");
        AssertContains(restored, "[model_providers.deepseek]");
    }

    private static async Task TestSwitchAndRestoreAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var originalConfig = "# user config\nmodel = \"gpt-original\"\n[features]\nmulti_agent = true\n";
        var originalModels = "{\"models\":[{\"slug\":\"user-model\"}]}\n";
        await File.WriteAllTextAsync(fixture.Paths.ConfigPath, originalConfig);
        await File.WriteAllTextAsync(fixture.Paths.ModelsPath, originalModels);

        var switched = await fixture.Orchestrator.SwitchToDeepSeekAsync(
            fixture.Paths,
            ModelProfile.DeepSeekPro,
            "sk-test-key",
            fixture.CredentialCommand,
            new SwitchOptions(RestartChatGpt: false));

        AssertEqual(ModelProfile.DeepSeekPro, switched.ActiveProfile, "active profile");
        var managedConfig = await File.ReadAllTextAsync(fixture.Paths.ConfigPath);
        AssertContains(managedConfig, "model = \"deepseek-v4-pro\"");
        AssertContains(managedConfig, "[model_providers.deepseek.auth]");
        AssertDoesNotContain(managedConfig, "sk-test-key");
        AssertTrue(Directory.Exists(fixture.Paths.BaselineDirectory), "baseline directory was not created");

        var status = await fixture.StatusService.GetStatusAsync(fixture.Paths);
        AssertEqual(ProviderState.DeepSeekPro, status.State, "status after switch");
        AssertTrue(!status.HasExternalChanges, "managed files were incorrectly reported as externally changed");

        await fixture.Orchestrator.RestoreOpenAiAsync(
            fixture.Paths,
            fixture.CredentialCommand,
            new SwitchOptions(RestartChatGpt: false));
        var restoredConfig = await File.ReadAllTextAsync(fixture.Paths.ConfigPath);
        AssertContains(restoredConfig, "# user config");
        AssertContains(restoredConfig, "model = \"gpt-original\"");
        AssertContains(restoredConfig, "[features]\nmulti_agent = true");
        AssertContains(restoredConfig, "[model_providers.deepseek]");
        AssertContains(restoredConfig, "args = [\"get\", \"deepseek\"]");
        AssertDoesNotContain(restoredConfig, "model_provider = \"deepseek\"");
        AssertEqual(originalModels, await File.ReadAllTextAsync(fixture.Paths.ModelsPath), "restored models.json");

        var restoredStatus = await fixture.StatusService.GetStatusAsync(fixture.Paths);
        AssertEqual(ProviderState.OpenAI, restoredStatus.State, "status after GPT restore");
        AssertTrue(!restoredStatus.HasExternalChanges, "compatibility provider was incorrectly reported as an external change");
    }

    private static async Task TestExternalChangesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await File.WriteAllTextAsync(fixture.Paths.ConfigPath, "model = \"gpt-original\"\n");
        await fixture.Orchestrator.SwitchToDeepSeekAsync(
            fixture.Paths,
            ModelProfile.DeepSeekFlash,
            "sk-test-key",
            fixture.CredentialCommand,
            new SwitchOptions(RestartChatGpt: false));

        await File.AppendAllTextAsync(fixture.Paths.ConfigPath, "# changed elsewhere\n");
        try
        {
            await fixture.Orchestrator.SwitchToDeepSeekAsync(
                fixture.Paths,
                ModelProfile.DeepSeekPro,
                null,
                fixture.CredentialCommand,
                new SwitchOptions(RestartChatGpt: false));
            throw new InvalidOperationException("Expected the switch to reject external changes.");
        }
        catch (SwitcherException exception) when (exception.Code == "external_changes_detected")
        {
            // Expected.
        }
    }

    private static async Task TestVendorAdoptionAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        const string originalConfig = "model = \"gpt-before-vendor-script\"\n";
        Directory.CreateDirectory(fixture.Paths.VendorBackupDirectory);
        await File.WriteAllTextAsync(Path.Combine(fixture.Paths.VendorBackupDirectory, "manifest.txt"), "original_config_existed=1\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Paths.VendorBackupDirectory, "config.toml"), originalConfig);
        await File.WriteAllTextAsync(
            fixture.Paths.ConfigPath,
            "model = \"deepseek-v4-flash\"\nmodel_provider = \"deepseek\"\n[model_providers.deepseek]\nexperimental_bearer_token = \"sk-vendor-plaintext\"\n");
        await File.WriteAllTextAsync(fixture.Paths.ModelsPath, FakeCatalogProvider.ValidCatalog);

        await fixture.Orchestrator.SwitchToDeepSeekAsync(
            fixture.Paths,
            ModelProfile.DeepSeekVision,
            "sk-replacement-key",
            fixture.CredentialCommand,
            new SwitchOptions(RestartChatGpt: false));

        foreach (var configBackup in Directory.EnumerateFiles(fixture.Paths.ManagementRoot, "config.toml", SearchOption.AllDirectories))
        {
            AssertDoesNotContain(await File.ReadAllTextAsync(configBackup), "sk-vendor-plaintext");
        }

        await fixture.Orchestrator.RestoreOpenAiAsync(
            fixture.Paths,
            fixture.CredentialCommand,
            new SwitchOptions(RestartChatGpt: false));
        var restoredConfig = await File.ReadAllTextAsync(fixture.Paths.ConfigPath);
        AssertContains(restoredConfig, originalConfig.TrimEnd());
        AssertContains(restoredConfig, "[model_providers.deepseek]");
        AssertTrue(!File.Exists(fixture.Paths.ModelsPath), "vendor import should restore the pre-script absence of models.json");
    }

    private static Task TestCatalogExtractionAsync()
    {
        var script = """
            $SCRIPT_VERSION = '9.8.7'
            $ModelsJson = @'
            {"models":[
              {"slug":"deepseek-v4-flash","value":1},
              {"slug":"untrusted-extra","value":2},
              {"slug":"deepseek-v4-pro","value":3},
              {"slug":"deepseek-v4-flash-vision-exp","value":4}
            ]}
            '@
            Write-Host "this code must not execute"
            """;
        var catalog = OfficialDeepSeekCatalogProvider.ExtractCatalog(script);
        AssertEqual("9.8.7", catalog.SourceVersion, "script version");
        AssertTrue(OfficialDeepSeekCatalogProvider.IsValidCatalog(catalog.Json), "extracted catalog validity");
        AssertDoesNotContain(catalog.Json, "untrusted-extra");
        AssertDoesNotContain(catalog.Json, "Write-Host");
        return Task.CompletedTask;
    }

    private static async Task TestUnmanagedDeepSeekAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await File.WriteAllTextAsync(
            fixture.Paths.ConfigPath,
            "model = \"deepseek-v4-pro\"\nmodel_provider = \"deepseek\"\n[model_providers.deepseek]\nexperimental_bearer_token = \"sk-unmanaged\"\n");
        try
        {
            await fixture.Orchestrator.SwitchToDeepSeekAsync(
                fixture.Paths,
                ModelProfile.DeepSeekPro,
                "sk-replacement-key",
                fixture.CredentialCommand,
                new SwitchOptions(RestartChatGpt: false));
            throw new InvalidOperationException("Expected an unmanaged DeepSeek configuration to be rejected.");
        }
        catch (SwitcherException exception) when (exception.Code == "unrecoverable_deepseek_baseline")
        {
            AssertTrue(!Directory.Exists(fixture.Paths.BaselineDirectory), "an unsafe baseline was created");
        }
    }

    private static async Task TestLiveCatalogAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexModelSwitcherTests", Guid.NewGuid().ToString("N"));
        var codexHome = Path.Combine(root, "codex-home");
        var appData = Path.Combine(root, "app-data");
        Directory.CreateDirectory(codexHome);
        Directory.CreateDirectory(appData);
        try
        {
            var paths = CodexPaths.Resolve(codexHome, appData);
            var writer = new AtomicFileWriter();
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var provider = new OfficialDeepSeekCatalogProvider(httpClient, writer);
            var catalog = await provider.GetCatalogAsync(paths, refresh: true);
            AssertTrue(OfficialDeepSeekCatalogProvider.IsValidCatalog(catalog.Json), "live catalog validity");
            AssertTrue(catalog.SourceVersion is not "unknown" and not "cache-fallback", "live catalog version was not detected");
            AssertTrue(File.Exists(paths.CatalogCachePath), "live catalog cache was not written");
        }
        finally
        {
            var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodexModelSwitcherTests"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(root);
            if (target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static void AssertContains(string source, string value)
    {
        if (!source.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected text was not found: {value}");
        }
    }

    private static void AssertDoesNotContain(string source, string value)
    {
        if (source.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected text was found: {value}");
        }
    }

    private static void AssertTrue(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}', found '{actual}'.");
        }
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(
            string root,
            CodexPaths paths,
            SwitchOrchestrator orchestrator,
            CodexStatusService statusService)
        {
            Root = root;
            Paths = paths;
            Orchestrator = orchestrator;
            StatusService = statusService;
        }

        public string Root { get; }
        public CodexPaths Paths { get; }
        public SwitchOrchestrator Orchestrator { get; }
        public CodexStatusService StatusService { get; }
        public CredentialCommandSpec CredentialCommand { get; } = new("credential-helper.exe", ["get", "deepseek"]);

        public static Task<TestFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "CodexModelSwitcherTests", Guid.NewGuid().ToString("N"));
            var codexHome = Path.Combine(root, "codex-home");
            var appData = Path.Combine(root, "app-data");
            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(appData);
            var paths = CodexPaths.Resolve(codexHome, appData);
            var writer = new AtomicFileWriter();
            var secrets = new InMemorySecretStore();
            var stateStore = new ManagedStateStore(writer);
            var orchestrator = new SwitchOrchestrator(
                secrets,
                new FakeCatalogProvider(),
                new FakeLifecycle(),
                writer,
                new BackupService(writer),
                stateStore,
                new TomlConfigEditor(),
                new DeepSeekConfigurationValidator());
            var statusService = new CodexStatusService(secrets, stateStore, new TomlConfigInspector());
            return Task.FromResult(new TestFixture(root, paths, orchestrator, statusService));
        }

        public ValueTask DisposeAsync()
        {
            var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodexModelSwitcherTests"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Root);
            if (target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public bool Contains(string key) => _values.ContainsKey(key);
        public string? Read(string key) => _values.GetValueOrDefault(key);
        public void Write(string key, string value) => _values[key] = value;
        public bool Delete(string key) => _values.Remove(key);
    }

    private sealed class FakeCatalogProvider : IModelCatalogProvider
    {
        public const string ValidCatalog = "{\"models\":[{\"slug\":\"deepseek-v4-flash\"},{\"slug\":\"deepseek-v4-pro\"},{\"slug\":\"deepseek-v4-flash-vision-exp\"}]}\n";

        public Task<ModelCatalogDocument> GetCatalogAsync(CodexPaths paths, bool refresh, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelCatalogDocument(ValidCatalog, "test", Hashing.Sha256(ValidCatalog)));
    }

    private sealed class FakeLifecycle : IChatGptLifecycle
    {
        public Task<RestartResult> RestartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RestartResult(true, true, true));
    }
}
