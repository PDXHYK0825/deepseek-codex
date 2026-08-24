using System.Text.Json;
using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Configuration;

public sealed class DeepSeekConfigurationValidator
{
    private static readonly HashSet<string> RequiredSlugs = new(StringComparer.Ordinal)
    {
        ModelProfile.DeepSeekFlash.ToModelSlug(),
        ModelProfile.DeepSeekPro.ToModelSlug(),
        ModelProfile.DeepSeekVision.ToModelSlug()
    };

    private readonly TomlConfigInspector _inspector = new();

    public void Validate(string config, string catalogJson, ModelProfile expectedProfile)
    {
        var inspection = _inspector.Inspect(config);
        var problems = new List<string>(inspection.Diagnostics);

        if (inspection.DuplicateTopLevelKeys.Count > 0)
        {
            problems.Add("The generated configuration has duplicate top-level keys.");
        }

        if (!string.Equals(inspection.Model, expectedProfile.ToModelSlug(), StringComparison.Ordinal))
        {
            problems.Add($"Expected model {expectedProfile.ToModelSlug()}, found {inspection.Model ?? "<missing>"}.");
        }

        if (!string.Equals(inspection.Provider, "deepseek", StringComparison.Ordinal))
        {
            problems.Add($"Expected model_provider deepseek, found {inspection.Provider ?? "<missing>"}.");
        }

        if (!inspection.HasDeepSeekProvider)
        {
            problems.Add("The [model_providers.deepseek] section is missing.");
        }

        try
        {
            using var document = JsonDocument.Parse(catalogJson);
            var slugs = document.RootElement.GetProperty("models")
                .EnumerateArray()
                .Select(model => model.GetProperty("slug").GetString())
                .Where(slug => slug is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            foreach (var required in RequiredSlugs)
            {
                if (!slugs.Contains(required))
                {
                    problems.Add($"The model catalog is missing {required}.");
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            problems.Add($"The model catalog is invalid: {exception.Message}");
        }

        if (problems.Count > 0)
        {
            throw new SwitcherException("configuration_validation_failed", string.Join(Environment.NewLine, problems));
        }
    }
}
