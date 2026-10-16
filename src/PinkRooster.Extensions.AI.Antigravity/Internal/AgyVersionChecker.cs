namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>An agy that was found and, unless skipped, version-checked.</summary>
internal sealed record ResolvedAgy(string ExecutablePath, Version? Version, bool NewerThanTested);

/// <summary>
/// Locates agy and reads its version once. A success is cached; a failure is not, so installing agy later works
/// without restarting the application.
/// </summary>
internal sealed class AgyVersionChecker
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);

    private readonly IAgyProcessRunner _runner;
    private readonly string? _explicitPath;
    private readonly bool _skipVersionCheck;
    private readonly string? _temporaryRoot;
    private readonly IReadOnlyDictionary<string, string?> _environment;
    private readonly Func<string?, string> _resolvePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ResolvedAgy? _resolved;

    public AgyVersionChecker(
        IAgyProcessRunner runner,
        AntigravityChatClientOptions options,
        Func<string?, string>? resolvePath = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);
        _runner = runner;
        _explicitPath = options.ExecutablePath;
        _skipVersionCheck = options.SkipVersionCheck;
        _temporaryRoot = options.TemporaryDirectoryRoot;
        _environment = new Dictionary<string, string?>(options.EnvironmentVariables, StringComparer.Ordinal);
        _resolvePath = resolvePath ?? AgyLocator.Resolve;
    }

    public async Task<ResolvedAgy> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        ResolvedAgy? cached = Volatile.Read(ref _resolved);
        if (cached is not null)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resolved is not null)
            {
                return _resolved;
            }

            string path = _resolvePath(_explicitPath);
            ResolvedAgy resolved = _skipVersionCheck
                ? new ResolvedAgy(path, null, false)
                : await CheckVersionAsync(path, cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref _resolved, resolved);
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ResolvedAgy> CheckVersionAsync(string path, CancellationToken cancellationToken)
    {
        using RequestDirectory directory = RequestDirectory.Create(_temporaryRoot);
        var request = new AgyProcessRequest(path, ["--version"], directory.Path, _environment, VersionTimeout, 64 * 1024);
        AgyProcessResult result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.TimedOut || result.OutputLimitExceeded || result.ExitCode != 0
            || !AgyVersions.TryParse(result.StandardOutput, out Version found))
        {
            throw new AntigravityVersionException(
                "The version of agy could not be read from 'agy --version'.",
                foundVersion: null,
                AgyVersions.Minimum)
            {
                ExitCode = result.ExitCode,
                StandardError = Truncate(result.StandardError),
            };
        }

        if (found < Version.Parse(AgyVersions.Minimum))
        {
            throw new AntigravityVersionException(
                $"agy {found} is older than the minimum supported version {AgyVersions.Minimum}. Update the Antigravity CLI.",
                found.ToString(),
                AgyVersions.Minimum);
        }

        return new ResolvedAgy(path, found, found > Version.Parse(AgyVersions.NewestTested));
    }

    private static string? Truncate(string text) =>
        text.Length <= 4096 ? text : text[..4096];
}
