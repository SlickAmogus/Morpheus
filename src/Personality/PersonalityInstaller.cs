using System;
using System.IO;

namespace Morpheus.Personality;

// Copies an avatar's personality.md (an output-style markdown with frontmatter)
// into ~/.claude/output-styles/morpheus-{avatarName}.md.
// Activation is user-driven (/config -> Output style) since output styles take effect
// on next session start and morpheus cannot force a session restart.
public static class PersonalityInstaller
{
    public static string UserOutputStylesDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", "output-styles");
    }

    public static string TargetPathFor(string avatarName)
        => Path.Combine(UserOutputStylesDir(), $"morpheus-{avatarName}.md");

    public static void Install(string personalityFile, string avatarName)
    {
        var target = TargetPathFor(avatarName);
        Directory.CreateDirectory(UserOutputStylesDir());
        File.Copy(personalityFile, target, overwrite: true);
    }

    public static void Uninstall(string avatarName)
    {
        var target = TargetPathFor(avatarName);
        if (File.Exists(target)) File.Delete(target);
    }
}
