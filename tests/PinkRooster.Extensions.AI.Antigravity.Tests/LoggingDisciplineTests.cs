using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

/// <summary>The library writes to a log only through <c>LogMessages</c>, whose parameters cannot carry content.</summary>
public class LoggingDisciplineTests
{
    private static readonly HashSet<string> ForbiddenParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "prompt", "text", "content", "message", "answer", "response", "schema", "stderr", "standardError", "standardOutput",
        "environment", "env", "variables", "credentials", "token", "secret", "path",
    };

    private static readonly HashSet<Type> AllowedParameterTypes =
    [
        typeof(ILogger), typeof(string), typeof(int), typeof(int?), typeof(long), typeof(long?), typeof(bool), typeof(double),
    ];

    [Fact]
    public void Log_message_parameters_are_numbers_names_and_versions_only()
    {
        MethodInfo[] methods = typeof(LogMessages).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.NotEmpty(methods);

        foreach (MethodInfo method in methods)
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.Contains(parameter.ParameterType, AllowedParameterTypes);
                if (parameter.ParameterType != typeof(ILogger))
                {
                    // 'path' is the one deliberate exception: the warning for a directory that could not be deleted names it.
                    bool deliberate = method.Name == nameof(LogMessages.RequestDirectoryNotDeleted) && parameter.Name == "path";
                    Assert.True(deliberate || !ForbiddenParameterNames.Contains(parameter.Name!), $"{method.Name}({parameter.Name})");
                }
            }
        }
    }

    [Fact]
    public void No_source_file_other_than_the_log_messages_writes_to_a_logger_directly()
    {
        string source = FindSourceDirectory();
        var offenders = new List<string>();
        var direct = new Regex(@"\b(?:_logger|logger|Logger)\s*\.\s*(?:Log|LogTrace|LogDebug|LogInformation|LogWarning|LogError|LogCritical|BeginScope)\s*\(");

        foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            if (direct.IsMatch(File.ReadAllText(file)))
            {
                offenders.Add(Path.GetRelativePath(source, file));
            }
        }

        Assert.Empty(offenders);
    }

    private static string FindSourceDirectory()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "src", "PinkRooster.Extensions.AI.Antigravity")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        Assert.NotNull(directory);
        return Path.Combine(directory, "src", "PinkRooster.Extensions.AI.Antigravity");
    }
}
