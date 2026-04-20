using System.Diagnostics;
using System.Text.Json;
using BabelStudio.Benchmarks.Metrics;
using BabelStudio.Benchmarks.Reports;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BabelStudio.Benchmarks.Scenarios;

internal sealed class OnnxModelBenchmarkScenario
{
    public async Task<BenchmarkReport> RunAsync(BenchmarkOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidatePaths(options);

        var warnings = new List<string>();
        var coldLoadStopwatch = Stopwatch.StartNew();
        using var sessionLease = BenchmarkSessionFactory.Create(options.ModelPath, options.ProviderPreference, options.DeviceId, warnings);
        coldLoadStopwatch.Stop();

        using JsonDocument? fixtureDocument = await FixtureDocument.LoadAsync(options.FixturePath, cancellationToken);
        var inputFactory = new BenchmarkInputFactory(options.ModelPath, sessionLease.Session.InputMetadata, fixtureDocument, warnings);
        IReadOnlyList<NamedOnnxValue> inputs = inputFactory.CreateInputs();

        var warmupStopwatch = Stopwatch.StartNew();
        using (sessionLease.Session.Run(inputs))
        {
        }
        warmupStopwatch.Stop();

        List<double> latencySamples = [];
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? results = null;

        for (var runIndex = 0; runIndex < options.RunCount; runIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var measuredStopwatch = Stopwatch.StartNew();
            var runResults = sessionLease.Session.Run(inputs);
            measuredStopwatch.Stop();

            latencySamples.Add(measuredStopwatch.Elapsed.TotalMilliseconds);

            if (runIndex == options.RunCount - 1)
            {
                results = runResults;
            }
            else
            {
                runResults.Dispose();
            }
        }

        using (results)
        {
            if (results is null)
            {
                throw new InvalidOperationException("Benchmark run did not produce any measured results.");
            }

            return new BenchmarkReport(
                ModelPath: options.ModelPath,
                ReportPath: options.OutputPath,
                RequestedProvider: options.ProviderPreference.ToString().ToLowerInvariant(),
                SelectedProvider: sessionLease.SelectedProvider,
                AvailableProviders: sessionLease.AvailableProviders,
                DeviceId: options.DeviceId,
                InputSource: options.FixturePath is null ? "dummy" : "fixture",
                MeasuredRunCount: options.RunCount,
                ColdLoadMilliseconds: coldLoadStopwatch.Elapsed.TotalMilliseconds,
                WarmupMilliseconds: warmupStopwatch.Elapsed.TotalMilliseconds,
                WarmLatencyAverageMilliseconds: latencySamples.Average(),
                WarmLatencyMinimumMilliseconds: latencySamples.Min(),
                WarmLatencyMaximumMilliseconds: latencySamples.Max(),
                WarmLatencySamplesMilliseconds: latencySamples,
                Inputs: inputFactory.GetInputSummaries(),
                Outputs: BenchmarkOutputInspector.DescribeOutputs(sessionLease.Session.OutputMetadata, results),
                Warnings: warnings,
                GeneratedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    private static void ValidatePaths(BenchmarkOptions options)
    {
        if (!File.Exists(options.ModelPath))
        {
            throw new FileNotFoundException("Model path does not exist.", options.ModelPath);
        }

        if (options.FixturePath is not null && !File.Exists(options.FixturePath))
        {
            throw new FileNotFoundException("Fixture path does not exist.", options.FixturePath);
        }
    }
}

internal static class FixtureDocument
{
    public static async Task<JsonDocument?> LoadAsync(string? fixturePath, CancellationToken cancellationToken)
    {
        if (fixturePath is null)
        {
            return null;
        }

        await using var stream = File.OpenRead(fixturePath);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}

internal sealed class BenchmarkInputFactory
{
    private readonly string _modelPath;
    private readonly IReadOnlyDictionary<string, NodeMetadata> _inputMetadata;
    private readonly Dictionary<string, JsonElement> _fixtureInputs;
    private readonly Dictionary<string, long[]> _fixtureDimensions;
    private readonly ICollection<string> _warnings;
    private readonly List<BenchmarkInputSummary> _summaries = [];
    private readonly BenchmarkModelProfile _modelProfile;

    public BenchmarkInputFactory(
        string modelPath,
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata,
        JsonDocument? fixtureDocument,
        ICollection<string> warnings)
    {
        _modelPath = modelPath;
        _inputMetadata = inputMetadata;
        _warnings = warnings;
        (_fixtureInputs, _fixtureDimensions) = ParseFixture(fixtureDocument);
        _modelProfile = BenchmarkModelProfile.Create(modelPath, inputMetadata);

        if (_modelProfile.Name is not null && fixtureDocument is null)
        {
            _warnings.Add($"Applied built-in dummy input profile: {_modelProfile.Name}.");
        }
    }

    public IReadOnlyList<NamedOnnxValue> CreateInputs()
    {
        var inputs = new List<NamedOnnxValue>(_inputMetadata.Count);

        foreach (var pair in _inputMetadata)
        {
            string inputName = pair.Key;
            NodeMetadata metadata = pair.Value;

            if (!metadata.IsTensor)
            {
                throw new NotSupportedException($"Input '{inputName}' is not a tensor input.");
            }

            int[] dimensions = ResolveDimensions(inputName, metadata);
            long elementCount = CountElements(dimensions);

            NamedOnnxValue inputValue = CreateTensorInput(inputName, metadata.ElementDataType, dimensions, elementCount);
            inputs.Add(inputValue);

            _summaries.Add(new BenchmarkInputSummary(
                Name: inputName,
                ElementType: metadata.ElementDataType.ToString(),
                Dimensions: dimensions.Select(static value => (long)value).ToArray(),
                ElementCount: elementCount));
        }

        return inputs;
    }

    public IReadOnlyList<BenchmarkInputSummary> GetInputSummaries() => _summaries;

    private NamedOnnxValue CreateTensorInput(
        string inputName,
        TensorElementType elementType,
        int[] dimensions,
        long elementCount)
    {
        bool hasFixtureValues = _fixtureInputs.TryGetValue(inputName, out JsonElement values);

        return elementType switch
        {
            TensorElementType.Float => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<float>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => index == 0 ? 1.0f : 0.0f, static element => element.GetSingle())),
            TensorElementType.Double => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<double>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => index == 0 ? 1.0d : 0.0d, static element => element.GetDouble())),
            TensorElementType.Int64 => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<long>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => index, static element => element.GetInt64())),
            TensorElementType.Int32 => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<int>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => checked((int)index), static element => element.GetInt32())),
            TensorElementType.Int16 => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<short>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => checked((short)index), static element => element.GetInt16())),
            TensorElementType.UInt16 => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<ushort>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => checked((ushort)index), static element => element.GetUInt16())),
            TensorElementType.Int8 => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<sbyte>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => checked((sbyte)index), static element => element.GetSByte())),
            TensorElementType.UInt8 => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<byte>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => checked((byte)index), static element => element.GetByte())),
            TensorElementType.UInt32 => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<uint>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => checked((uint)index), static element => element.GetUInt32())),
            TensorElementType.UInt64 => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<ulong>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => checked((ulong)index), static element => element.GetUInt64())),
            TensorElementType.Bool => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<bool>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static index => index == 0, static element => element.GetBoolean())),
            TensorElementType.String => NamedOnnxValue.CreateFromTensor(inputName, CreateTensor<string>(inputName, dimensions, elementCount, hasFixtureValues ? values : default, hasFixtureValues, static _ => "babel", static element => element.GetString() ?? string.Empty)),
            _ => throw new NotSupportedException($"Input '{inputName}' uses unsupported tensor element type '{elementType}'.")
        };
    }

    private DenseTensor<T> CreateTensor<T>(
        string inputName,
        int[] dimensions,
        long elementCount,
        JsonElement source,
        bool hasFixtureValues,
        Func<int, T> dummyValueFactory,
        Func<JsonElement, T> fixtureValueParser)
    {
        if (elementCount > int.MaxValue)
        {
            throw new NotSupportedException($"Tensor element count '{elementCount}' exceeds supported benchmark input size.");
        }

        T[] data = new T[elementCount];

        if (hasFixtureValues)
        {
            var index = 0;
            foreach (JsonElement element in FlattenValues(source))
            {
                if (index >= data.Length)
                {
                    throw new InvalidOperationException("Fixture contains more values than the declared tensor shape.");
                }

                data[index++] = fixtureValueParser(element);
            }

            if (index != data.Length)
            {
                throw new InvalidOperationException($"Fixture provided {index} values but tensor shape requires {data.Length}.");
            }
        }
        else
        {
            for (var index = 0; index < data.Length; index++)
            {
                if (_modelProfile.TryGetDummyValue(inputName, index, out T? specializedValue))
                {
                    data[index] = specializedValue!;
                }
                else
                {
                    data[index] = dummyValueFactory(index);
                }
            }
        }

        return new DenseTensor<T>(data, dimensions);
    }

    private int[] ResolveDimensions(string inputName, NodeMetadata metadata)
    {
        if (_fixtureDimensions.TryGetValue(inputName, out var fixtureDimensions))
        {
            return fixtureDimensions.Select(ToPositiveDimension).ToArray();
        }

        if (_modelProfile.TryGetDimensions(inputName, out int[]? specializedDimensions))
        {
            return specializedDimensions!;
        }

        return metadata.Dimensions.Select(ToPositiveDimension).ToArray();
    }

    private static int ToPositiveDimension(int value) => value <= 0 ? 1 : value;

    private static int ToPositiveDimension(long value)
    {
        if (value <= 0)
        {
            return 1;
        }

        if (value > int.MaxValue)
        {
            throw new NotSupportedException($"Tensor dimension '{value}' exceeds supported benchmark input size.");
        }

        return checked((int)value);
    }

    private static long CountElements(IEnumerable<int> dimensions)
    {
        long count = 1;
        foreach (int dimension in dimensions)
        {
            count *= dimension == 0 ? 1 : dimension;
        }

        return count;
    }

    private static IEnumerable<JsonElement> FlattenValues(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            yield return element;
            yield break;
        }

        foreach (JsonElement child in element.EnumerateArray())
        {
            foreach (JsonElement flattened in FlattenValues(child))
            {
                yield return flattened;
            }
        }
    }

    private static (Dictionary<string, JsonElement> Inputs, Dictionary<string, long[]> Dimensions) ParseFixture(JsonDocument? fixtureDocument)
    {
        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var dimensions = new Dictionary<string, long[]>(StringComparer.Ordinal);

        if (fixtureDocument is null)
        {
            return (inputs, dimensions);
        }

        JsonElement root = fixtureDocument.RootElement;
        if (!root.TryGetProperty("inputs", out JsonElement inputsArray) || inputsArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Fixture file must contain an 'inputs' array.");
        }

        foreach (JsonElement inputElement in inputsArray.EnumerateArray())
        {
            string name = inputElement.GetProperty("name").GetString()
                ?? throw new InvalidOperationException("Fixture input name must be a string.");

            if (!inputElement.TryGetProperty("values", out JsonElement values))
            {
                throw new InvalidOperationException($"Fixture input '{name}' is missing a 'values' property.");
            }

            inputs[name] = values.Clone();

            if (inputElement.TryGetProperty("dimensions", out JsonElement dimensionArray))
            {
                dimensions[name] = dimensionArray.EnumerateArray().Select(static value => value.GetInt64()).ToArray();
            }
        }

        return (inputs, dimensions);
    }
}

internal sealed class BenchmarkModelProfile
{
    public static readonly BenchmarkModelProfile None = new(null);

    private readonly Dictionary<string, int[]> _dimensions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _scalarValues = new(StringComparer.Ordinal);

    private BenchmarkModelProfile(string? name)
    {
        Name = name;
    }

    public string? Name { get; }

    public static BenchmarkModelProfile Create(
        string modelPath,
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata)
    {
        if (LooksLikeSileroVad(modelPath, inputMetadata))
        {
            var profile = new BenchmarkModelProfile("silero-vad");
            profile._dimensions["input"] = [1, 512];
            profile._dimensions["state"] = [2, 1, 128];
            profile._scalarValues["sr"] = 16000L;
            return profile;
        }

        return None;
    }

    public bool TryGetDimensions(string inputName, out int[]? dimensions)
    {
        if (_dimensions.TryGetValue(inputName, out int[]? found))
        {
            dimensions = found;
            return true;
        }

        dimensions = null;
        return false;
    }

    public bool TryGetDummyValue<T>(string inputName, int index, out T? value)
    {
        if (index == 0 &&
            _scalarValues.TryGetValue(inputName, out object? found) &&
            found is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    private static bool LooksLikeSileroVad(
        string modelPath,
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata)
    {
        if (!Path.GetFileName(modelPath).Contains("silero", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return inputMetadata.ContainsKey("input")
            && inputMetadata.ContainsKey("state")
            && inputMetadata.ContainsKey("sr");
    }
}

internal static class BenchmarkSessionFactory
{
    public static BenchmarkSessionLease Create(
        string modelPath,
        BenchmarkProviderPreference preference,
        int deviceId,
        ICollection<string> warnings)
    {
        return preference switch
        {
            BenchmarkProviderPreference.Cpu => CreateCpu(modelPath),
            BenchmarkProviderPreference.Dml => CreateDml(modelPath, deviceId),
            BenchmarkProviderPreference.Auto => CreateAuto(modelPath, deviceId, warnings),
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "Unknown provider preference.")
        };
    }

    private static BenchmarkSessionLease CreateAuto(string modelPath, int deviceId, ICollection<string> warnings)
    {
        try
        {
            return CreateDml(modelPath, deviceId);
        }
        catch (Exception ex)
        {
            warnings.Add($"DirectML unavailable, falling back to CPU: {ex.Message}");
            return CreateCpu(modelPath);
        }
    }

    private static BenchmarkSessionLease CreateCpu(string modelPath)
    {
        var sessionOptions = CreateBaseOptions();
        var session = new InferenceSession(modelPath, sessionOptions);
        return new BenchmarkSessionLease(session, "CPUExecutionProvider", ["CPUExecutionProvider"]);
    }

    private static BenchmarkSessionLease CreateDml(string modelPath, int deviceId)
    {
        var sessionOptions = CreateBaseOptions();
        sessionOptions.AppendExecutionProvider_DML(deviceId);
        var session = new InferenceSession(modelPath, sessionOptions);
        return new BenchmarkSessionLease(session, "DmlExecutionProvider", ["DmlExecutionProvider", "CPUExecutionProvider"]);
    }

    private static SessionOptions CreateBaseOptions() =>
        new()
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
}

internal sealed record BenchmarkSessionLease(
    InferenceSession Session,
    string SelectedProvider,
    IReadOnlyList<string> AvailableProviders) : IDisposable
{
    public void Dispose() => Session.Dispose();
}

internal static class BenchmarkOutputInspector
{
    public static IReadOnlyList<BenchmarkOutputSummary> DescribeOutputs(
        IReadOnlyDictionary<string, NodeMetadata> outputMetadata,
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        var summaries = new List<BenchmarkOutputSummary>(results.Count);

        foreach (DisposableNamedOnnxValue result in results)
        {
            outputMetadata.TryGetValue(result.Name, out NodeMetadata? metadata);

            summaries.Add(new BenchmarkOutputSummary(
                Name: result.Name,
                ElementType: metadata?.ElementDataType.ToString() ?? "Unknown",
                Dimensions: DescribeDimensions(metadata)));
        }

        return summaries;
    }

    private static IReadOnlyList<long> DescribeDimensions(NodeMetadata? metadata)
    {
        if (metadata is null)
        {
            return [];
        }

        var dimensions = new List<long>(metadata.Dimensions.Length);
        foreach (int dimension in metadata.Dimensions)
        {
            dimensions.Add(dimension);
        }

        return dimensions;
    }
}
