using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PinkRooster.Extensions.AI.Antigravity.Internal;

/// <summary>
/// The loopback HTTP endpoint for one tool request. Listens on 127.0.0.1 on a port the system picks, answers only requests that
/// carry this request's bearer token and name the loopback address as host, and serves the MCP surface of
/// <see cref="McpToolServer"/>. A deliberately small HTTP/1.1 subset (research.md, "Bridge listener").
/// </summary>
internal sealed class ToolBridge : IAsyncDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxHeaderCount = 100;
    private const int MaxBodyBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);

    private readonly TcpListener _listener;
    private readonly McpToolServer _server;
    private readonly byte[] _token;
    private readonly CancellationTokenSource _stop;
    private readonly ConcurrentDictionary<TcpClient, Task> _connections = new();
    private readonly Task _acceptLoop;
    private int _disposed;

    private ToolBridge(TcpListener listener, McpToolServer server, string token, CancellationTokenSource stop)
    {
        _listener = listener;
        _server = server;
        Token = token;
        _token = Encoding.ASCII.GetBytes(token);
        _stop = stop;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The port the bridge listens on.</summary>
    public int Port { get; }

    /// <summary>The bearer token every MCP request must carry.</summary>
    public string Token { get; }

    /// <summary>The address of the MCP endpoint.</summary>
    public string Url => $"http://127.0.0.1:{Port}/mcp";

    /// <summary>Starts listening. Throws <see cref="AntigravityProcessException"/> when no loopback port can be opened.</summary>
    public static ToolBridge Start(McpToolServer server, CancellationTokenSource stop)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(stop);

        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new AntigravityProcessException($"The tool bridge could not open a loopback port: {ex.SocketErrorCode}.", ex);
        }

        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return new ToolBridge(listener, server, token, stop);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                TaskCompletionSource handling = new TaskCompletionSource();
                _connections[client] = handling.Task;
                _ = HandleAsync(client, handling);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The bridge is shutting down.
        }
    }

    private async Task HandleAsync(TcpClient client, TaskCompletionSource handling)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                await ServeAsync(client.GetStream(), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException or InvalidDataException)
        {
            // A dropped or malformed connection ends only that connection.
        }
        finally
        {
            handling.TrySetResult();
            _connections.TryRemove(client, out _);
        }
    }

    private async Task ServeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        HttpReader reader = new HttpReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpRequest? request = await reader.ReadRequestAsync(cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                return; // closed by the peer
            }

            if (request.ExpectsContinue)
            {
                await WriteAsync(stream, 100, null, null, keepAlive: true, cancellationToken).ConfigureAwait(false);
            }

            await request.ReadBodyAsync(cancellationToken).ConfigureAwait(false);
            (int status, string? body, string? allow) = await RouteAsync(request, cancellationToken).ConfigureAwait(false);
            await WriteAsync(stream, status, body, allow, keepAlive: request.KeepAlive && status < 500, cancellationToken).ConfigureAwait(false);
            if (!request.KeepAlive)
            {
                return;
            }
        }
    }

    private async Task<(int Status, string? Body, string? Allow)> RouteAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        // DNS-rebinding defence: only the loopback address and this port are acceptable names, and a browser origin is foreign.
        if (!IsLoopbackHost(request.Header("host")) || request.Header("origin") is { } origin && !IsLoopbackOrigin(origin))
        {
            return (403, McpToolServer.Error(null, 403, -32600, "Forbidden.").Body, null);
        }

        if (request.Path != "/mcp")
        {
            return (404, McpToolServer.Error(null, 404, -32601, "Not found.").Body, null);
        }

        if (request.Method != "POST")
        {
            return (405, null, "POST");
        }

        if (!TokenMatches(request.Header("authorization")))
        {
            return (401, McpToolServer.Error(null, 401, -32001, "Unauthorized.").Body, null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(request.Body);
        }
        catch (JsonException)
        {
            return (400, McpToolServer.Error(null, 400, -32700, "Parse error.").Body, null);
        }

        using (document)
        {
            McpReply reply = await _server.HandleAsync(document.RootElement, cancellationToken).ConfigureAwait(false);
            return (reply.HttpStatus, reply.Body, null);
        }
    }

    private bool IsLoopbackHost(string? host) =>
        host is not null && (host == $"127.0.0.1:{Port}" || host == $"localhost:{Port}");

    private bool IsLoopbackOrigin(string origin) => origin == $"http://127.0.0.1:{Port}" || origin == $"http://localhost:{Port}";

    private bool TokenMatches(string? authorization)
    {
        const string Prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] offered = Encoding.ASCII.GetBytes(authorization[Prefix.Length..]);
        return CryptographicOperations.FixedTimeEquals(offered, _token);
    }

    private static async Task WriteAsync(
        NetworkStream stream, int status, string? body, string? allow, bool keepAlive, CancellationToken cancellationToken)
    {
        byte[] payload = body is null ? [] : Encoding.UTF8.GetBytes(body);
        StringBuilder head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
        if (status == 100)
        {
            head.Append("\r\n");
        }
        else
        {
            if (body is not null)
            {
                head.Append("Content-Type: application/json\r\n");
            }

            if (allow is not null)
            {
                head.Append("Allow: ").Append(allow).Append("\r\n");
            }

            head.Append("Content-Length: ").Append(payload.Length).Append("\r\n");
            head.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n\r\n");
        }

        byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
        await stream.WriteAsync(headBytes, cancellationToken).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        100 => "Continue",
        200 => "OK",
        202 => "Accepted",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        _ => "Error",
    };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Cancel first so running tool bodies see their token fire, then close the socket side.
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        foreach (TcpClient client in _connections.Keys)
        {
            client.Dispose();
        }

        Task all = Task.WhenAll(_connections.Values.Append(_acceptLoop));
        await Task.WhenAny(all, Task.Delay(ShutdownWait)).ConfigureAwait(false);
    }

    private sealed class HttpRequest(HttpReader reader, string method, string path, Dictionary<string, string> headers, bool keepAlive)
    {
        public string Method { get; } = method;

        public string Path { get; } = path;

        public bool KeepAlive { get; } = keepAlive;

        public string Body { get; private set; } = string.Empty;

        public bool ExpectsContinue => string.Equals(Header("expect"), "100-continue", StringComparison.OrdinalIgnoreCase);

        public string? Header(string name) => headers.GetValueOrDefault(name);

        public async Task ReadBodyAsync(CancellationToken cancellationToken)
        {
            if (Header("transfer-encoding") is { } encoding && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                Body = await reader.ReadChunkedAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (Header("content-length") is { } length)
            {
                if (!int.TryParse(length, out int count) || count < 0 || count > MaxBodyBytes)
                {
                    throw new InvalidDataException("Bad content length.");
                }

                Body = await reader.ReadBodyAsync(count, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class HttpReader(NetworkStream stream)
    {
        private readonly byte[] _buffer = new byte[8192];
        private int _start;
        private int _end;

        public async Task<HttpRequest?> ReadRequestAsync(CancellationToken cancellationToken)
        {
            string? requestLine = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (requestLine is null)
            {
                return null;
            }

            string[] parts = requestLine.Split(' ');
            if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Bad request line.");
            }

            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int total = requestLine.Length;
            while (true)
            {
                string line = await ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Truncated headers.");
                if (line.Length == 0)
                {
                    break;
                }

                total += line.Length;
                int colon = line.IndexOf(':');
                if (colon <= 0 || headers.Count >= MaxHeaderCount || total > MaxHeaderBytes)
                {
                    throw new InvalidDataException("Bad header.");
                }

                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            string target = parts[1];
            int query = target.IndexOf('?');
            string path = query < 0 ? target : target[..query];
            bool keepAlive = parts[2] == "HTTP/1.1"
                ? !string.Equals(headers.GetValueOrDefault("connection"), "close", StringComparison.OrdinalIgnoreCase)
                : string.Equals(headers.GetValueOrDefault("connection"), "keep-alive", StringComparison.OrdinalIgnoreCase);
            return new HttpRequest(this, parts[0], path, headers, keepAlive);
        }

        public async Task<string> ReadBodyAsync(int count, CancellationToken cancellationToken)
        {
            byte[] body = new byte[count];
            await ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(body);
        }

        public async Task<string> ReadChunkedAsync(CancellationToken cancellationToken)
        {
            using MemoryStream body = new MemoryStream();
            while (true)
            {
                string sizeLine = await ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Truncated chunk.");
                int semicolon = sizeLine.IndexOf(';');
                if (!int.TryParse(semicolon < 0 ? sizeLine : sizeLine[..semicolon], System.Globalization.NumberStyles.HexNumber, null, out int size)
                    || size < 0 || body.Length + size > MaxBodyBytes)
                {
                    throw new InvalidDataException("Bad chunk size.");
                }

                if (size == 0)
                {
                    while (!string.IsNullOrEmpty(await ReadLineAsync(cancellationToken).ConfigureAwait(false)))
                    {
                        // trailers are ignored
                    }

                    return Encoding.UTF8.GetString(body.ToArray());
                }

                byte[] chunk = new byte[size];
                await ReadExactlyAsync(chunk, cancellationToken).ConfigureAwait(false);
                body.Write(chunk);
                await ReadLineAsync(cancellationToken).ConfigureAwait(false); // the CRLF after the chunk
            }
        }

        private async Task ReadExactlyAsync(byte[] target, CancellationToken cancellationToken)
        {
            int filled = 0;
            while (filled < target.Length)
            {
                if (_start == _end && !await FillAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException("Truncated body.");
                }

                int take = Math.Min(target.Length - filled, _end - _start);
                Buffer.BlockCopy(_buffer, _start, target, filled, take);
                _start += take;
                filled += take;
            }
        }

        // Returns null at a clean end of stream before any byte of the line.
        private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            List<byte> line = new List<byte>(128);
            while (true)
            {
                if (_start == _end && !await FillAsync(cancellationToken).ConfigureAwait(false))
                {
                    return line.Count == 0 ? null : throw new InvalidDataException("Truncated line.");
                }

                byte b = _buffer[_start++];
                if (b == (byte)'\n')
                {
                    if (line.Count > 0 && line[^1] == (byte)'\r')
                    {
                        line.RemoveAt(line.Count - 1);
                    }

                    return Encoding.ASCII.GetString(line.ToArray());
                }

                line.Add(b);
                if (line.Count > MaxHeaderBytes)
                {
                    throw new InvalidDataException("Line too long.");
                }
            }
        }

        private async Task<bool> FillAsync(CancellationToken cancellationToken)
        {
            int read = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            _start = 0;
            _end = read;
            return read > 0;
        }
    }
}
