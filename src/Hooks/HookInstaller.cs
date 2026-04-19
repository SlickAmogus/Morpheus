using System.IO;

namespace Morpheus.Hooks;

// Stub — next pass: merge morpheus hooks into a target project's .claude/settings.json
// (Stop, PreToolUse, PostToolUse -> morpheus-bridge.exe --port {port} --kind stop|tool).
// Ship a bridge exe so user's hook commands don't depend on morpheus being alive
// at hook-fire time (bridge POSTs, no-ops on connection refused).
public static class HookInstaller
{
    public static void InstallToProject(string projectDir, int morpheusPort) { }
    public static void UninstallFromProject(string projectDir) { }
    public static string SuggestSettingsPath(string projectDir)
        => Path.Combine(projectDir, ".claude", "settings.json");
}
