using System.Runtime.CompilerServices;

namespace PinkRooster.Extensions.AI.Antigravity.IntegrationTests;

/// <summary>
/// A test that runs the real agy. It is skipped unless <c>AGY_INTEGRATION_TESTS=1</c> is set, because every run spends model
/// tokens (about 17,000 input tokens per call even for one word). Run these yourself; they are never part of CI.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public const string EnableVariable = "AGY_INTEGRATION_TESTS";

    public IntegrationFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Skip = $"Runs the real agy and spends model tokens. Set {EnableVariable}=1 to run it.";
        }
    }
}

/// <summary>A test that needs one more switch on top of <see cref="IntegrationFactAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IntegrationFactWhenAttribute : FactAttribute
{
    public IntegrationFactWhenAttribute(string variable, string value = "1", [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (Environment.GetEnvironmentVariable(IntegrationFactAttribute.EnableVariable) != "1")
        {
            Skip = $"Runs the real agy and spends model tokens. Set {IntegrationFactAttribute.EnableVariable}=1 to run it.";
        }
        else if (Environment.GetEnvironmentVariable(variable) != value)
        {
            Skip = $"Also needs {variable}={value}.";
        }
    }
}
