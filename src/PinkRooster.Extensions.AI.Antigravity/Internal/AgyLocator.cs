namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>Finds the agy executable.</summary>
internal static class AgyLocator
{
    private static readonly string[] BatchExtensions = [".cmd", ".bat"];

    /// <summary>Resolves the executable for the current machine.</summary>
    public static string Resolve(string? explicitPath) =>
        Resolve(
            explicitPath,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("LOCALAPPDATA"),
            OperatingSystem.IsWindows(),
            File.Exists);

    /// <summary>Resolves the executable from the given inputs; the overload above supplies the real ones.</summary>
    /// <exception cref="AntigravityNotFoundException">Nothing was found. The exception lists what was searched.</exception>
    /// <exception cref="ArgumentException">The explicit path names a batch file, which is never launched.</exception>
    public static string Resolve(
        string? explicitPath,
        string? pathVariable,
        string? homeDirectory,
        string? localAppData,
        bool isWindows,
        Func<string, bool> fileExists)
    {
        List<string> searched = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            RejectBatchFile(explicitPath);
            if (fileExists(explicitPath))
            {
                return explicitPath;
            }

            searched.Add(explicitPath);
            throw NotFound(searched);
        }

        string[] names = isWindows ? ["agy.exe"] : ["agy"];
        char separator = isWindows ? ';' : ':';

        foreach (string directory in (pathVariable ?? string.Empty).Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (string name in names)
            {
                string candidate = Path.Combine(trimmed, name);
                searched.Add(candidate);
                if (fileExists(candidate))
                {
                    return candidate;
                }
            }
        }

        string? defaultDirectory = isWindows
            ? (string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "agy", "bin"))
            : (string.IsNullOrEmpty(homeDirectory) ? null : Path.Combine(homeDirectory, ".local", "bin"));

        if (defaultDirectory is not null)
        {
            string candidate = Path.Combine(defaultDirectory, names[0]);
            searched.Add(candidate);
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        throw NotFound(searched);
    }

    private static void RejectBatchFile(string path)
    {
        foreach (string extension in BatchExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "ExecutablePath must be the agy executable, not a batch file: batch files are not launched.", nameof(path));
            }
        }
    }

    private static AntigravityNotFoundException NotFound(List<string> searched) =>
        new(
            "The agy executable was not found. Install the Antigravity CLI, put it on the PATH, or set ExecutablePath. Searched: "
            + (searched.Count == 0 ? "(nothing)" : string.Join(", ", searched)),
            searched);
}
