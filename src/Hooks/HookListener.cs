using System;
using System.Threading;
using System.Threading.Tasks;

namespace Morpheus.Hooks;

// Stub — next pass: HttpListener on 127.0.0.1:{port}, POST /speak + /tool + /bind endpoints,
// session-ID lock-on-first-message, event bus to game loop.
public sealed class HookListener : IDisposable
{
    public int Port { get; private set; }
    public string? BoundSessionId { get; private set; }

    public event Action<StopHookEvent>? OnStop;
    public event Action<ToolHookEvent>? OnTool;

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Unbind() => BoundSessionId = null;
    public void Dispose() { }
}
