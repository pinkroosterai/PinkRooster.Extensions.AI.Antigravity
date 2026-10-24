using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PinkRooster.Extensions.AI.Antigravity;

/// <summary>Registers <see cref="AntigravityChatClient"/> with dependency injection.</summary>
public static class AntigravityServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IChatClient"/> backed by <see cref="AntigravityChatClient"/>. Its options come from
    /// <see cref="IOptions{TOptions}"/> of <see cref="AntigravityChatClientOptions"/>, so they can be set with
    /// <paramref name="configure"/> or bound from configuration with
    /// <c>services.Configure&lt;AntigravityChatClientOptions&gt;(configuration.GetSection("Antigravity"))</c>.
    /// </summary>
    /// <remarks>
    /// Invalid options raise <see cref="ArgumentException"/> when the client is first resolved. To add middleware, build the
    /// pipeline around the resolved client, for example <c>provider.GetRequiredService&lt;IChatClient&gt;().AsBuilder()</c>.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional delegate that sets options.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAntigravityChatClient(
        this IServiceCollection services,
        Action<AntigravityChatClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        OptionsBuilder<AntigravityChatClientOptions> builder = services.AddOptions<AntigravityChatClientOptions>();
        if (configure is not null)
        {
            builder.Configure(configure);
        }

        services.AddSingleton<IChatClient>(provider => new AntigravityChatClient(
            provider.GetRequiredService<IOptions<AntigravityChatClientOptions>>().Value,
            provider.GetService<ILogger<AntigravityChatClient>>()));

        return services;
    }
}
