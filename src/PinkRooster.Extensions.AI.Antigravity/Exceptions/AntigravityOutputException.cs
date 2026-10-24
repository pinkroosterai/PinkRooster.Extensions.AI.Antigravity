namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>agy's output could not be read as the expected JSON envelope, or it exceeded the output limit.</summary>
public sealed class AntigravityOutputException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityOutputException(string message)
        : base(message)
    {
    }
}
