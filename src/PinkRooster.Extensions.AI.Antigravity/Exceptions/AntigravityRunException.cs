namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>
/// agy ended with exit code 0 but did not succeed: its status was not <c>SUCCESS</c>, or the answer was empty
/// because agy denied an action.
/// </summary>
public sealed class AntigravityRunException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityRunException(string message, IReadOnlyList<string>? deniedActions = null)
        : base(message)
    {
        DeniedActions = deniedActions ?? Array.Empty<string>();
    }

    /// <summary>The kinds of action agy denied, when it reported any.</summary>
    public IReadOnlyList<string> DeniedActions { get; }
}
