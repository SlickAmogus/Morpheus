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
    private CancellationTokenSource? _pollCts;
    private readonly object _pollLock = new();

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
        try { _pollCts?.Cancel(); } catch { }
        try { _http?.Stop(); } catch { }
        try { _http?.Close(); } catch { }
        _http = null;
        _cts?.Dispose();
        _cts = null;
        _pollCts?.Dispose();
        _pollCts = null;
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

        var sessionId = env.SessionId ?? "";
        var transcriptPath = env.TranscriptPath;
        var cwd = env.Cwd;

        // Claude Code sometimes fires Stop before flushing the assistant record to the JSONL.
        // Try now, then poll briefly. Cancel any previous poll (new turn supersedes the old one).
        CancellationToken ct;
        lock (_pollLock)
        {
            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            ct = _pollCts.Token;
        }

        var first = string.IsNullOrEmpty(transcriptPath) ? null
            : TranscriptReader.ReadCurrentTurn(transcriptPath);
        if (first is not null && !string.IsNullOrWhiteSpace(first.Text))
        {
            OnStop?.Invoke(new StopHookEvent
            {
                SessionId = sessionId,
                TranscriptPath = transcriptPath,
                AssistantMessage = first.Text,
                MessageUuid = first.Uuid,
                Cwd = cwd,
            });
            return;
        }

        _ = Task.Run(async () =>
        {
            const int maxAttempts = 60;    // ~15s at 250ms per attempt
            const int delayMs = 250;
            string? finalUuid = first?.Uuid;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try { await Task.Delay(delayMs, ct); }
                catch (OperationCanceledException) { return; }
                if (ct.IsCancellationRequested || string.IsNullOrEmpty(transcriptPath)) return;

                var r = TranscriptReader.ReadCurrentTurn(transcriptPath);
                if (r is null) continue;
                if (r.Uuid is { Length: > 0 }) finalUuid = r.Uuid;
                if (string.IsNullOrWhiteSpace(r.Text)) continue;

                OnStop?.Invoke(new StopHookEvent
                {
                    SessionId = sessionId,
                    TranscriptPath = transcriptPath,
                    AssistantMessage = r.Text,
                    MessageUuid = r.Uuid,
                    Cwd = cwd,
                });
                return;
            }

            // No text appeared in time — tell the game loop so status is accurate.
            OnStop?.Invoke(new StopHookEvent
            {
                SessionId = sessionId,
                TranscriptPath = transcriptPath,
                AssistantMessage = null,
                MessageUuid = finalUuid,
                Cwd = cwd,
            });
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
