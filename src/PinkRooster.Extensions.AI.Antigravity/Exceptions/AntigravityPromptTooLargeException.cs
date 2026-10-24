namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>The prompt is longer than the configured or platform limit.</summary>
public sealed class AntigravityPromptTooLargeException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityPromptTooLargeException(string message, long length, long limit)
        : base(message)
    {
        Length = length;
        Limit = limit;
    }

    /// <summary>The size of the prompt, in the unit of the limit that was hit.</summary>
    public long Length { get; }

    /// <summary>The limit that was hit.</summary>
    public long Limit { get; }
}
