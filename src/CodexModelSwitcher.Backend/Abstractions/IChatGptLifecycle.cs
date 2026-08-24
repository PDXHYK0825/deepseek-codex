using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Abstractions;

public interface IChatGptLifecycle
{
    Task<RestartResult> RestartAsync(CancellationToken cancellationToken = default);
}
