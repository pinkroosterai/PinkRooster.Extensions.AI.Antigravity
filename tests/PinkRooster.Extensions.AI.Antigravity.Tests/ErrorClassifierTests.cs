using PinkRooster.Extensions.AI.Antigravity.Internal;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class ErrorClassifierTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private static AgyEnvelope Classify(AgyProcessResult result) => ErrorClassifier.Classify(result, Timeout);

    private static AgyProcessResult Raw(int? exit, string stdout, string stderr = "", bool timedOut = false, bool overflow = false) =>
        new(exit, stdout, stderr, timedOut, overflow, TimeSpan.FromMilliseconds(5));

    [Fact]
    public void A_clean_success_returns_the_envelope()
    {
        AgyEnvelope e = Classify(Fixture.Result("success-text.json"));

        Assert.Equal("pong\n", e.Response);
    }

    [Fact]
    public void A_success_whose_stderr_merely_mentions_authentication_is_still_a_success()
    {
        AgyEnvelope e = Classify(Raw(0, Fixture.Text("success-text.json"), "note: authentication required for plugin X"));

        Assert.True(e.IsSuccess);
    }

    [Fact]
    public void An_empty_success_without_denial_or_notice_is_a_success_with_empty_text()
    {
        AgyEnvelope e = Classify(Raw(0, Fixture.Text("timeout-empty-success.json")));

        Assert.True(e.IsSuccess);
        Assert.Equal(string.Empty, e.Response);
    }

    [Fact]
    public void A_non_empty_answer_with_denials_succeeds_and_keeps_the_denial()
    {
        AgyEnvelope e = Classify(Fixture.Result("denied-with-answer.json"));

        Assert.Equal(["command"], e.DeniedActionKinds);
    }

    [Fact]
    public void Success_without_structured_output_is_still_a_success_here_because_the_structured_checker_decides_that()
    {
        Assert.True(Classify(Fixture.Result("schema-missing-structured.json")).IsSuccess);
    }

    // Timeout

    [Fact]
    public void Our_timeout_raises_Timeout_with_the_configured_value()
    {
        var ex = Assert.Throws<AntigravityTimeoutException>(() => Classify(Raw(null, "", "", timedOut: true)));

        Assert.Equal(Timeout, ex.Timeout);
        Assert.Contains("5 minutes", ex.Message);
    }

    [Fact]
    public void Agys_own_timeout_notice_with_exit_zero_is_a_timeout_not_an_empty_success()
    {
        var ex = Assert.Throws<AntigravityTimeoutException>(() => Classify(Fixture.Result("timeout-empty-success.json", stderrFixture: "timeout.stderr.txt")));

        Assert.Contains("print timeout after", ex.StandardError);
    }

    [Fact]
    public void The_timeout_notice_is_matched_regardless_of_case()
    {
        Assert.Throws<AntigravityTimeoutException>(() => Classify(Raw(0, Fixture.Text("timeout-empty-success.json"), "[AGY] PRINT TIMEOUT AFTER 9s")));
    }

    [Fact]
    public void Short_timeouts_are_worded_in_seconds()
    {
        var ex = Assert.Throws<AntigravityTimeoutException>(() => ErrorClassifier.Classify(Raw(null, "", "", timedOut: true), TimeSpan.FromSeconds(30)));

        Assert.Contains("30 seconds", ex.Message);
    }

    // Output limit

    [Fact]
    public void Output_over_the_limit_raises_Output()
    {
        Assert.Throws<AntigravityOutputException>(() => Classify(Raw(null, "partial", "", overflow: true)));
    }

    [Fact]
    public void The_output_limit_outranks_a_timeout()
    {
        Assert.Throws<AntigravityOutputException>(() => Classify(Raw(null, "", "", timedOut: true, overflow: true)));
    }

    // Not signed in

    [Fact]
    public void The_documented_sign_in_text_maps_to_NotAuthenticated_from_the_envelope()
    {
        var ex = Assert.Throws<AntigravityNotAuthenticatedException>(() =>
            Classify(Fixture.Result("not-signed-in.SYNTHETIC.json", exitCode: 1, stderrFixture: "not-signed-in.SYNTHETIC.stderr.txt")));

        Assert.Contains("agy", ex.Message);
        Assert.Contains("interactively", ex.Message);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public void The_documented_sign_in_text_maps_to_NotAuthenticated_from_stderr_alone()
    {
        Assert.Throws<AntigravityNotAuthenticatedException>(() => Classify(Raw(1, "", "Error: Authentication Required")));
    }

    [Fact]
    public void The_documented_sign_in_text_maps_to_NotAuthenticated_when_exit_is_zero_but_status_is_error()
    {
        Assert.Throws<AntigravityNotAuthenticatedException>(() =>
            Classify(Raw(0, """{"status":"ERROR","error":"authentication required"}""")));
    }

    [Fact]
    public void A_timeout_outranks_the_sign_in_text()
    {
        Assert.Throws<AntigravityTimeoutException>(() => Classify(Raw(null, "", "authentication required", timedOut: true)));
    }

    // Non-zero exit

    [Fact]
    public void A_non_zero_exit_raises_Process_with_the_envelope_error_in_the_message_and_stderr_on_the_property()
    {
        var ex = Assert.Throws<AntigravityProcessException>(() =>
            Classify(Fixture.Result("exit1-bad-model.json", exitCode: 1, stderrFixture: "exit1-bad-model.stderr.txt")));

        Assert.Equal(1, ex.ExitCode);
        Assert.Contains("code 1", ex.Message);
        Assert.Contains("conflicts with --effort", ex.Message);
        Assert.Contains("invalid model selection", ex.StandardError);
    }

    [Fact]
    public void A_non_zero_exit_with_unreadable_stdout_uses_the_first_stderr_line()
    {
        var ex = Assert.Throws<AntigravityProcessException>(() =>
            Classify(Fixture.Result("", exitCode: 1, stderrFixture: "exit1-bad-schema.stderr.txt")));

        Assert.Contains("invalid --json-schema", ex.Message);
        Assert.DoesNotContain("\n", ex.Message);
    }

    [Fact]
    public void A_non_zero_exit_for_an_empty_prompt_is_a_process_error()
    {
        var ex = Assert.Throws<AntigravityProcessException>(() => Classify(Fixture.Result("exit1-empty-prompt.json", exitCode: 1)));

        Assert.Contains("empty prompt", ex.Message);
    }

    [Fact]
    public void The_cancelled_run_of_a_terminated_agy_is_a_process_error()
    {
        var ex = Assert.Throws<AntigravityProcessException>(() =>
            Classify(Fixture.Result("cancel-context-canceled.json", exitCode: 1, stderrFixture: "cancel-context-canceled.stderr.txt")));

        Assert.Contains("context canceled", ex.Message);
    }

    [Fact]
    public void Detail_in_the_message_is_cut_to_200_characters()
    {
        string error = new string('x', 500);

        var ex = Assert.Throws<AntigravityProcessException>(() => Classify(Raw(1, $$"""{"status":"ERROR","error":"{{error}}"}""")));

        Assert.True(ex.Message.Length < 300);
        Assert.DoesNotContain(new string('x', 201), ex.Message);
    }

    [Fact]
    public void A_missing_exit_code_without_a_timeout_flag_is_still_a_process_error()
    {
        var ex = Assert.Throws<AntigravityProcessException>(() => Classify(Raw(null, "")));

        Assert.Contains("no exit code", ex.Message);
    }

    [Fact]
    public void An_exit_code_from_an_external_kill_is_a_process_error()
    {
        Assert.Throws<AntigravityProcessException>(() => Classify(Raw(137, "")));
    }

    // Unreadable output

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Exit_zero_with_output_that_is_not_an_envelope_raises_Output(string stdout)
    {
        var ex = Assert.Throws<AntigravityOutputException>(() => Classify(Raw(0, stdout, "SECRET-STDERR")));

        Assert.Equal("SECRET-STDERR", ex.StandardError);
        Assert.DoesNotContain("SECRET-STDERR", ex.Message);
    }

    // Status

    [Fact]
    public void Exit_zero_with_status_ERROR_raises_Run_with_the_first_line_of_the_error()
    {
        var ex = Assert.Throws<AntigravityRunException>(() => Classify(Fixture.Result("error-safety-block.json")));

        Assert.Contains("status ERROR", ex.Message);
        Assert.Contains("blocked by content safety filters", ex.Message);
        Assert.DoesNotContain("\n", ex.Message);
        Assert.Equal(0, ex.ExitCode);
    }

    [Fact]
    public void Status_ERROR_is_a_failure_even_though_a_structured_result_is_present()
    {
        AgyEnvelope e = EnvelopeParserTestsHelper.Parse(Fixture.Text("error-safety-block.json"));
        Assert.NotNull(e.StructuredOutput);

        Assert.Throws<AntigravityRunException>(() => Classify(Fixture.Result("error-safety-block.json")));
    }

    [Theory]
    [InlineData("CANCELED")]
    [InlineData("INTERRUPTED")]
    [InlineData("WAITING")]
    [InlineData("RUNNING")]
    [InlineData("INVALID")]
    [InlineData("success")]
    public void Any_status_other_than_exactly_SUCCESS_raises_Run(string status)
    {
        var ex = Assert.Throws<AntigravityRunException>(() => Classify(Raw(0, $$"""{"status":"{{status}}","response":"text"}""")));

        Assert.Contains(status, ex.Message);
    }

    // Denied actions

    [Fact]
    public void An_empty_answer_with_a_denied_action_raises_Run_naming_the_kinds()
    {
        var ex = Assert.Throws<AntigravityRunException>(() =>
            Classify(Fixture.Result("denied-empty.json", stderrFixture: "denied.stderr.txt")));

        Assert.Equal(["command"], ex.DeniedActions);
        Assert.Contains("command", ex.Message);
        Assert.Contains("permission", ex.StandardError);
    }

    // Hygiene

    [Fact]
    public void Standard_error_never_appears_in_exception_messages_of_the_timeout_and_sign_in_cases()
    {
        var timeout = Assert.Throws<AntigravityTimeoutException>(() => Classify(Raw(null, "", "SECRET-STDERR", timedOut: true)));
        var auth = Assert.Throws<AntigravityNotAuthenticatedException>(() => Classify(Raw(1, "", "authentication required SECRET-STDERR")));

        Assert.DoesNotContain("SECRET-STDERR", timeout.Message);
        Assert.DoesNotContain("SECRET-STDERR", auth.Message);
        Assert.Contains("SECRET-STDERR", timeout.StandardError);
    }

    [Fact]
    public void Standard_error_on_exceptions_is_cut_to_4_kib()
    {
        var ex = Assert.Throws<AntigravityTimeoutException>(() => Classify(Raw(null, "", new string('e', 10_000), timedOut: true)));

        Assert.Equal(4096, ex.StandardError!.Length);
    }

    private static class EnvelopeParserTestsHelper
    {
        public static AgyEnvelope Parse(string text)
        {
            Assert.True(EnvelopeParser.TryParse(text, out AgyEnvelope? envelope));
            return envelope!;
        }
    }
}
