using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class VersionCheckerTests
{
    private const string Agy = "/fake/agy";

    private static AgyVersionChecker Checker(FakeProcessRunner runner, Action<AntigravityChatClientOptions>? configure = null)
    {
        var options = new AntigravityChatClientOptions();
        configure?.Invoke(options);
        return new AgyVersionChecker(runner, options, _ => Agy);
    }

    [Fact]
    public async Task Supported_version_is_accepted_and_the_executable_returned()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5\n"));

        ResolvedAgy resolved = await Checker(runner).EnsureReadyAsync(CancellationToken.None);

        Assert.Equal(Agy, resolved.ExecutablePath);
        Assert.Equal(new Version(1, 3, 5), resolved.Version);
        Assert.False(resolved.NewerThanTested);
    }

    [Fact]
    public async Task The_recorded_agy_output_is_read_as_a_supported_version()
    {
        string recorded = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "version-1.3.5.txt"));
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok(recorded));

        ResolvedAgy resolved = await Checker(runner).EnsureReadyAsync(CancellationToken.None);

        Assert.Equal(new Version(1, 3, 5), resolved.Version);
    }

    [Fact]
    public async Task Version_is_read_with_the_version_flag_in_an_empty_directory_that_is_removed_afterwards()
    {
        var runner = new FakeProcessRunner().Returns(request =>
        {
            Assert.True(Directory.Exists(request.WorkingDirectory));
            Assert.Empty(Directory.EnumerateFileSystemEntries(request.WorkingDirectory));
            return FakeProcessRunner.Ok("1.3.5");
        });

        await Checker(runner).EnsureReadyAsync(CancellationToken.None);

        AgyProcessRequest request = Assert.Single(runner.Requests);
        Assert.Equal(["--version"], request.Arguments);
        Assert.Equal(Agy, request.ExecutablePath);
        Assert.False(Directory.Exists(request.WorkingDirectory));
    }

    [Fact]
    public async Task Newer_version_is_accepted_and_flagged()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.4.0"));

        ResolvedAgy resolved = await Checker(runner).EnsureReadyAsync(CancellationToken.None);

        Assert.Equal(new Version(1, 4, 0), resolved.Version);
        Assert.True(resolved.NewerThanTested);
    }

    [Theory]
    [InlineData("1.3.4")]
    [InlineData("1.0.9")]
    [InlineData("0.9.99")]
    public async Task Older_version_raises_VersionException_with_found_and_required(string output)
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok(output));

        var ex = await Assert.ThrowsAsync<AntigravityVersionException>(() => Checker(runner).EnsureReadyAsync(CancellationToken.None));

        Assert.Equal(output, ex.FoundVersion);
        Assert.Equal("1.3.5", ex.RequiredVersion);
        Assert.Contains("1.3.5", ex.Message);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("1.3")]
    public async Task Unreadable_output_raises_VersionException(string output)
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok(output));

        var ex = await Assert.ThrowsAsync<AntigravityVersionException>(() => Checker(runner).EnsureReadyAsync(CancellationToken.None));

        Assert.Null(ex.FoundVersion);
    }

    [Fact]
    public async Task Non_zero_exit_raises_VersionException_with_exit_code_and_stderr_but_not_in_the_message()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5", "SECRET-STDERR", exitCode: 2));

        var ex = await Assert.ThrowsAsync<AntigravityVersionException>(() => Checker(runner).EnsureReadyAsync(CancellationToken.None));

        Assert.Equal(2, ex.ExitCode);
        Assert.Equal("SECRET-STDERR", ex.StandardError);
        Assert.DoesNotContain("SECRET-STDERR", ex.Message);
    }

    [Fact]
    public async Task Timeout_of_the_version_run_raises_VersionException()
    {
        var runner = new FakeProcessRunner().Returns(new AgyProcessResult(null, "", "", TimedOut: true, OutputLimitExceeded: false, TimeSpan.FromSeconds(30)));

        await Assert.ThrowsAsync<AntigravityVersionException>(() => Checker(runner).EnsureReadyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_success_is_cached_for_the_life_of_the_checker()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"));
        AgyVersionChecker checker = Checker(runner);

        ResolvedAgy first = await checker.EnsureReadyAsync(CancellationToken.None);
        ResolvedAgy second = await checker.EnsureReadyAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task A_failure_is_not_cached_so_a_later_install_works()
    {
        var runner = new FakeProcessRunner()
            .Returns(FakeProcessRunner.Ok("1.0.0"))
            .Returns(FakeProcessRunner.Ok("1.3.5"));
        AgyVersionChecker checker = Checker(runner);

        await Assert.ThrowsAsync<AntigravityVersionException>(() => checker.EnsureReadyAsync(CancellationToken.None));
        ResolvedAgy second = await checker.EnsureReadyAsync(CancellationToken.None);

        Assert.Equal(new Version(1, 3, 5), second.Version);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task Concurrent_first_callers_share_one_version_run()
    {
        var runner = new FakeProcessRunner().Returns(request =>
        {
            Thread.Sleep(50);
            return FakeProcessRunner.Ok("1.3.5");
        });
        AgyVersionChecker checker = Checker(runner);

        ResolvedAgy[] all = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => checker.EnsureReadyAsync(CancellationToken.None))));

        Assert.Single(runner.Requests);
        Assert.All(all, r => Assert.Equal(Agy, r.ExecutablePath));
    }

    [Fact]
    public async Task Skipping_the_check_resolves_the_path_but_never_runs_agy()
    {
        var runner = new FakeProcessRunner();

        ResolvedAgy resolved = await Checker(runner, o => o.SkipVersionCheck = true).EnsureReadyAsync(CancellationToken.None);

        Assert.Equal(Agy, resolved.ExecutablePath);
        Assert.Null(resolved.Version);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Missing_executable_propagates_NotFound_before_any_run()
    {
        var runner = new FakeProcessRunner();
        var checker = new AgyVersionChecker(runner, new AntigravityChatClientOptions(),
            _ => throw new AntigravityNotFoundException("missing", ["/x"]));

        await Assert.ThrowsAsync<AntigravityNotFoundException>(() => checker.EnsureReadyAsync(CancellationToken.None));

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Explicit_path_option_is_handed_to_the_resolver()
    {
        string? seen = "unset";
        var options = new AntigravityChatClientOptions { ExecutablePath = "/custom/agy" };
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"));
        var checker = new AgyVersionChecker(runner, options, path => { seen = path; return "/custom/agy"; });

        await checker.EnsureReadyAsync(CancellationToken.None);

        Assert.Equal("/custom/agy", seen);
    }

    [Fact]
    public async Task Environment_variables_reach_the_version_run()
    {
        var runner = new FakeProcessRunner().Returns(FakeProcessRunner.Ok("1.3.5"));

        await Checker(runner, o => o.EnvironmentVariables["AGY_ADC_AUTH"] = "true").EnsureReadyAsync(CancellationToken.None);

        Assert.Equal("true", runner.Requests[0].EnvironmentOverrides["AGY_ADC_AUTH"]);
    }

    [Fact]
    public async Task A_cancelled_token_raises_OperationCanceled_and_starts_nothing()
    {
        var runner = new FakeProcessRunner();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Checker(runner).EnsureReadyAsync(cts.Token));

        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("1.3.5", true, "1.3.5")]
    [InlineData("agy version 2.10.0 (build abc)", true, "2.10.0")]
    [InlineData("v1.4.0-beta.2", true, "1.4.0")]
    [InlineData("1.3", false, null)]
    [InlineData("no digits", false, null)]
    [InlineData(null, false, null)]
    public void Version_text_parser_reads_the_first_three_part_number(string? output, bool ok, string? expected)
    {
        Assert.Equal(ok, AgyVersions.TryParse(output, out Version version));
        if (ok)
        {
            Assert.Equal(Version.Parse(expected!), version);
        }
    }
}
