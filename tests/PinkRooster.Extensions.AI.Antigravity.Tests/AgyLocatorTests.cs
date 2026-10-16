using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public sealed class AgyLocatorTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "agy-locator-" + Guid.NewGuid().ToString("N"));

    public AgyLocatorTests() => Directory.CreateDirectory(_scratch);

    public void Dispose() => Directory.Delete(_scratch, recursive: true);

    private static Func<string, bool> Exists(params string[] files) => path => files.Contains(path);

    private static string P(params string[] parts) => Path.Combine(parts);

    [Fact]
    public void Explicit_path_that_exists_is_returned_without_searching()
    {
        string file = P(_scratch, "my-agy");
        File.WriteAllText(file, "x");

        Assert.Equal(file, AgyLocator.Resolve(file));
    }

    [Fact]
    public void Explicit_path_that_does_not_exist_raises_NotFound_naming_it()
    {
        string file = P(_scratch, "missing");

        var ex = Assert.Throws<AntigravityNotFoundException>(() => AgyLocator.Resolve(file));

        Assert.Equal([file], ex.SearchedLocations);
        Assert.Contains(file, ex.Message);
    }

    [Theory]
    [InlineData("/x/agy.cmd")]
    [InlineData("C:\\x\\agy.BAT")]
    public void Batch_files_are_refused_even_when_they_exist(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            AgyLocator.Resolve(path, null, null, null, isWindows: true, _ => true));
    }

    [Fact]
    public void Search_path_is_searched_in_order_and_first_hit_wins()
    {
        string first = P("/opt/a", "agy");
        string second = P("/opt/b", "agy");

        string found = AgyLocator.Resolve(null, "/opt/a:/opt/b", "/home/u", null, false, Exists(first, second));

        Assert.Equal(first, found);
    }

    [Fact]
    public void Later_path_entry_is_used_when_the_first_has_no_agy()
    {
        string second = P("/opt/b", "agy");

        Assert.Equal(second, AgyLocator.Resolve(null, "/opt/a:/opt/b", "/home/u", null, false, Exists(second)));
    }

    [Fact]
    public void Default_install_directory_is_used_on_unix_when_the_path_has_none()
    {
        string home = P("/home", "u", ".local", "bin", "agy");

        Assert.Equal(home, AgyLocator.Resolve(null, "/opt/a", "/home/u", null, false, Exists(home)));
    }

    [Fact]
    public void Windows_looks_for_exe_on_the_path_then_in_local_app_data()
    {
        string onPath = P("C:\\tools", "agy.exe");
        Assert.Equal(onPath, AgyLocator.Resolve(null, "C:\\bin;C:\\tools", null, "C:\\Users\\u\\AppData\\Local", true, Exists(onPath)));

        string local = P("C:\\Users\\u\\AppData\\Local", "agy", "bin", "agy.exe");
        Assert.Equal(local, AgyLocator.Resolve(null, "C:\\bin", null, "C:\\Users\\u\\AppData\\Local", true, Exists(local)));
    }

    [Fact]
    public void Windows_does_not_accept_a_batch_shim_on_the_path()
    {
        string shim = P("C:\\tools", "agy.cmd");

        Assert.Throws<AntigravityNotFoundException>(() =>
            AgyLocator.Resolve(null, "C:\\tools", null, null, true, Exists(shim)));
    }

    [Fact]
    public void Nothing_found_raises_NotFound_listing_every_place_searched()
    {
        var ex = Assert.Throws<AntigravityNotFoundException>(() =>
            AgyLocator.Resolve(null, "/opt/a::/opt/b", "/home/u", null, false, _ => false));

        Assert.Equal([P("/opt/a", "agy"), P("/opt/b", "agy"), P("/home/u", ".local", "bin", "agy")], ex.SearchedLocations);
        foreach (string place in ex.SearchedLocations)
        {
            Assert.Contains(place, ex.Message);
        }
    }

    [Fact]
    public void Missing_path_variable_and_home_still_end_in_NotFound()
    {
        var ex = Assert.Throws<AntigravityNotFoundException>(() => AgyLocator.Resolve(null, null, null, null, false, _ => false));

        Assert.Empty(ex.SearchedLocations);
    }

    [Fact]
    public void Blank_explicit_path_falls_back_to_the_search()
    {
        string file = P("/opt/a", "agy");

        Assert.Equal(file, AgyLocator.Resolve("  ", "/opt/a", null, null, false, Exists(file)));
    }
}
