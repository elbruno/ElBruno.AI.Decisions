using System.Text.Json;
using Microsoft.AI.Foundry.Local;

namespace ElBruno.AI.Decisions.Foundry.Local;

/// <summary>Registers cached Foundry Local decision models with the SDK's typed-decision task.</summary>
public static class FoundryLocalDecisionModelResolver
{
    private const string DecisionTask = "typed-decision";

    /// <summary>
    /// Resolves a cached public model by alias and registers its existing assets in the local catalog
    /// under a distinct local identity as a typed-decision model, if needed.
    /// </summary>
    /// <param name="modelAlias">A cached model whose capabilities include noul, choice, and score.</param>
    /// <param name="cancellationToken">Cancels catalog operations.</param>
    /// <returns>The typed model. The caller owns its load and unload lifecycle.</returns>
    /// <exception cref="InvalidOperationException">The Foundry Local manager is not initialized or the model is not cached.</exception>
    /// <exception cref="ArgumentException">The alias is unknown or the model does not advertise decision capabilities.</exception>
    public static async Task<IModel> ResolveAsync(string modelAlias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelAlias);
        if (!FoundryLocalManager.IsInitialized)
            throw new InvalidOperationException("Initialize FoundryLocalManager before resolving a cached decision model.");

        FoundryLocalManager manager = FoundryLocalManager.Instance;
        ICatalog publicCatalog = await manager.GetCatalogAsync(CatalogType.Public, cancellationToken).ConfigureAwait(false);
        IModel model = await publicCatalog.GetModelAsync(modelAlias, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException($"Foundry Local model '{modelAlias}' was not found in the public catalog.", nameof(modelAlias));

        string? capabilities = model.Info.Capabilities;
        if (!SupportsDecisionCapabilities(capabilities))
            throw new ArgumentException(
                $"Foundry Local model '{modelAlias}' does not advertise noul, choice, and score capabilities.",
                nameof(modelAlias));
        if (!await model.IsCachedAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException(
                $"Foundry Local model '{modelAlias}' is not cached. Download it before resolving it.");
        if (model.Info.Task == DecisionTask)
            return model;

        string modelPath = await model.GetPathAsync(cancellationToken).ConfigureAwait(false);
        (string localAlias, string localId) = GetCompatibilityIdentity(model.Id);
        // SDK 2.4.1 honors the package's Alias field over the alias derived from the registration ID.
        string inferenceMetadataPath = Path.Combine(modelPath, "inference_model.json");
        if (File.Exists(inferenceMetadataPath))
        {
            using FileStream stream = File.OpenRead(inferenceMetadataPath);
            using JsonDocument inferenceMetadata = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            EnsureCompatiblePackageAlias(inferenceMetadata.RootElement, localAlias);
        }
        if (await publicCatalog.GetModelAsync(localAlias, cancellationToken).ConfigureAwait(false) is not null ||
            await publicCatalog.GetModelVariantAsync(localId, cancellationToken).ConfigureAwait(false) is not null)
            throw new InvalidOperationException($"Foundry Local compatibility identity '{localId}' is already in the public catalog.");

        ICatalog localCatalog = await manager.GetCatalogAsync(CatalogType.Local, cancellationToken).ConfigureAwait(false);
        IModel? registered = await localCatalog.GetModelVariantAsync(localId, cancellationToken).ConfigureAwait(false);
        if (registered is null && await localCatalog.GetModelAsync(localAlias, cancellationToken).ConfigureAwait(false) is not null)
        {
            registered = await localCatalog.GetModelVariantAsync(localId, cancellationToken).ConfigureAwait(false);
            if (registered is null)
                throw new InvalidOperationException($"Foundry Local compatibility alias '{localAlias}' is already registered locally.");
        }

        var metadata = new ModelInfoBuilder()
            .SetStringProperty(ModelInfoPropertyKeys.Task, DecisionTask)
            .SetStringProperty(ModelInfoPropertyKeys.DisplayName, model.Info.DisplayName ?? model.Alias)
            .SetStringProperty(ModelInfoPropertyKeys.Capabilities, capabilities!);
        if (model.Info.Runtime is { } runtime)
        {
            if (runtime.DeviceType != DeviceType.Invalid)
                metadata.SetStringProperty(ModelInfoPropertyKeys.DeviceType, runtime.DeviceType.ToString());
            if (!string.IsNullOrWhiteSpace(runtime.ExecutionProvider))
                metadata.SetStringProperty(ModelInfoPropertyKeys.ExecutionProvider, runtime.ExecutionProvider);
        }
        if (model.Info.ContextLength is > 0)
            metadata.SetIntProperty(ModelInfoPropertyKeys.ContextLength, model.Info.ContextLength.Value);

        if (registered is null)
        {
            try
            {
                registered = await localCatalog.RegisterModelAsync(modelPath, localId, metadata, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (FoundryLocalException)
            {
                // Another resolver may have registered the exact ID after the first lookup.
                registered = await localCatalog.GetModelVariantAsync(localId, cancellationToken).ConfigureAwait(false);
                if (registered is null)
                    throw;
            }
        }

        string registeredPath = await registered.GetPathAsync(cancellationToken).ConfigureAwait(false);
        if (registered.Id != localId || registered.Alias != localAlias || registered.Info.Task != DecisionTask ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(registeredPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(modelPath)), StringComparison.Ordinal))
            throw new InvalidOperationException($"Foundry Local compatibility identity '{localId}' refers to a different model, task, or path.");

        return registered;
    }

    internal static (string Alias, string Id) GetCompatibilityIdentity(string modelId)
    {
        int separator = modelId.LastIndexOf(':');
        if (separator <= 0 || separator == modelId.Length - 1)
            throw new InvalidOperationException($"Foundry Local model ID '{modelId}' has no name or version.");

        string alias = $"elbruno-decisions-{modelId[..separator]}-typed-decision";
        return (alias, alias + modelId[separator..]);
    }

    internal static void EnsureCompatiblePackageAlias(JsonElement metadata, string localAlias)
    {
        if (metadata.ValueKind == JsonValueKind.Object &&
            (!metadata.TryGetProperty("Alias", out JsonElement alias) ||
             (alias.ValueKind == JsonValueKind.String && (alias.GetString() is "" || alias.GetString() == localAlias))))
            return;

        throw new InvalidOperationException(
            "The Foundry Local package has invalid alias metadata or overrides its registration alias. " +
            "Automatic registration was skipped to avoid a catalog collision.");
    }

    internal static bool SupportsDecisionCapabilities(string? capabilities)
    {
        if (string.IsNullOrWhiteSpace(capabilities))
            return false;
        string[] values = capabilities.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Contains("noul", StringComparer.OrdinalIgnoreCase) &&
            values.Contains("choice", StringComparer.OrdinalIgnoreCase) &&
            values.Contains("score", StringComparer.OrdinalIgnoreCase);
    }
}
