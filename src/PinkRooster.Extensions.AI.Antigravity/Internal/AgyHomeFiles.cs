using System.Text;
using System.Text.Json.Nodes;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// Writes the two agy configuration files a tool request needs into the request's own agy home (never the user's).
/// The paths and shapes are the ones agy was verified with (research.md, "HTTP bridge run").
/// </summary>
internal static class AgyHomeFiles
{
    public const string McpConfigRelativePath = ".gemini/config/mcp_config.json";
    public const string SettingsRelativePath = ".gemini/antigravity-cli/settings.json";

    /// <summary>The permission rule that lets agy call the bridge's tools in print mode.</summary>
    public static string PermissionRule => $"mcp({McpToolServer.ServerName}/*)";

    public static void WriteBridge(string homePath, string url, string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(homePath);
        ArgumentException.ThrowIfNullOrEmpty(url);
        ArgumentException.ThrowIfNullOrEmpty(token);

        JsonObject config = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [McpToolServer.ServerName] = new JsonObject
                {
                    ["serverUrl"] = url,
                    ["headers"] = new JsonObject { ["Authorization"] = "Bearer " + token },
                },
            },
        };
        JsonObject settings = new JsonObject
        {
            ["permissions"] = new JsonObject { ["allow"] = new JsonArray(PermissionRule) },
        };

        Write(homePath, McpConfigRelativePath, config);
        Write(homePath, SettingsRelativePath, settings);
    }

    private static void Write(string homePath, string relativePath, JsonObject content)
    {
        string file = Path.Combine(homePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
