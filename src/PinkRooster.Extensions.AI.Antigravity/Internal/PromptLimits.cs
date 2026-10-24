using System.Text;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// The size limits a prompt must respect. The prompt travels as one command-line argument, so the platform limits it
/// in bytes (Linux, macOS) or UTF-16 units (Windows) independently of the caller's character limit.
/// </summary>
internal sealed record PromptLimits(int MaxCharacters, int? MaxUtf8Bytes, int? MaxUtf16Units)
{
    // Conservative values, unverified beyond the observation that a 150,000-byte argument fails on Linux
    // (docs/software-design.md F-11, section 5.7). The process tests probe the real limits.
    public const int DefaultMaxUtf8Bytes = 120_000;
    public const int DefaultMaxUtf16Units = 30_000;

    public static PromptLimits ForCurrentPlatform(int maxCharacters) =>
        OperatingSystem.IsWindows()
            ? new PromptLimits(maxCharacters, null, DefaultMaxUtf16Units)
            : new PromptLimits(maxCharacters, DefaultMaxUtf8Bytes, null);

    /// <summary>Throws <see cref="AntigravityPromptTooLargeException"/> when the prompt exceeds a limit.</summary>
    public void Check(string prompt)
    {
        if (prompt.Length > MaxCharacters)
        {
            throw new AntigravityPromptTooLargeException(
                $"The prompt has {prompt.Length} characters; the limit is {MaxCharacters} (MaxPromptLength).",
                prompt.Length,
                MaxCharacters);
        }

        if (MaxUtf16Units is int units && prompt.Length > units)
        {
            throw new AntigravityPromptTooLargeException(
                $"The prompt has {prompt.Length} UTF-16 units; the platform limit is {units}.",
                prompt.Length,
                units);
        }

        if (MaxUtf8Bytes is int bytes)
        {
            int actual = Encoding.UTF8.GetByteCount(prompt);
            if (actual > bytes)
            {
                throw new AntigravityPromptTooLargeException(
                    $"The prompt is {actual} bytes in UTF-8; the platform limit is {bytes}.",
                    actual,
                    bytes);
            }
        }
    }
}
