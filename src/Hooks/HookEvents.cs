namespace Morpheus.Hooks;

// DTOs mirror the JSON payload Claude Code sends to a hook command on stdin.
// We'll receive these via a bridge command we install into the user's settings.json.

public sealed class StopHookEvent
{
    public string SessionId { get; set; } = "";
    public string? AssistantMessage { get; set; }
    public string? TranscriptPath { get; set; }
}

public sealed class ToolHookEvent
{
    public string SessionId { get; set; } = "";
    public string Phase { get; set; } = ""; // "pre" | "post"
    public string ToolName { get; set; } = "";
    public string? ToolInputSummary { get; set; }
}
