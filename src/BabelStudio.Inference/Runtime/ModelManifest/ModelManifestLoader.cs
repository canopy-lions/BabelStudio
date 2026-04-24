using System.Text.Json;

namespace BabelStudio.Inference.Runtime.ModelManifest;

public static class ModelManifestLoader
{
    public static ModelManifestCatalog LoadCatalog(string manifestPath)
    {
        string fullManifestPath = Path.GetFullPath(manifestPath);
        using FileStream stream = File.OpenRead(fullManifestPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        return LoadCatalog(document.RootElement, fullManifestPath);
    }

    public static ModelManifestCatalog LoadCatalog(JsonElement rootElement, string sourceName)
    {
        if (rootElement.ValueKind is JsonValueKind.Object &&
            rootElement.TryGetProperty("models", out JsonElement modelsElement))
        {
            return new ModelManifestCatalog(ReadManifestArray(modelsElement, sourceName));
        }

        return new ModelManifestCatalog([ReadManifest(rootElement, "$", sourceName)]);
    }

    private static IReadOnlyList<ModelManifest> ReadManifestArray(JsonElement modelsElement, string sourceName)
    {
        if (modelsElement.ValueKind is not JsonValueKind.Array)
        {
            throw new ModelManifestValidationException($"Manifest '{sourceName}' field '$.models' must be an array.");
        }

        var models = new List<ModelManifest>();
        int index = 0;
        foreach (JsonElement element in modelsElement.EnumerateArray())
        {
            models.Add(ReadManifest(element, $"$.models[{index}]", sourceName));
            index++;
        }

        if (models.Count == 0)
        {
            throw new ModelManifestValidationException($"Manifest '{sourceName}' did not contain any model entries.");
        }

        return models;
    }

    private static ModelManifest ReadManifest(JsonElement element, string path, string sourceName)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw new ModelManifestValidationException($"Manifest '{sourceName}' entry '{path}' must be an object.");
        }

        string modelId = ReadRequiredString(element, "model_id", path, sourceName);
        ModelTask task = ParseTask(ReadRequiredString(element, "task", path, sourceName), path, sourceName);
        ModelLicenseKind license = ParseLicense(ReadRequiredString(element, "license", path, sourceName), path, sourceName);
        bool commercialAllowed = ReadRequiredBoolean(element, "commercial_allowed", path, sourceName);
        bool redistributionAllowed = ReadRequiredBoolean(element, "redistribution_allowed", path, sourceName);
        bool requiresAttribution = ReadRequiredBoolean(element, "requires_attribution", path, sourceName);
        bool requiresUserConsent = ReadRequiredBoolean(element, "requires_user_consent", path, sourceName);
        bool voiceCloning = ReadRequiredBoolean(element, "voice_cloning", path, sourceName);
        bool commercialSafeMode = ReadRequiredBoolean(element, "commercial_safe_mode", path, sourceName);
        string sourceUrl = ReadOptionalString(element, "source_url");
        string revision = ReadOptionalString(element, "revision");
        string sha256 = ReadOptionalString(element, "sha256");
        string? rootPath = ReadOptionalNullableString(element, "root_path", path, sourceName);
        string? benchmarkEntry = ReadOptionalNullableString(element, "benchmark_entry", path, sourceName);
        IReadOnlyList<string> aliases = ReadAliases(element, path, sourceName);
        IReadOnlyList<ModelVariantManifest> variants = ReadVariants(element, path, sourceName);
        HashVerificationPolicy hashVerificationPolicy = ReadHashVerificationPolicy(element, path, sourceName);

        if (voiceCloning && !requiresUserConsent)
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' entry '{path}' must set 'requires_user_consent' when 'voice_cloning' is true.");
        }

        if (!commercialAllowed && commercialSafeMode)
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' entry '{path}' cannot mark a model commercial-safe when commercial use is not allowed.");
        }

        if ((license is ModelLicenseKind.Unknown or ModelLicenseKind.NonCommercial or ModelLicenseKind.CcByNc40) && commercialSafeMode)
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' entry '{path}' cannot mark an unknown-license or non-commercial model as commercial-safe.");
        }

        if ((license is ModelLicenseKind.NonCommercial or ModelLicenseKind.CcByNc40) && commercialAllowed)
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' entry '{path}' cannot set 'commercial_allowed' to true when the license is non-commercial.");
        }

        if (!string.IsNullOrWhiteSpace(benchmarkEntry) && string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' entry '{path}' must define 'root_path' when 'benchmark_entry' is present.");
        }

        return new ModelManifest(
            modelId,
            task,
            license,
            commercialAllowed,
            redistributionAllowed,
            requiresAttribution,
            requiresUserConsent,
            voiceCloning,
            commercialSafeMode,
            sourceUrl,
            revision,
            sha256,
            variants,
            aliases,
            rootPath,
            benchmarkEntry,
            hashVerificationPolicy);
    }

    private static IReadOnlyList<string> ReadAliases(JsonElement element, string path, string sourceName)
    {
        if (!element.TryGetProperty("aliases", out JsonElement aliasesElement))
        {
            return [];
        }

        if (aliasesElement.ValueKind is not JsonValueKind.Array)
        {
            throw new ModelManifestValidationException($"Manifest '{sourceName}' field '{path}.aliases' must be an array.");
        }

        var aliases = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (JsonElement aliasElement in aliasesElement.EnumerateArray())
        {
            if (aliasElement.ValueKind is not JsonValueKind.String)
            {
                throw new ModelManifestValidationException(
                    $"Manifest '{sourceName}' field '{path}.aliases[{index}]' must be a string.");
            }

            string alias = aliasElement.GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(alias))
            {
                throw new ModelManifestValidationException(
                    $"Manifest '{sourceName}' field '{path}.aliases[{index}]' cannot be empty.");
            }

            if (!seen.Add(alias))
            {
                throw new ModelManifestValidationException(
                    $"Manifest '{sourceName}' field '{path}.aliases' contains duplicate alias '{alias}'.");
            }

            aliases.Add(alias);
            index++;
        }

        return aliases;
    }

    private static IReadOnlyList<ModelVariantManifest> ReadVariants(JsonElement element, string path, string sourceName)
    {
        if (!element.TryGetProperty("variants", out JsonElement variantsElement))
        {
            return [];
        }

        if (variantsElement.ValueKind is not JsonValueKind.Array)
        {
            throw new ModelManifestValidationException($"Manifest '{sourceName}' field '{path}.variants' must be an array.");
        }

        var variants = new List<ModelVariantManifest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (JsonElement variantElement in variantsElement.EnumerateArray())
        {
            if (variantElement.ValueKind is not JsonValueKind.Object)
            {
                throw new ModelManifestValidationException(
                    $"Manifest '{sourceName}' field '{path}.variants[{index}]' must be an object.");
            }

            string alias = ReadRequiredString(variantElement, "alias", $"{path}.variants[{index}]", sourceName);
            string entryPath = ReadRequiredString(variantElement, "entry_path", $"{path}.variants[{index}]", sourceName);
            string sha256 = ReadOptionalString(variantElement, "sha256");

            if (!seen.Add(alias))
            {
                throw new ModelManifestValidationException(
                    $"Manifest '{sourceName}' field '{path}.variants' contains duplicate variant alias '{alias}'.");
            }

            variants.Add(new ModelVariantManifest(alias, entryPath, sha256));
            index++;
        }

        return variants;
    }

    private static HashVerificationPolicy ReadHashVerificationPolicy(JsonElement element, string path, string sourceName)
    {
        if (!element.TryGetProperty("hash_verification", out JsonElement policyElement))
        {
            return new HashVerificationPolicy(HashVerificationMode.VerifyIfShaPresent, "SHA-256");
        }

        if (policyElement.ValueKind is not JsonValueKind.Object)
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' field '{path}.hash_verification' must be an object.");
        }

        string modeText = ReadRequiredString(policyElement, "mode", $"{path}.hash_verification", sourceName);
        HashVerificationMode mode = ParseHashVerificationMode(modeText, path, sourceName);
        string algorithm = ReadOptionalString(policyElement, "algorithm");
        if (string.IsNullOrWhiteSpace(algorithm))
        {
            algorithm = "SHA-256";
        }

        return new HashVerificationPolicy(mode, algorithm);
    }

    private static string ReadRequiredString(JsonElement element, string propertyName, string path, string sourceName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' entry '{path}' is missing required field '{propertyName}'.");
        }

        if (property.ValueKind is not JsonValueKind.String)
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' field '{path}.{propertyName}' must be a string.");
        }

        string value = property.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' field '{path}.{propertyName}' cannot be empty.");
        }

        return value;
    }

    private static string ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return string.Empty;
        }

        return property.ValueKind is JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static string? ReadOptionalNullableString(
        JsonElement element,
        string propertyName,
        string path,
        string sourceName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind is JsonValueKind.String
            ? property.GetString()?.Trim()
            : throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' field '{path}.{propertyName}' must be a string or null.");
    }

    private static bool ReadRequiredBoolean(JsonElement element, string propertyName, string path, string sourceName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' entry '{path}' is missing required field '{propertyName}'.");
        }

        if (property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' field '{path}.{propertyName}' must be a boolean.");
        }

        return property.GetBoolean();
    }

    private static ModelTask ParseTask(string value, string path, string sourceName) =>
        TryParse(
            () => ModelManifestText.ParseTask(value),
            () => new ModelManifestValidationException(
                $"Manifest '{sourceName}' field '{path}.task' value '{value}' is invalid. Expected asr, translation, tts, diarization, vad, or separation."));

    private static ModelLicenseKind ParseLicense(string value, string path, string sourceName) =>
        TryParse(
            () => ModelManifestText.ParseLicense(value),
            () => new ModelManifestValidationException(
                $"Manifest '{sourceName}' field '{path}.license' value '{value}' is invalid. Expected MIT, Apache-2.0, CC-BY-4.0, CC-BY-NC-4.0, custom, unknown, non-commercial, or noncommercial."));

    private static T TryParse<T>(Func<T> parser, Func<Exception> errorFactory)
    {
        try
        {
            return parser();
        }
        catch (ArgumentException)
        {
            throw errorFactory();
        }
    }

    private static HashVerificationMode ParseHashVerificationMode(string value, string path, string sourceName) =>
        value.ToLowerInvariant() switch
        {
            "none" => HashVerificationMode.None,
            "verify-if-sha-present" => HashVerificationMode.VerifyIfShaPresent,
            "required" => HashVerificationMode.Required,
            _ => throw new ModelManifestValidationException(
                $"Manifest '{sourceName}' field '{path}.hash_verification.mode' value '{value}' is invalid. Expected none, verify-if-sha-present, or required.")
        };
}
