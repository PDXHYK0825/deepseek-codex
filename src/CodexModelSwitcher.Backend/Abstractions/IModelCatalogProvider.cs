using CodexModelSwitcher.Domain;
using CodexModelSwitcher.Infrastructure;

namespace CodexModelSwitcher.Abstractions;

public interface IModelCatalogProvider
{
    Task<ModelCatalogDocument> GetCatalogAsync(
        CodexPaths paths,
        bool refresh,
        CancellationToken cancellationToken = default);
}
