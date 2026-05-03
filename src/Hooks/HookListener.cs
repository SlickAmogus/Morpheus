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
    private DateTime _lastActivityTime = DateTime.UtcNow;
    private const double SessionTimeoutSeconds = 30.0; // auto-rebind after 30s inactivity

    public int Port { get; private set; }
    public string? BoundSessionId { get; private set; }
    public string? FilterCwd { get; set; }

    public event Action<StopHookEvent>? OnStop;
    public event Action<ToolHookEvent>? OnTool;
    public event Action<string>? OnBindChanged;  // fires when a new session locks on
    public event Action<string>? OnPollTick;     // status message during polling
    public event Action<string>? OnDiagnostic;  // diagnostic / warning messages

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

            AppLogger.Log($"[hook] {path} received");

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
            AppLogger.Log($"[hook] handler exception: {ex.Message}");
            try { Respond(ctx, 500, $"{{\"error\":{JsonEncodedText.Encode(ex.Message)}}}"); } catch { }
        }
    }

    private void HandleSpeak(string body)
    {
        var env = Parse(body);
        if (env is null) { AppLogger.Log("[hook] /speak: failed to parse body"); return; }
        AppLogger.Log($"[hook] /speak sid={env.SessionId?[..Math.Min(8, env.SessionId?.Length ?? 0)]} cwd={env.Cwd}");
        if (!LockOrIgnore(env.SessionId, env.Cwd)) return;

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

        // Fire immediately if we already have text, but still poll briefly —
        // Claude Code sometimes flushes final post-tool text after Stop fires.
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
        }

        _ = Task.Run(async () =>
        {
            // If we already had text, only poll for a short late-flush window (10s).
            // If we had nothing, poll for the full 5-min wait-for-text window.
            int maxAttempts = first is not null && !string.IsNullOrWhiteSpace(first.Text)
                ? 40    // 40 × 250ms = 10 seconds
                : 1200; // 1200 × 250ms = 5 minutes
            const int delayMs = 250;
            string? lastText = first?.Text;
            string? finalUuid = first?.Uuid;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try { await Task.Delay(delayMs, ct); }
                catch (OperationCanceledException) { return; }
                if (ct.IsCancellationRequested || string.IsNullOrEmpty(transcriptPath)) return;

                if (attempt % 8 == 0 && lastText is null)
                    OnPollTick?.Invoke($"waiting for transcript… {(attempt * delayMs) / 1000}s");

                var r = TranscriptReader.ReadCurrentTurn(transcriptPath);
                if (r is null) continue;
                if (r.Uuid is { Length: > 0 }) finalUuid = r.Uuid;
                if (string.IsNullOrWhiteSpace(r.Text)) continue;
                if (r.Text == lastText) continue; // nothing new

                lastText = r.Text;
                OnStop?.Invoke(new StopHookEvent
                {
                    SessionId = sessionId,
                    TranscriptPath = transcriptPath,
                    AssistantMessage = r.Text,
                    MessageUuid = r.Uuid,
                    Cwd = cwd,
                });
            }

            // If we never got any text, report empty turn so the game loop updates status.
            if (lastText is null)
            {
                OnStop?.Invoke(new StopHookEvent
                {
                    SessionId = sessionId,
                    TranscriptPath = transcriptPath,
                    AssistantMessage = null,
                    MessageUuid = finalUuid,
                    Cwd = cwd,
                });
            }
        });
    }

    private void HandleTool(string body, string phase)
    {
        var env = Parse(body);
        if (env is null) { AppLogger.Log($"[hook] /tool-{phase}: failed to parse body"); return; }
        AppLogger.Log($"[hook] /tool-{phase} tool={env.ToolName} sid={env.SessionId?[..Math.Min(8, env.SessionId?.Length ?? 0)]} cwd={env.Cwd}");
        if (!LockOrIgnore(env.SessionId, env.Cwd)) return;

        OnTool?.Invoke(new ToolHookEvent
        {
            SessionId = env.SessionId ?? "",
            Phase = phase,
            ToolName = env.ToolName ?? "",
            Cwd = env.Cwd,
        });

        // PreToolUse is the only intra-turn flush point: any assistant text emitted
        // before this tool call has usually landed in the JSONL by now. Read once
        // and fire a Stop event so the game loop can speak the new suffix mid-turn.
        if (phase == "pre" && !string.IsNullOrEmpty(env.TranscriptPath))
        {
            var r = TranscriptReader.ReadCurrentTurn(env.TranscriptPath);
            if (r is not null && !string.IsNullOrWhiteSpace(r.Text))
            {
                OnStop?.Invoke(new StopHookEvent
                {
                    SessionId = env.SessionId ?? "",
                    TranscriptPath = env.TranscriptPath,
                    AssistantMessage = r.Text,
                    MessageUuid = r.Uuid,
                    Cwd = env.Cwd,
                });
            }
        }
    }

    private bool LockOrIgnore(string? sessionId, string? cwd)
    {
        // CWD filter: ignore events from sessions in different directories
        if (FilterCwd is not null)
        {
            if (cwd is null)
            {
                // Hook payload didn't include cwd — pass through (can't filter)
                OnDiagnostic?.Invoke($"hook: cwd missing in payload, filter bypassed (filter={FilterCwd})");
            }
            else
            {
                try
                {
                    var normFilter = Path.GetFullPath(FilterCwd).TrimEnd(Path.DirectorySeparatorChar, '/');
                    var normCwd    = Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar, '/');
                    if (!string.Equals(normFilter, normCwd, StringComparison.OrdinalIgnoreCase))
                    {
                        OnDiagnostic?.Invoke($"hook cwd mismatch — got: {normCwd} | want: {normFilter}");
                        return false;
                    }
                }
                catch
                {
                    OnDiagnostic?.Invoke($"hook: cwd path error (got={cwd}, filter={FilterCwd})");
                }
            }
        }

        if (string.IsNullOrEmpty(sessionId)) { AppLogger.Log("[hook] LockOrIgnore: empty sessionId, ignored"); return false; }

        _lastActivityTime = DateTime.UtcNow;

        if (BoundSessionId is null)
        {
            BoundSessionId = sessionId;
            AppLogger.Log($"[hook] bound to new session {sessionId[..Math.Min(8, sessionId.Length)]}");
            OnBindChanged?.Invoke(sessionId);
            return true;
        }

        if (BoundSessionId != sessionId)
        {
            var timeSinceLastActivity = (DateTime.UtcNow - _lastActivityTime).TotalSeconds;
            if (timeSinceLastActivity > SessionTimeoutSeconds)
            {
                AppLogger.Log($"[hook] session timed out, rebinding {sessionId[..Math.Min(8, sessionId.Length)]}");
                BoundSessionId = sessionId;
                OnBindChanged?.Invoke(sessionId);
                return true;
            }
            AppLogger.Log($"[hook] ignored — bound to {BoundSessionId[..Math.Min(8, BoundSessionId.Length)]}, got {sessionId[..Math.Min(8, sessionId.Length)]}");
            return false;
        }

        return true; // same session
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
