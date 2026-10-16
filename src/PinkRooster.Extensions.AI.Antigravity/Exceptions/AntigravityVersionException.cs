namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>The installed agy is older than the minimum version, or its version could not be read.</summary>
public sealed class AntigravityVersionException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityVersionException(string message, string? foundVersion, string requiredVersion)
        : base(message)
    {
        FoundVersion = foundVersion;
        RequiredVersion = requiredVersion;
    }

    /// <summary>The version agy reported, or null when none could be read.</summary>
    public string? FoundVersion { get; }

    /// <summary>The minimum version this library supports.</summary>
    public string RequiredVersion { get; }
}
