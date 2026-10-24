using Microsoft.Extensions.AI;

namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>
/// Options for <c>AntigravityChatClient</c>. Options are validated and copied when the client is created;
/// changing an instance afterwards has no effect on an existing client.
/// </summary>
public sealed class AntigravityChatClientOptions
{
    /// <summary>The preamble used when <see cref="SystemPreamble"/> is not replaced.</summary>
    public const string DefaultSystemPreamble =
        "Answer the request below directly in plain text. Do not use any tools, do not read, create, change or delete files, " +
        "and do not run commands. Your entire output is the reply to the final message.";

    /// <summary>
    /// Path of the agy executable. When null, the system search path and then the default install directory are searched.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>The agy model used when the request names none. Null leaves the choice to agy.</summary>
    public string? DefaultModelId { get; set; }

    /// <summary>The reasoning effort used when the request names none. Null leaves the choice to agy.</summary>
    public ReasoningEffort? DefaultReasoningEffort { get; set; }

    /// <summary>The longest a single request may run. Default 5 minutes; at least 1 second.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The most agy processes one client runs at once. Default 4; at least 1.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// The longest prompt, in characters, that is passed to agy. Default 100,000, and 30,000 on Windows.
    /// A platform limit on the prompt's size in bytes applies as well; the smaller limit wins.
    /// </summary>
    public int MaxPromptLength { get; set; } = OperatingSystem.IsWindows() ? 30_000 : 100_000;

    /// <summary>When true, the agy version is not read or checked.</summary>
    public bool SkipVersionCheck { get; set; }

    /// <summary>Environment variables added to the host environment for agy. A null value removes the variable.</summary>
    public IDictionary<string, string?> EnvironmentVariables { get; set; } = new Dictionary<string, string?>();

    /// <summary>The directory under which per-request directories are created. Null uses the system temporary directory.</summary>
    public string? TemporaryDirectoryRoot { get; set; }

    /// <summary>
    /// Extra command-line arguments for agy. Flags that the library sets itself or that would break safety or
    /// statelessness are rejected when the client is created.
    /// </summary>
    public IList<string> AdditionalArguments { get; set; } = new List<string>();

    /// <summary>The text placed at the start of every prompt. It must not be empty.</summary>
    public string SystemPreamble { get; set; } = DefaultSystemPreamble;

    /// <summary>The most bytes read from agy's standard output before the request fails. Default 16 MiB.</summary>
    public int MaxOutputBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// The most tool calls agy may make in one request. Calls beyond it get an error result and agy decides what to do. Default 20;
    /// at least 1. <c>FunctionInvokingChatClient</c> cannot limit these calls, because agy runs them itself.
    /// </summary>
    public int MaxToolCalls { get; set; } = 20;

    /// <summary>
    /// When true, every request runs agy with its own throwaway home directory (the <c>HOME</c> variable) and sends the
    /// system text in an <c>AGENTS.md</c> file in the request directory. agy then sees none of the user's own agy rules,
    /// skills, settings or MCP servers, and nothing of the request stays in agy's history. Costs about 18 MB written per
    /// request. The default is true on Linux, the only platform where this is verified, and false elsewhere; setting it to true
    /// on another platform has no effect. When true on Linux, <see cref="EnvironmentVariables"/> may not set <c>HOME</c>.
    /// Setting it to false runs agy in the user's own home as before.
    /// </summary>
    public bool IsolateAgyHome { get; set; } = OperatingSystem.IsLinux();
}
