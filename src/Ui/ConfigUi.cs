using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Morpheus.Avatar;

namespace Morpheus.Ui;

// Keyboard-driven v1:
//   F1 — install hooks into current working directory (binds that session)
//   F2 — cycle avatar
//   F3 — cycle template
//   F5 — test-speak (bundled clip)
//   F6 — install active avatar's personality file
//   Esc — quit
public sealed class ConfigUi
{
    public bool Visible { get; set; } = true;

    public IReadOnlyList<AvatarEntry> Avatars { get; set; } = [];
    public int AvatarIndex { get; set; }

    public IReadOnlyList<string> Templates { get; set; } = [];
    public int TemplateIndex { get; set; }

    public string? BoundSessionId { get; set; }
    public string? StatusLine { get; set; }

    public AvatarEntry? SelectedAvatar =>
        Avatars.Count == 0 ? null : Avatars[AvatarIndex % Avatars.Count];

    public string? SelectedTemplate =>
        Templates.Count == 0 ? null : Templates[TemplateIndex % Templates.Count];

    public void CycleAvatar(int delta)
    {
        if (Avatars.Count == 0) return;
        AvatarIndex = ((AvatarIndex + delta) % Avatars.Count + Avatars.Count) % Avatars.Count;
    }

    public void CycleTemplate(int delta)
    {
        if (Templates.Count == 0) return;
        TemplateIndex = ((TemplateIndex + delta) % Templates.Count + Templates.Count) % Templates.Count;
    }

    public void Draw(SpriteBatch batch, TextRenderer text, Rectangle viewport, Color tint)
    {
        if (!Visible) return;
        var y = 12f;
        const float lh = 22f;
        var pad = new Vector2(14f, y);

        text.DrawString(batch, "morpheus", pad, tint, 24);
        pad.Y += 32;

        var av = SelectedAvatar;
        text.DrawString(batch, $"avatar:   {(av?.Manifest.Name ?? "<none>")}  (F2)", pad, Color.White);
        pad.Y += lh;
        text.DrawString(batch, $"template: {SelectedTemplate ?? "<none>"}  (F3)", pad, Color.White);
        pad.Y += lh;
        text.DrawString(batch, $"session:  {BoundSessionId ?? "(unbound — F1 to install hooks)"}", pad, Color.LightGray);
        pad.Y += lh;

        if (!string.IsNullOrEmpty(StatusLine))
        {
            text.DrawString(batch, StatusLine, pad, Color.Yellow);
            pad.Y += lh;
        }

        pad.X = 14f;
        pad.Y = viewport.Height - 18f;
        text.DrawString(batch,
            "F1 bind   F2 avatar   F3 template   F5 test   F6 personality   F7 color   F8 compact   F9 reset layout   Esc quit",
            pad, new Color(150, 150, 150), 12);
    }
}
