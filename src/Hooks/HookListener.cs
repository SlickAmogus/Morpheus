using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Morpheus.Hooks;

public sealed class HookListener : IDisposable
{
    private HttpListener? _http;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int Port { get; private set; }
    public string? BoundSessionId { get; private set; }

    public event Action<StopHookEvent>? OnStop;
    public event Action<ToolHookEvent>? OnTool;
    public event Action<string>? OnBindChanged; // fires when a new session locks on

    public Task StartAsync(int port, CancellationToken ct = default)
    {
        Stop();
        Port = port;
        _http = new HttpListener();
        _http.Prefixes.Add($"http://127.0.0.1:{port}/");
        _http.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => Loop(_cts.Token));
        return Task.CompletedTask;
    }

    public void Unbind()
    {
        BoundSessionId = null;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _http?.Stop(); } catch { }
        try { _http?.Close(); } catch { }
        _http = null;
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task Loop(CancellationToken ct)
    {
        var listener = _http!;
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }

            _ = Task.Run(() => Handle(ctx));
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (ctx.Request.HttpMethod != "POST")
            {
                Respond(ctx, 405, "{\"error\":\"POST only\"}");
                return;
            }

            string body;
            using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = r.ReadToEnd();

            switch (path)
            {
                case "/speak":      HandleSpeak(body); break;
                case "/tool-pre":   HandleTool(body, "pre"); break;
                case "/tool-post":  HandleTool(body, "post"); break;
                case "/unbind":     Unbind(); break;
                case "/ping":       break;
                default:
                    Respond(ctx, 404, "{\"error\":\"unknown route\"}");
                    return;
            }
            Respond(ctx, 200, "{\"ok\":true}");
        }
        catch (Exception ex)
        {
            try { Respond(ctx, 500, $"{{\"error\":{JsonEncodedText.Encode(ex.Message)}}}"); } catch { }
        }
    }

    private void HandleSpeak(string body)
    {
        var env = Parse(body);
        if (env is null) return;
        if (!LockOrIgnore(env.SessionId)) return;

        string? text = null;
        if (env.TranscriptPath is { Length: > 0 })
            text = TranscriptReader.ReadLastAssistantText(env.TranscriptPath);

        OnStop?.Invoke(new StopHookEvent
        {
            SessionId = env.SessionId ?? "",
            TranscriptPath = env.TranscriptPath,
            AssistantMessage = text,
            Cwd = env.Cwd,
        });
    }

    private void HandleTool(string body, string phase)
    {
        var env = Parse(body);
        if (env is null) return;
        if (!LockOrIgnore(env.SessionId)) return;

        OnTool?.Invoke(new ToolHookEvent
        {
            SessionId = env.SessionId ?? "",
            Phase = phase,
            ToolName = env.ToolName ?? "",
            Cwd = env.Cwd,
        });
    }

    private bool LockOrIgnore(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        if (BoundSessionId is null)
        {
            BoundSessionId = sessionId;
            OnBindChanged?.Invoke(sessionId);
            return true;
        }
        return BoundSessionId == sessionId;
    }

    private static HookEnvelope? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return JsonSerializer.Deserialize<HookEnvelope>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch { return null; }
    }

    private static void Respond(HttpListenerContext ctx, int status, string json)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    public void Dispose() => Stop();
}
