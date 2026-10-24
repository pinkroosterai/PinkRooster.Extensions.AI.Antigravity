using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PinkRooster.Extensions.AI.Antigravity.Tests.Support;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class DiExtensionTests
{
    private static ChatClientMetadata MetadataOf(IChatClient client) =>
        Assert.IsType<ChatClientMetadata>(client.GetService(typeof(ChatClientMetadata)));

    [Fact]
    public void The_client_is_registered_as_a_singleton_IChatClient()
    {
        using ServiceProvider provider = new ServiceCollection().AddAntigravityChatClient().BuildServiceProvider();

        IChatClient first = provider.GetRequiredService<IChatClient>();
        IChatClient second = provider.GetRequiredService<IChatClient>();

        Assert.IsType<AntigravityChatClient>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void The_delegate_sets_options()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddAntigravityChatClient(o => o.DefaultModelId = "gemini-3.8-flash-low")
            .BuildServiceProvider();

        Assert.Equal("gemini-3.8-flash-low", MetadataOf(provider.GetRequiredService<IChatClient>()).DefaultModelId);
    }

    [Fact]
    public void Options_can_be_bound_from_configuration()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Antigravity:DefaultModelId"] = "from-config",
            ["Antigravity:Timeout"] = "00:02:00",
            ["Antigravity:MaxConcurrency"] = "2",
            ["Antigravity:DefaultReasoningEffort"] = "High",
            ["Antigravity:AdditionalArguments:0"] = "--log-file",
            ["Antigravity:AdditionalArguments:1"] = "agy.log",
            ["Antigravity:EnvironmentVariables:AGY_ADC_AUTH"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.Configure<AntigravityChatClientOptions>(configuration.GetSection("Antigravity"));
        services.AddAntigravityChatClient();
        using ServiceProvider provider = services.BuildServiceProvider();

        AntigravityChatClientOptions bound = provider.GetRequiredService<IOptions<AntigravityChatClientOptions>>().Value;

        Assert.Equal("from-config", MetadataOf(provider.GetRequiredService<IChatClient>()).DefaultModelId);
        Assert.Equal(TimeSpan.FromMinutes(2), bound.Timeout);
        Assert.Equal(2, bound.MaxConcurrency);
        Assert.Equal(ReasoningEffort.High, bound.DefaultReasoningEffort);
        Assert.Equal(["--log-file", "agy.log"], bound.AdditionalArguments);
        Assert.Equal("true", bound.EnvironmentVariables["AGY_ADC_AUTH"]);
    }

    [Fact]
    public void Configuration_and_delegate_can_be_combined_with_the_delegate_last()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Antigravity:DefaultModelId"] = "from-config",
        }).Build();
        var services = new ServiceCollection();
        services.Configure<AntigravityChatClientOptions>(configuration.GetSection("Antigravity"));
        services.AddAntigravityChatClient(o => o.DefaultModelId = "from-delegate");
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal("from-delegate", MetadataOf(provider.GetRequiredService<IChatClient>()).DefaultModelId);
    }

    [Fact]
    public void Invalid_options_raise_ArgumentException_when_the_client_is_resolved()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddAntigravityChatClient(o => o.MaxConcurrency = 0)
            .BuildServiceProvider();

        Assert.Throws<ArgumentException>(() => provider.GetRequiredService<IChatClient>());
    }

    [Fact]
    public async Task A_logger_from_the_container_is_used()
    {
        var logger = new CapturingLogger();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<AntigravityChatClient>>(new TypedLogger(logger));
        // An existing file that is not an executable: the run fails at start, after the client has logged the ignored option.
        string notExecutable = typeof(DiExtensionTests).Assembly.Location;
        services.AddAntigravityChatClient(o => { o.SkipVersionCheck = true; o.ExecutablePath = notExecutable; });
        using ServiceProvider provider = services.BuildServiceProvider();
        IChatClient client = provider.GetRequiredService<IChatClient>();

        await Assert.ThrowsAsync<AntigravityProcessException>(() => client.GetResponseAsync("x", new ChatOptions { Temperature = 0.5f }));

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("Temperature"));
    }

    [Fact]
    public void The_registration_is_chainable_and_checks_its_argument()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddAntigravityChatClient());
        Assert.Throws<ArgumentNullException>(() => AntigravityServiceCollectionExtensions.AddAntigravityChatClient(null!));
    }

    [Fact]
    public void The_resolved_client_can_be_wrapped_in_a_pipeline()
    {
        using ServiceProvider provider = new ServiceCollection().AddAntigravityChatClient().BuildServiceProvider();

        using IChatClient wrapped = provider.GetRequiredService<IChatClient>().AsBuilder().Build();

        Assert.NotNull(wrapped.GetService<AntigravityChatClient>());
    }

    private sealed class TypedLogger(ILogger inner) : ILogger<AntigravityChatClient>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
