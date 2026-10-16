using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>
/// An <see cref="IChatClient"/> that answers each request by running the Antigravity CLI (<c>agy</c>) once, in print mode,
/// in a new empty working directory.
/// </summary>
/// <remarks>
/// The client is safe for concurrent use. It keeps no agy process between requests and no conversation state: every call
/// sends the whole conversation again. Caller tools, non-text content and sampling options are not forwarded to agy; see
/// the README for exactly what each <see cref="ChatOptions"/> member does.
/// </remarks>
public sealed class AntigravityChatClient : IChatClient
{
    /// <summary>The oldest agy version this library supports.</summary>
    public const string MinimumAgyVersion = AgyVersions.Minimum;

    private readonly AntigravityChatClientOptions _options;
    private readonly ILogger _logger;
    private readonly IAgyProcessRunner _runner;
    private readonly AgyVersionChecker _versionChecker;
    private readonly SemaphoreSlim _slots;
    private readonly PromptLimits _limits;
    private readonly ChatClientMetadata _metadata;
    private int _disposed;
    private int _newerVersionLogged;

    /// <summary>Creates a client.</summary>
    /// <param name="options">The options; null uses the defaults. They are validated and copied now.</param>
    /// <param name="logger">An optional logger. Nothing is logged without one, and nothing logged contains prompt or answer text.</param>
    /// <exception cref="ArgumentException">An option is invalid.</exception>
    public AntigravityChatClient(AntigravityChatClientOptions? options = null, ILogger<AntigravityChatClient>? logger = null)
        : this(options, logger, new AgyProcessRunner(), null)
    {
    }

    internal AntigravityChatClient(
        AntigravityChatClientOptions? options,
        ILogger? logger,
        IAgyProcessRunner runner,
        Func<string?, string>? resolvePath)
    {
        options ??= new AntigravityChatClientOptions();
        OptionsValidator.Validate(options);

        _options = Copy(options);
        _logger = logger ?? NullLogger.Instance;
        _runner = runner;
        _versionChecker = new AgyVersionChecker(runner, _options, resolvePath);
        _slots = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
        _limits = PromptLimits.ForCurrentPlatform(_options.MaxPromptLength);
        _metadata = new ChatClientMetadata("antigravity", null, _options.DefaultModelId);
    }

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        ValidatedRequest request = RequestValidator.Validate(messages, options);
        if (request.ResponseKind != ResponseKind.Text)
        {
            // Plan phase 5 adds structured output; until then the request is refused before anything runs.
            throw new NotSupportedException("Structured output (JSON formats) is not implemented yet.");
        }

        string prompt = PromptBuilder.Build(request, _options.SystemPreamble, PromptBuilder.NewNonce(), _limits);

        ResolvedAgy agy = await _versionChecker.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        if (agy.NewerThanTested && Interlocked.Exchange(ref _newerVersionLogged, 1) == 0)
        {
            LogMessages.AgyNewerThanTested(_logger, agy.Version!.ToString(), AgyVersions.NewestTested);
        }

        string? modelId = options?.ModelId ?? _options.DefaultModelId;
        ReasoningEffort? effort = options?.Reasoning?.Effort ?? _options.DefaultReasoningEffort;

        IReadOnlyList<string> ignored = InvocationBuilder.IgnoredOptionNames(options);
        if (ignored.Count > 0)
        {
            LogMessages.IgnoredOptions(_logger, string.Join(", ", ignored));
        }

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();
        try
        {
            using RequestDirectory directory = RequestDirectory.Create(
                _options.TemporaryDirectoryRoot,
                (path, _) => LogMessages.RequestDirectoryNotDeleted(_logger, path));

            AgyInvocation invocation = InvocationBuilder.Build(
                prompt, modelId, effort, schemaFilePath: null, _options.Timeout, _options.AdditionalArguments, _options.EnvironmentVariables);
            if (invocation.EffortRequested && !invocation.EffortApplied)
            {
                LogMessages.EffortNotApplied(_logger, modelId);
            }

            AgyProcessResult result = await _runner.RunAsync(
                new AgyProcessRequest(
                    agy.ExecutablePath,
                    invocation.Arguments,
                    directory.Path,
                    invocation.EnvironmentOverrides,
                    _options.Timeout,
                    _options.MaxOutputBytes),
                cancellationToken).ConfigureAwait(false);

            AgyEnvelope envelope = ErrorClassifier.Classify(result, _options.Timeout);
            if (envelope.DeniedActionKinds.Count > 0)
            {
                LogMessages.DeniedActionsWithAnswer(_logger, string.Join(", ", envelope.DeniedActionKinds));
            }

            ChatResponse response = ResponseMapper.Map(envelope, ResponseMapper.PlainText(envelope), modelId, DateTimeOffset.UtcNow);
            LogMessages.RequestCompleted(
                _logger, modelId, watch.ElapsedMilliseconds, prompt.Length, envelope.Usage?.InputTokens, envelope.Usage?.OutputTokens);
            return response;
        }
        catch (AntigravityException ex)
        {
            LogMessages.RequestFailed(_logger, ex.GetType().Name, watch.ElapsedMilliseconds, ex.ExitCode);
            throw;
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>
    /// Runs the request like <see cref="GetResponseAsync"/> and yields the whole answer as one update. There is no
    /// incremental text; errors surface on the first <c>MoveNextAsync</c>.
    /// </summary>
    /// <inheritdoc cref="GetResponseAsync" path="/param"/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        var contents = new List<AIContent>(response.Messages.SelectMany(m => m.Contents));
        if (response.Usage is not null)
        {
            contents.Add(new UsageContent(response.Usage));
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, contents)
        {
            ResponseId = response.ResponseId,
            ModelId = response.ModelId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            RawRepresentation = response.RawRepresentation,
            AdditionalProperties = response.AdditionalProperties,
        };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return _metadata;
        }

        return serviceType == typeof(AntigravityChatClient) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No agy process outlives a request, so there is nothing to release. Requests already running finish normally.
        Interlocked.Exchange(ref _disposed, 1);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static AntigravityChatClientOptions Copy(AntigravityChatClientOptions source) =>
        new()
        {
            ExecutablePath = source.ExecutablePath,
            DefaultModelId = source.DefaultModelId,
            DefaultReasoningEffort = source.DefaultReasoningEffort,
            Timeout = source.Timeout,
            MaxConcurrency = source.MaxConcurrency,
            MaxPromptLength = source.MaxPromptLength,
            SkipVersionCheck = source.SkipVersionCheck,
            EnvironmentVariables = new Dictionary<string, string?>(source.EnvironmentVariables),
            TemporaryDirectoryRoot = source.TemporaryDirectoryRoot,
            AdditionalArguments = new List<string>(source.AdditionalArguments),
            SystemPreamble = source.SystemPreamble,
            MaxOutputBytes = source.MaxOutputBytes,
        };
}
