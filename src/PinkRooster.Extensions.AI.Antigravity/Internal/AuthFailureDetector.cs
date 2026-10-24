namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// Decides whether agy's diagnostics say it is not signed in. This is the only place that knows the wording.
/// The one entry below is the text the official headless documentation gives; no real "not signed in" output has been
/// captured yet (docs/specification.md F-19, open question Q5), so a different wording degrades to a process or run
/// exception, never to a silent success.
/// </summary>
internal static class AuthFailureDetector
{
    private static readonly string[] Markers =
    [
        "authentication required",
    ];

    public static bool Matches(params string?[] texts)
    {
        foreach (string? text in texts)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            foreach (string marker in Markers)
            {
                if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
