using Microsoft.Extensions.Logging;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// The only way the library writes to a log. Every parameter is a number, a version, a model name, an option name or an
/// exception type name: there is no parameter that can carry a prompt, an answer, a schema, standard error or an
/// environment value (docs/software-design.md section 7.3).
/// </summary>
internal static partial class LogMessages
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Ignored ChatOptions members (names only): {OptionNames}.")]
    public static partial void IgnoredOptions(ILogger logger, string optionNames);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Reasoning effort not passed: model '{ModelId}' already carries a level, or effort was not mappable.")]
    public static partial void EffortNotApplied(ILogger logger, string? modelId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "agy {Version} is newer than the newest version this library was tested with ({NewestTested}).")]
    public static partial void AgyNewerThanTested(ILogger logger, string version, string newestTested);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "agy request completed: model {ModelId}, {ElapsedMs} ms, prompt {PromptLength} characters, {InputTokens} input and {OutputTokens} output tokens.")]
    public static partial void RequestCompleted(ILogger logger, string? modelId, long elapsedMs, int promptLength, long? inputTokens, long? outputTokens);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "agy request failed: {ExceptionType} after {ElapsedMs} ms (exit code {ExitCode}).")]
    public static partial void RequestFailed(ILogger logger, string exceptionType, long elapsedMs, int? exitCode);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "agy denied actions ({ActionKinds}) but still returned an answer.")]
    public static partial void DeniedActionsWithAnswer(ILogger logger, string actionKinds);

    [LoggerMessage(EventId = 8, Level = LogLevel.Debug, Message = "IsolateAgyHome is true but isolation is only applied on Linux; requests run in the user's own agy home.")]
    public static partial void IsolationNotSupported(ILogger logger);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "The request directory '{Path}' could not be deleted.")]
    public static partial void RequestDirectoryNotDeleted(ILogger logger, string path);
}
