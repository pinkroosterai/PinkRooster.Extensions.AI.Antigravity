using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests.Support;

/// <summary>Reads the recorded agy outputs under <c>Fixtures</c> (see its README for their provenance).</summary>
internal static class Fixture
{
    public static string Text(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>A finished process built from a stdout fixture and, optionally, a stderr fixture.</summary>
    public static AgyProcessResult Result(string stdoutFixture, int exitCode = 0, string? stderrFixture = null, bool timedOut = false, bool overflow = false) =>
        new(
            timedOut || overflow ? null : exitCode,
            stdoutFixture.Length == 0 ? string.Empty : Text(stdoutFixture),
            stderrFixture is null ? string.Empty : Text(stderrFixture),
            timedOut,
            overflow,
            TimeSpan.FromMilliseconds(10));
}
