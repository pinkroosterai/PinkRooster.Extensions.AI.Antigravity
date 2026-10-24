using System.Reflection;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class ExceptionHierarchyTests
{
    private static readonly Type[] ExpectedSubtypes =
    [
        typeof(AntigravityNotFoundException),
        typeof(AntigravityVersionException),
        typeof(AntigravityNotAuthenticatedException),
        typeof(AntigravityTimeoutException),
        typeof(AntigravityProcessException),
        typeof(AntigravityRunException),
        typeof(AntigravityOutputException),
        typeof(AntigravityInvalidOutputException),
        typeof(AntigravityPromptTooLargeException),
    ];

    [Fact]
    public void Every_public_exception_type_derives_from_the_base_type()
    {
        Type[] exceptionTypes = typeof(AntigravityException).Assembly.GetExportedTypes()
            .Where(t => typeof(Exception).IsAssignableFrom(t))
            .ToArray();

        Assert.NotEmpty(exceptionTypes);
        foreach (Type type in exceptionTypes)
        {
            Assert.True(typeof(AntigravityException).IsAssignableFrom(type), $"{type.Name} must derive from AntigravityException.");
        }
    }

    [Fact]
    public void The_nine_specified_subtypes_exist()
    {
        Type[] exported = typeof(AntigravityException).Assembly.GetExportedTypes();

        foreach (Type expected in ExpectedSubtypes)
        {
            Assert.Contains(expected, exported);
            Assert.Equal(typeof(AntigravityException), expected.BaseType);
        }
    }

    [Fact]
    public void Base_type_carries_standard_error_and_exit_code()
    {
        var exception = new AntigravityProcessException("failed") { StandardError = "boom", ExitCode = 1 };

        Assert.Equal("boom", exception.StandardError);
        Assert.Equal(1, exception.ExitCode);
        Assert.DoesNotContain("boom", exception.Message);
    }

    [Fact]
    public void Properties_carry_the_data_the_specification_names()
    {
        var notFound = new AntigravityNotFoundException("missing", ["/a", "/b"]);
        var version = new AntigravityVersionException("old", "1.0.0", "1.3.5");
        var timeout = new AntigravityTimeoutException("slow", TimeSpan.FromSeconds(7));
        var run = new AntigravityRunException("denied", ["command"]);
        var invalid = new AntigravityInvalidOutputException("bad", "{}");
        var tooLarge = new AntigravityPromptTooLargeException("long", 200, 100);

        Assert.Equal(["/a", "/b"], notFound.SearchedLocations);
        Assert.Equal("1.0.0", version.FoundVersion);
        Assert.Equal("1.3.5", version.RequiredVersion);
        Assert.Equal(TimeSpan.FromSeconds(7), timeout.Timeout);
        Assert.Equal(["command"], run.DeniedActions);
        Assert.Equal("{}", invalid.RawOutput);
        Assert.Equal(200, tooLarge.Length);
        Assert.Equal(100, tooLarge.Limit);
    }

    [Fact]
    public void Raw_output_is_not_part_of_the_message()
    {
        var invalid = new AntigravityInvalidOutputException("The structured result did not match the schema.", "SECRET-ANSWER");

        Assert.DoesNotContain("SECRET-ANSWER", invalid.Message);
        Assert.DoesNotContain("SECRET-ANSWER", invalid.ToString());
    }

    [Fact]
    public void Public_exceptions_live_in_the_library_namespace_and_have_a_public_constructor()
    {
        foreach (Type type in typeof(AntigravityException).Assembly.GetExportedTypes().Where(t => typeof(Exception).IsAssignableFrom(t)))
        {
            Assert.Equal("PinkRooster.Extensions.AI.Antigravity", type.Namespace);
            Assert.NotNull(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault());
        }
    }
}
