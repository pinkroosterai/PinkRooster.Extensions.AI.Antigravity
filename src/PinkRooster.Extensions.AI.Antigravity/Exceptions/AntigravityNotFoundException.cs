namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>The agy executable could not be found.</summary>
public sealed class AntigravityNotFoundException : AntigravityException
{
    /// <summary>Creates the exception.</summary>
    public AntigravityNotFoundException(string message, IReadOnlyList<string> searchedLocations)
        : base(message)
    {
        SearchedLocations = searchedLocations;
    }

    /// <summary>The places that were searched.</summary>
    public IReadOnlyList<string> SearchedLocations { get; }
}
