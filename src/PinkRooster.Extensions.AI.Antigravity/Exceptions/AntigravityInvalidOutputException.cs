namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>The structured result is missing, is not JSON, or does not match the requested schema.</summary>
public sealed class AntigravityInvalidOutputException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityInvalidOutputException(string message, string? rawOutput)
        : base(message)
    {
        RawOutput = rawOutput;
    }

    /// <summary>
    /// What agy returned in place of a valid result. Not part of the message; it is model output and may be sensitive.
    /// </summary>
    public string? RawOutput { get; }
}
