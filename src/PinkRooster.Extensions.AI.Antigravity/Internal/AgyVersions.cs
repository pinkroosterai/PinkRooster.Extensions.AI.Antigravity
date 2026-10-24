using System.Text.RegularExpressions;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>The agy versions this library knows about, and how to read one.</summary>
internal static partial class AgyVersions
{
    /// <summary>The oldest agy the library supports: the only version whose behavior was verified.</summary>
    public const string Minimum = "1.3.5";

    /// <summary>The newest agy the library was tested against. A newer one is accepted with one information entry.</summary>
    public const string NewestTested = "1.3.5";

    /// <summary>Reads the first dotted three-part number from the output of <c>agy --version</c>.</summary>
    public static bool TryParse(string? output, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        Match match = VersionPattern().Match(output);
        return match.Success && Version.TryParse(match.Value, out version!);
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionPattern();
}
