namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>agy reported that it is not signed in. Start <c>agy</c> once interactively to sign in.</summary>
public sealed class AntigravityNotAuthenticatedException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityNotAuthenticatedException(string message)
        : base(message)
    {
    }
}
