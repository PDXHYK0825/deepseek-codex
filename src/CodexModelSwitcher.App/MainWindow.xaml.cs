using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexModelSwitcher.Application;
using CodexModelSwitcher.Domain;
using CodexModelSwitcher.Infrastructure;

namespace CodexModelSwitcher.App;

public partial class MainWindow : Window
{
    private readonly BackendRuntime _backend;
    private readonly CodexPaths _paths;
    private readonly CancellationTokenSource _windowCancellation = new();
    private bool _isBusy;
    private bool _isSynchronizingApiKey;

    public MainWindow()
    {
        InitializeComponent();
        _backend = BackendRuntime.CreateDefault();
        _paths = CodexPaths.Resolve();
        CodexHomeText.Text = _paths.CodexHome;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e) => await RefreshStatusAsync();

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        await RefreshStatusAsync();
    }

    private async void OnSwitchClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not Button { Tag: string target } || !ModelProfileExtensions.TryParseCliName(target, out var profile))
        {
            return;
        }

        string? apiKey = null;
        if (profile.IsDeepSeek())
        {
            apiKey = GetApiKeyInput().Trim();
            if (apiKey.Length == 0 && !_backend.SecretStore.Contains(DeepSeekApiKey.SecretName))
            {
                ShowError("请先输入并保存 DeepSeek API Key。");
                FocusApiKeyInput();
                return;
            }
        }

        SetBusy(true, $"正在切换到 {profile.ToDisplayName()}…");
        try
        {
            SwitchResult result;
            try
            {
                result = await ExecuteSwitchAsync(profile, apiKey, acceptExternalChanges: false);
            }
            catch (SwitcherException exception) when (exception.Code == "external_changes_detected")
            {
                var answer = MessageBox.Show(
                    this,
                    "Codex 配置被其他程序修改过。继续操作前会自动保存一份安全快照，是否继续？",
                    "检测到外部修改",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    ResultText.Text = "已取消，本次没有覆盖外部修改。";
                    return;
                }

                result = await ExecuteSwitchAsync(profile, apiKey, acceptExternalChanges: true);
            }

            ResultText.Text = result.Restart is { Started: false }
                ? "模型已切换，但 ChatGPT 未能自动启动，请手动打开。"
                : result.ActiveProfile.IsDeepSeek()
                    ? $"已切换到 {result.ActiveProfile.ToDisplayName()}；请新建任务，旧任务继续使用原模型。"
                    : "已切换到 OpenAI / GPT；历史 DeepSeek 任务仍可查看。";
            await RefreshStatusAsync(showBusy: false);
        }
        catch (SwitcherException exception)
        {
            ShowError(ToFriendlyMessage(exception));
        }
        catch (OperationCanceledException)
        {
            ResultText.Text = "操作已取消";
        }
        catch
        {
            ShowError("切换失败，请刷新状态后重试。配置写入失败时后端会自动回滚。 ");
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    private async void OnSaveKeyClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var apiKey = GetApiKeyInput().Trim();
        if (apiKey.Length == 0)
        {
            ShowError("请输入要保存的 DeepSeek API Key。");
            FocusApiKeyInput();
            return;
        }

        SetBusy(true, "正在保存 API Key…");
        try
        {
            var validatedApiKey = DeepSeekApiKey.Validate(apiKey);
            _backend.SecretStore.Write(DeepSeekApiKey.SecretName, validatedApiKey);
            SetApiKeyInput(validatedApiKey);
            await RefreshStatusAsync(showBusy: false);
            ResultText.Text = "API Key 已安全保存到 Windows 凭据库";
        }
        catch (SwitcherException exception)
        {
            ShowError(ToFriendlyMessage(exception));
        }
        catch
        {
            ShowError("API Key 保存失败，请稍后重试。");
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    private async void OnClearKeyClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "确定清除已保存的 DeepSeek API Key 吗？当前 DeepSeek 配置将在下次请求时无法认证。",
            "清除 API Key",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _backend.SecretStore.Delete(DeepSeekApiKey.SecretName);
        SetApiKeyInput(string.Empty);
        await RefreshStatusAsync();
        ResultText.Text = "已清除保存的 API Key";
    }

    private void OnApiKeyMaskChanged(object sender, RoutedEventArgs e)
    {
        if (ApiKeyPasswordBox is null || ApiKeyTextBox is null)
        {
            return;
        }

        var shouldMask = MaskApiKeyCheckBox.IsChecked == true;
        ApiKeyPasswordBox.Visibility = shouldMask ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyTextBox.Visibility = shouldMask ? Visibility.Collapsed : Visibility.Visible;
        FocusApiKeyInput();
    }

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingApiKey || ApiKeyTextBox is null)
        {
            return;
        }

        _isSynchronizingApiKey = true;
        ApiKeyTextBox.Text = ApiKeyPasswordBox.Password;
        _isSynchronizingApiKey = false;
    }

    private void OnApiKeyTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSynchronizingApiKey || ApiKeyPasswordBox is null)
        {
            return;
        }

        _isSynchronizingApiKey = true;
        ApiKeyPasswordBox.Password = ApiKeyTextBox.Text;
        _isSynchronizingApiKey = false;
    }

    private async Task RefreshStatusAsync(bool showBusy = true)
    {
        if (showBusy)
        {
            SetBusy(true, "正在读取 Codex 状态…");
        }

        try
        {
            var status = await _backend.Status.GetStatusAsync(_paths, _windowCancellation.Token);
            CurrentStateText.Text = ToStateText(status.State);
            CurrentModelText.Text = status.Model is null ? "使用 Codex 默认模型" : $"当前模型：{status.Model}";
            KeyStatusText.Text = status.HasStoredApiKey ? "API Key：已安全保存" : "API Key：未保存";
            ApiKeySaveStatusText.Text = status.HasStoredApiKey ? "已保存" : "未保存";
            ApiKeySaveStatusText.Foreground = new SolidColorBrush(status.HasStoredApiKey
                ? Color.FromRgb(22, 163, 74)
                : Color.FromRgb(100, 116, 139));
            SetApiKeyInput(status.HasStoredApiKey
                ? _backend.SecretStore.Read(DeepSeekApiKey.SecretName) ?? string.Empty
                : string.Empty);
            ClearKeyButton.IsEnabled = status.HasStoredApiKey;
            StatusIndicator.Fill = new SolidColorBrush(ToStateColor(status.State, status.HasExternalChanges));
            if (status.HasExternalChanges)
            {
                ResultText.Text = "检测到配置被其他程序修改，切换前需要确认处理。";
            }
            else if (ResultText.Text.Length == 0)
            {
                ResultText.Text = "准备就绪";
            }
        }
        catch (OperationCanceledException)
        {
            // Window is closing.
        }
        catch
        {
            CurrentStateText.Text = "无法读取当前状态";
            CurrentModelText.Text = "请确认已经运行过一次 Codex 或 ChatGPT";
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        finally
        {
            if (showBusy)
            {
                SetBusy(false, string.Empty);
            }
        }
    }

    private string GetApiKeyInput() => MaskApiKeyCheckBox.IsChecked == true
        ? ApiKeyPasswordBox.Password
        : ApiKeyTextBox.Text;

    private void SetApiKeyInput(string value)
    {
        _isSynchronizingApiKey = true;
        ApiKeyPasswordBox.Password = value;
        ApiKeyTextBox.Text = value;
        _isSynchronizingApiKey = false;
    }

    private void FocusApiKeyInput()
    {
        if (MaskApiKeyCheckBox.IsChecked == true)
        {
            ApiKeyPasswordBox.Focus();
            ApiKeyPasswordBox.SelectAll();
        }
        else
        {
            ApiKeyTextBox.Focus();
            ApiKeyTextBox.SelectAll();
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _isBusy = busy;
        ContentArea.IsEnabled = !busy;
        BusyText.Text = message;
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private Task<SwitchResult> ExecuteSwitchAsync(
        ModelProfile profile,
        string? apiKey,
        bool acceptExternalChanges)
    {
        var options = new SwitchOptions(
            RestartChatGpt: AutoRestartCheckBox.IsChecked == true,
            AcceptExternalChanges: acceptExternalChanges);
        var bridgePath = ResolveCredentialBridgePath();
        var credentialCommand = new CredentialCommandSpec(bridgePath, ["get", "deepseek"]);
        if (!profile.IsDeepSeek())
        {
            return _backend.Switcher.RestoreOpenAiAsync(
                _paths,
                credentialCommand,
                options,
                _windowCancellation.Token);
        }

        return _backend.Switcher.SwitchToDeepSeekAsync(
            _paths,
            profile,
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            credentialCommand,
            options,
            _windowCancellation.Token);
    }

    private static string ResolveCredentialBridgePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "codex-model-switcher-credential.exe");
        if (!File.Exists(path))
        {
            throw new SwitcherException("credential_bridge_missing", "The credential bridge is missing from the application directory.");
        }

        return path;
    }

    private static string ToStateText(ProviderState state) => state switch
    {
        ProviderState.OpenAI => "当前使用 OpenAI / GPT",
        ProviderState.DeepSeekFlash => "当前使用 DeepSeek V4 Flash",
        ProviderState.DeepSeekPro => "当前使用 DeepSeek V4 Pro",
        ProviderState.DeepSeekVision => "当前使用 DeepSeek Vision 实验版",
        ProviderState.VendorScriptManaged => "检测到 DeepSeek 官方脚本配置",
        ProviderState.Unknown => "当前使用未受管的自定义模型",
        ProviderState.Broken => "Codex 配置需要处理",
        _ => "状态未知"
    };

    private static Color ToStateColor(ProviderState state, bool hasExternalChanges)
    {
        if (hasExternalChanges)
        {
            return Color.FromRgb(245, 158, 11);
        }

        return state switch
        {
            ProviderState.OpenAI => Color.FromRgb(37, 99, 235),
            ProviderState.DeepSeekFlash or ProviderState.DeepSeekPro or ProviderState.DeepSeekVision => Color.FromRgb(22, 163, 74),
            ProviderState.VendorScriptManaged => Color.FromRgb(124, 58, 237),
            ProviderState.Broken => Color.FromRgb(220, 38, 38),
            _ => Color.FromRgb(100, 116, 139)
        };
    }

    private static string ToFriendlyMessage(SwitcherException exception) => exception.Code switch
    {
        "api_key_required" or "invalid_api_key" => "请输入有效的 DeepSeek API Key，Key 应以 sk- 开头。",
        "codex_home_missing" => "没有找到 Codex 配置。请先运行一次 Codex 或 ChatGPT，再重试。",
        "baseline_missing" => "还没有可恢复的 GPT 原始配置。请先成功切换一次 DeepSeek。",
        "external_changes_detected" => "配置已被其他程序修改。为防止覆盖，本次切换已停止。请刷新状态后处理。",
        "catalog_unavailable" => "无法获取 DeepSeek 模型信息，请检查网络后重试。",
        "unrecoverable_deepseek_baseline" => "检测到已有 DeepSeek 配置，但没有原始 GPT 备份。为避免丢失配置，本次操作已停止。",
        "credential_bridge_missing" => "程序安装不完整，缺少安全凭据组件。请重新安装或重新发布应用。",
        "operation_busy" => "另一个切换操作正在进行，请稍后再试。",
        _ => exception.Message
    };

    private void ShowError(string message)
    {
        ResultText.Text = message;
        MessageBox.Show(this, message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _windowCancellation.Cancel();
        _windowCancellation.Dispose();
        _backend.Dispose();
    }
}
