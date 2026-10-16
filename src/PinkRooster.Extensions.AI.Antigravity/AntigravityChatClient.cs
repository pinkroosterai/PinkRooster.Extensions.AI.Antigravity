using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
    private readonly ISchemaValidator _schemaValidator;
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
        Func<string?, string>? resolvePath,
        ISchemaValidator? schemaValidator = null)
    {
        options ??= new AntigravityChatClientOptions();
        OptionsValidator.Validate(options);

        _options = Copy(options);
        _logger = logger ?? NullLogger.Instance;
        _runner = runner;
        _schemaValidator = schemaValidator ?? SchemaValidator.Instance;
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
        Prepared prepared = await PrepareAsync(messages, options, cancellationToken).ConfigureAwait(false);
        ValidatedRequest request = prepared.Request;
        ICompiledSchema? schema = prepared.Schema;
        string prompt = prepared.Prompt;
        ResolvedAgy agy = prepared.Agy;
        string? modelId = prepared.ModelId;
        ReasoningEffort? effort = prepared.Effort;

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();
        try
        {
            using RequestDirectory directory = RequestDirectory.Create(
                _options.TemporaryDirectoryRoot,
                (path, _) => LogMessages.RequestDirectoryNotDeleted(_logger, path));

            string? schemaFile = request.ResponseKind == ResponseKind.JsonSchema ? directory.WriteSchemaFile(request.SchemaText!) : null;
            AgyInvocation invocation = InvocationBuilder.Build(
                prompt, modelId, effort, schemaFile, _options.Timeout, _options.AdditionalArguments, _options.EnvironmentVariables);
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

            string text = request.ResponseKind switch
            {
                ResponseKind.JsonSchema => StructuredResultChecker.CheckSchemaResult(envelope, schema!),
                ResponseKind.Json => StructuredResultChecker.CheckPlainJson(ResponseMapper.PlainText(envelope)),
                _ => ResponseMapper.PlainText(envelope),
            };

            ChatResponse response = ResponseMapper.Map(envelope, text, modelId, DateTimeOffset.UtcNow);
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
    /// Runs the request like <see cref="GetResponseAsync"/> and yields the answer text as agy writes it, then one final update with
    /// usage and metadata. A request for a JSON response format (plain or with a schema) yields the whole checked answer as one
    /// update, because it can only be checked once complete. Errors surface on a <c>MoveNextAsync</c> call, the first one when nothing
    /// was yielded yet; ending the enumeration early ends the agy process.
    /// </summary>
    /// <inheritdoc cref="GetResponseAsync" path="/param"/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(messages);
        if (RequestValidator.Validate(messages, options).ResponseKind != ResponseKind.Text)
        {
            yield return OneUpdate(await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false));
            yield break;
        }

        Prepared prepared = await PrepareAsync(messages, options, cancellationToken).ConfigureAwait(false);
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();
        try
        {
            using RequestDirectory directory = RequestDirectory.Create(
                _options.TemporaryDirectoryRoot,
                (path, _) => LogMessages.RequestDirectoryNotDeleted(_logger, path));

            AgyInvocation invocation = InvocationBuilder.Build(
                prepared.Prompt,
                prepared.ModelId,
                prepared.Effort,
                null,
                _options.Timeout,
                _options.AdditionalArguments,
                _options.EnvironmentVariables,
                streamJson: true);
            if (invocation.EffortRequested && !invocation.EffortApplied)
            {
                LogMessages.EffortNotApplied(_logger, prepared.ModelId);
            }

            var lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<AgyProcessResult> run = Task.Run(
                async () =>
                {
                    try
                    {
                        return await _runner.RunStreamingAsync(
                            new AgyProcessRequest(
                                prepared.Agy.ExecutablePath,
                                invocation.Arguments,
                                directory.Path,
                                invocation.EnvironmentOverrides,
                                _options.Timeout,
                                _options.MaxOutputBytes),
                            line => lines.Writer.TryWrite(line),
                            stop.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        lines.Writer.TryComplete();
                    }
                },
                CancellationToken.None);

            try
            {
                string? conversationId = null;
                string? model = null;
                string? resultJson = null;
                bool malformed = false;
                var hold = new TrailingTerminatorHold();

                await foreach (string line in lines.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (resultJson is not null)
                    {
                        continue; // nothing is expected after the result event
                    }

                    if (!StreamEventReader.TryRead(line, out StreamEvent? streamEvent))
                    {
                        malformed = true;
                        break;
                    }

                    conversationId ??= streamEvent!.ConversationId;
                    switch (streamEvent!.Kind)
                    {
                        case StreamEventKind.Init:
                            model ??= streamEvent.Model;
                            break;
                        case StreamEventKind.Result:
                            resultJson = streamEvent.ResultJson;
                            break;
                        case StreamEventKind.Text:
                            string text = hold.Push(streamEvent.Text!);
                            if (text.Length > 0)
                            {
                                yield return new ChatResponseUpdate(ChatRole.Assistant, text) { ResponseId = conversationId };
                            }

                            break;
                    }
                }

                if (malformed)
                {
                    stop.Cancel();
                    throw FailedStream(
                        new AntigravityOutputException("agy's stream output could not be read as stream-json events."), watch);
                }

                AgyProcessResult result = await AwaitRunAsync(run, watch).ConfigureAwait(false);
                AgyEnvelope envelope = ClassifyStream(result, resultJson, watch);
                if (envelope.DeniedActionKinds.Count > 0)
                {
                    LogMessages.DeniedActionsWithAnswer(_logger, string.Join(", ", envelope.DeniedActionKinds));
                }

                string leftover = hold.Finish();
                if (leftover.Length > 0)
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, leftover) { ResponseId = conversationId };
                }

                ChatResponse response = ResponseMapper.Map(envelope, string.Empty, model ?? prepared.ModelId, DateTimeOffset.UtcNow);
                LogMessages.RequestCompleted(
                    _logger, response.ModelId, watch.ElapsedMilliseconds, prepared.Prompt.Length, envelope.Usage?.InputTokens, envelope.Usage?.OutputTokens);
                yield return OneUpdate(response, includeText: false);
            }
            finally
            {
                // Stopping early (break, cancellation, an exception above) must not leave agy running.
                stop.Cancel();
                try
                {
                    await run.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or AntigravityException)
                {
                    // Already reported to the caller, or the caller stopped; the process tree is gone either way.
                }
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    // The update for a whole response; the closing update of a streamed answer leaves the text out, which was already sent.
    private static ChatResponseUpdate OneUpdate(ChatResponse response, bool includeText = true)
    {
        var contents = new List<AIContent>(includeText ? response.Messages.SelectMany(m => m.Contents) : []);
        if (response.Usage is not null)
        {
            contents.Add(new UsageContent(response.Usage));
        }

        return new ChatResponseUpdate(ChatRole.Assistant, contents)
        {
            ResponseId = response.ResponseId,
            ModelId = response.ModelId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            RawRepresentation = response.RawRepresentation,
            AdditionalProperties = response.AdditionalProperties,
        };
    }

    private async Task<AgyProcessResult> AwaitRunAsync(Task<AgyProcessResult> run, Stopwatch watch)
    {
        try
        {
            return await run.ConfigureAwait(false);
        }
        catch (AntigravityException ex)
        {
            throw FailedStream(ex, watch);
        }
    }

    // The result event holds the fields of the JSON envelope, so the same classification applies as for a buffered run.
    private AgyEnvelope ClassifyStream(AgyProcessResult result, string? resultJson, Stopwatch watch)
    {
        try
        {
            return ErrorClassifier.Classify(result with { StandardOutput = resultJson ?? string.Empty }, _options.Timeout);
        }
        catch (AntigravityException ex)
        {
            throw FailedStream(ex, watch);
        }
    }

    private AntigravityException FailedStream(AntigravityException ex, Stopwatch watch)
    {
        LogMessages.RequestFailed(_logger, ex.GetType().Name, watch.ElapsedMilliseconds, ex.ExitCode);
        return ex;
    }

    private sealed record Prepared(
        ValidatedRequest Request,
        ICompiledSchema? Schema,
        string Prompt,
        ResolvedAgy Agy,
        string? ModelId,
        ReasoningEffort? Effort);

    // Everything that happens before a slot is taken and agy is started; shared by the buffered and the streaming call.
    private async Task<Prepared> PrepareAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        ValidatedRequest request = RequestValidator.Validate(messages, options);

        // An unusable schema must fail here, before agy is run and paid for.
        ICompiledSchema? schema = request.ResponseKind == ResponseKind.JsonSchema ? _schemaValidator.Compile(request.SchemaText!) : null;

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

        return new Prepared(request, schema, prompt, agy, modelId, effort);
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
