// FakeAgy imitates the agy command line for tests of the process runner. Its behavior is chosen with environment
// variables, so the arguments the library passes (which this program ignores unless asked to echo them) stay realistic.
//
//   FAKE_AGY_VERSION         text printed for "--version" (default "1.3.5")
//   FAKE_AGY_STDOUT          text written to standard output
//   FAKE_AGY_LINES           lines (separated by newlines) written to standard output one at a time, each flushed
//   FAKE_AGY_LINE_DELAY_MS   wait this long after each of those lines
//   FAKE_AGY_STDOUT_HEX      bytes (hex digits) written to standard output, for invalid UTF-8
//   FAKE_AGY_STDERR          text written to standard error
//   FAKE_AGY_STDOUT_MB       this many MiB of 'x' written to standard output
//   FAKE_AGY_STDERR_MB       this many MiB of 'e' written to standard error, before anything else
//   FAKE_AGY_SLEEP_MS        wait this long before exiting
//   FAKE_AGY_EXIT            exit code (default 0)
//   FAKE_AGY_ECHO_ARGS=1     write the received arguments as a JSON array to standard output
//   FAKE_AGY_ECHO_CWD=1      write the working directory to standard output
//   FAKE_AGY_ECHO_ENV=NAME   write the value of that environment variable (or "<unset>") to standard output
//   FAKE_AGY_READ_STDIN=1    read standard input to its end and write "stdin-length:N"
//   FAKE_AGY_ECHO_CONTEXT=1  write a success envelope whose response lists HOME, whether it is a directory, the contents of
//                            AGENTS.md in the working directory (or "<none>") and the received arguments
//   FAKE_AGY_ECHO_ATTACHMENTS=1  write a success envelope whose response lists each file under "attachments" in the working
//                            directory as "name:length:first-bytes-hex", and the received arguments
//   FAKE_AGY_MCP_CALLS       lines of "tool|{json arguments}": act as agy toward the MCP server named in $HOME/.gemini/config/mcp_config.json
//                            (agy's observed sequence), call each tool, and answer with a success envelope listing the results; when the
//                            allow rule in $HOME/.gemini/antigravity-cli/settings.json is missing, answer as agy does without permission
//   FAKE_AGY_MCP_HOLD_MS     after the MCP calls, wait this long before answering
//   FAKE_AGY_WRITE_FILE=NAME create that file in the working directory
//   FAKE_AGY_CHILD_SLEEP_MS  start a child that sleeps this long and shares the standard streams
//   FAKE_AGY_CHILD_PIDFILE   file that receives the child's process id
//   FAKE_AGY_SELF_KILL=1     end this process abruptly, as an outside signal would

using System.Diagnostics;
using System.Text;
using System.Text.Json;

if (args.Length >= 2 && args[0] == "--fake-sleep")
{
    Thread.Sleep(int.Parse(args[1]));
    return 0;
}

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Stream stdout = Console.OpenStandardOutput();
Stream stderr = Console.OpenStandardError();

static string? Env(string name) => Environment.GetEnvironmentVariable(name);

void Write(Stream stream, string text)
{
    byte[] bytes = utf8.GetBytes(text);
    stream.Write(bytes, 0, bytes.Length);
    stream.Flush();
}

void WriteMegabytes(Stream stream, int megabytes, byte fill)
{
    byte[] chunk = new byte[64 * 1024];
    Array.Fill(chunk, fill);
    for (long written = 0; written < megabytes * 1024L * 1024L; written += chunk.Length)
    {
        stream.Write(chunk, 0, chunk.Length);
    }

    stream.Flush();
}

if (args.Contains("--version"))
{
    Write(stdout, (Env("FAKE_AGY_VERSION") ?? "1.3.5") + "\n");
    return 0;
}

if (Env("FAKE_AGY_SELF_KILL") == "1")
{
    Process.GetCurrentProcess().Kill();
}

if (int.TryParse(Env("FAKE_AGY_CHILD_SLEEP_MS"), out int childSleep))
{
    string host = Environment.ProcessPath!;
    var start = new ProcessStartInfo(host) { UseShellExecute = false };
    if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
    {
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
    }

    start.ArgumentList.Add("--fake-sleep");
    start.ArgumentList.Add(childSleep.ToString());
    Process child = Process.Start(start)!;
    string? pidFile = Env("FAKE_AGY_CHILD_PIDFILE");
    if (pidFile is not null)
    {
        File.WriteAllText(pidFile, child.Id.ToString());
    }
}

if (int.TryParse(Env("FAKE_AGY_STDERR_MB"), out int stderrMb))
{
    WriteMegabytes(stderr, stderrMb, (byte)'e');
}

if (Env("FAKE_AGY_WRITE_FILE") is { } fileName)
{
    File.WriteAllText(fileName, "written by FakeAgy");
}

if (Env("FAKE_AGY_READ_STDIN") == "1")
{
    string input = Console.In.ReadToEnd();
    Write(stdout, "stdin-length:" + input.Length + "\n");
}

if (Env("FAKE_AGY_ECHO_ARGS") == "1")
{
    Write(stdout, JsonSerializer.Serialize(args));
}

if (Env("FAKE_AGY_ECHO_CWD") == "1")
{
    Write(stdout, Directory.GetCurrentDirectory());
}

if (Env("FAKE_AGY_ECHO_CONTEXT") == "1")
{
    string? homeValue = Environment.GetEnvironmentVariable("HOME");
    string agentsPath = Path.Combine(Directory.GetCurrentDirectory(), "AGENTS.md");
    string context =
        "home=" + homeValue + "\n" +
        "home_exists=" + (homeValue is not null && Directory.Exists(homeValue)) + "\n" +
        "cwd=" + Directory.GetCurrentDirectory() + "\n" +
        "agents=" + (File.Exists(agentsPath) ? File.ReadAllText(agentsPath) : "<none>") + "\n" +
        "args=" + JsonSerializer.Serialize(args);
    Write(stdout, JsonSerializer.Serialize(new { status = "SUCCESS", response = context, conversation_id = "ctx-1" }));
}

if (Env("FAKE_AGY_ECHO_ATTACHMENTS") == "1")
{
    string folder = Path.Combine(Directory.GetCurrentDirectory(), "attachments");
    IEnumerable<string> found = Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.Ordinal).Select(f =>
        {
            byte[] bytes = File.ReadAllBytes(f);
            return Path.GetFileName(f) + ":" + bytes.Length + ":" + Convert.ToHexString(bytes.AsSpan(0, Math.Min(4, bytes.Length)));
        })
        : [];
    string listing = "files=" + string.Join(",", found) + "\nargs=" + JsonSerializer.Serialize(args);
    Write(stdout, JsonSerializer.Serialize(new { status = "SUCCESS", response = listing, conversation_id = "att-1" }));
}

if (Env("FAKE_AGY_MCP_CALLS") is { } mcpCalls)
{
    string home = Environment.GetEnvironmentVariable("HOME") ?? throw new InvalidOperationException("HOME is not set.");
    using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, ".gemini", "config", "mcp_config.json")));
    JsonElement server = config.RootElement.GetProperty("mcpServers").GetProperty("agy-tools");
    string url = server.GetProperty("serverUrl").GetString()!;
    string authorization = server.GetProperty("headers").GetProperty("Authorization").GetString()!;

    bool allowed = false;
    string settingsPath = Path.Combine(home, ".gemini", "antigravity-cli", "settings.json");
    if (File.Exists(settingsPath))
    {
        using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        allowed = settings.RootElement.TryGetProperty("permissions", out JsonElement permissions)
            && permissions.TryGetProperty("allow", out JsonElement allow)
            && allow.EnumerateArray().Any(e => e.GetString() == "mcp(agy-tools/*)");
    }

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    int rpcId = 0;
    async Task<string> Post(string method, string paramsJson, bool auth = true)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"id\":" + (++rpcId) + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, utf8, "application/json") };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        if (auth)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        using HttpResponseMessage response = await http.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    await Post("server/discover", "{}"); // answered 404; agy then falls back
    await Post("initialize", "{\"protocolVersion\":\"2025-11-25\"}");
    using (var get = new HttpRequestMessage(HttpMethod.Get, url))
    {
        get.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        get.Headers.TryAddWithoutValidation("Authorization", authorization);
        using HttpResponseMessage ignored = await http.SendAsync(get);
    }

    string origin = new Uri(url).GetLeftPart(UriPartial.Authority);
    using (HttpResponseMessage wellKnown = await http.GetAsync(origin + "/.well-known/oauth-protected-resource/mcp"))
    {
    }

    await Post("tools/list", "{}");

    var results = new List<string>();
    if (allowed)
    {
        foreach (string line in mcpCalls.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int bar = line.IndexOf('|');
            string reply = await Post("tools/call", "{\"name\":\"" + line[..bar] + "\",\"arguments\":" + line[(bar + 1)..] + "}");
            using JsonDocument parsed = JsonDocument.Parse(reply);
            results.Add(parsed.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty);
        }
    }

    if (int.TryParse(Env("FAKE_AGY_MCP_HOLD_MS"), out int hold))
    {
        await Task.Delay(hold);
    }

    Write(stdout, allowed
        ? JsonSerializer.Serialize(new { status = "SUCCESS", response = "tools: " + string.Join(" | ", results), conversation_id = "mcp-1" })
        : "{\"status\":\"SUCCESS\",\"response\":\"\",\"conversation_id\":\"mcp-1\",\"denied_actions\":[{\"action\":\"mcp\",\"display_name\":\"CallMcpTool\"}]}");
}

if (Env("FAKE_AGY_ECHO_ENV") is { } variable)
{
    Write(stdout, Environment.GetEnvironmentVariable(variable) ?? "<unset>");
}

if (Env("FAKE_AGY_STDOUT") is { } text)
{
    Write(stdout, text);
}

if (Env("FAKE_AGY_LINES") is { } lines)
{
    _ = int.TryParse(Env("FAKE_AGY_LINE_DELAY_MS"), out int lineDelay);
    foreach (string line in lines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        Write(stdout, line + "\n");
        if (lineDelay > 0)
        {
            Thread.Sleep(lineDelay);
        }
    }
}

if (Env("FAKE_AGY_STDOUT_HEX") is { } hex)
{
    byte[] bytes = Convert.FromHexString(hex);
    stdout.Write(bytes, 0, bytes.Length);
    stdout.Flush();
}

if (int.TryParse(Env("FAKE_AGY_STDOUT_MB"), out int stdoutMb))
{
    WriteMegabytes(stdout, stdoutMb, (byte)'x');
}

if (Env("FAKE_AGY_STDERR") is { } errorText)
{
    Write(stderr, errorText);
}

if (int.TryParse(Env("FAKE_AGY_SLEEP_MS"), out int sleep))
{
    Thread.Sleep(sleep);
}

return int.TryParse(Env("FAKE_AGY_EXIT"), out int exitCode) ? exitCode : 0;
