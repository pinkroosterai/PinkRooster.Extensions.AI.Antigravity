using System.Text;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// The <c>AGENTS.md</c> that carries a request's system text. agy applies it as a system-level rule and truncates a rule file
/// above 24,000 bytes, so a longer text is refused instead of being cut silently.
/// </summary>
internal static class AgentsFile
{
    public const string FileName = "AGENTS.md";

    /// <summary>The size above which agy truncates a rule file, in UTF-8 bytes (agy rules documentation).</summary>
    public const int MaxBytes = 24_000;

    /// <summary>The file content: the preamble, then each system text, separated by blank lines.</summary>
    public static string Build(ValidatedRequest request, string preamble)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(preamble);

        var sb = new StringBuilder();
        sb.Append(preamble.TrimEnd()).Append('\n');
        foreach (string system in request.SystemTexts)
        {
            sb.Append('\n').Append(system.TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Throws <see cref="AntigravityPromptTooLargeException"/> when the content is larger than agy reads whole.</summary>
    public static void Check(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        int bytes = Encoding.UTF8.GetByteCount(content);
        if (bytes > MaxBytes)
        {
            throw new AntigravityPromptTooLargeException(
                $"The system text is {bytes} bytes in UTF-8; agy reads at most {MaxBytes} bytes of a rule file.",
                bytes,
                MaxBytes);
        }
    }
}
