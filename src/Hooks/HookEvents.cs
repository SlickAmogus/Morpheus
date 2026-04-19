using System.Text.Json.Serialization;

namespace Morpheus.Hooks;

// Mirrors Claude Code hook JSON POSTed to morpheus's HttpListener.
// type="http" hooks send the raw hook payload as the request body.

public sealed class HookEnvelope
{
    [JsonPropertyName("session_id")]   public string? SessionId { get; set; }
    [JsonPropertyName("transcript_path")] public string? TranscriptPath { get; set; }
    [JsonPropertyName("cwd")]          public string? Cwd { get; set; }
    [JsonPropertyName("hook_event_name")] public string? HookEventName { get; set; }
    [JsonPropertyName("tool_name")]    public string? ToolName { get; set; }
}

public sealed class StopHookEvent
{
    public string SessionId { get; set; } = "";
    public string? TranscriptPath { get; set; }
    public string? AssistantMessage { get; set; }
    public string? Cwd { get; set; }
}

public sealed class ToolHookEvent
{
    public string SessionId { get; set; } = "";
    public string Phase { get; set; } = ""; // "pre" | "post"
    public string ToolName { get; set; } = "";
    public string? Cwd { get; set; }
}
