using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Morpheus.Audio;
using Morpheus.Ui.Widgets;

namespace Morpheus.Ui;

// Right-side panel for two extra voice flows:
//   1) Custom voice — type a display name + a voice_id (e.g. one you cloned or
//      designed on elevenlabs.io) to add it to the main voice dropdown.
//   2) Shared library — search the public ElevenLabs voice library and pick a
//      result to use directly (Creator tier and up).
public sealed class VoicesExtraPanel
{
    public TextInput CustomName { get; } = new() { Placeholder = "display name" };
    public TextInput CustomId   { get; } = new() { Placeholder = "voice id" };
    public Button    AddCustom  { get; } = new() { Label = "add" };

    public TextInput Search        { get; } = new() { Placeholder = "search shared library…" };
    public Button    SearchBtn     { get; } = new() { Label = "go" };
    public Dropdown  SharedResults { get; } = new();
    public Button    UseShared     { get; } = new() { Label = "use" };

    public Color AccentColor { get; set; } = new Color(0, 200, 255);

    public IReadOnlyList<TextInput> TextInputs => new[] { CustomName, CustomId, Search };

    public void Layout(int x, int y, int width)
    {
        int cursor = y + 22;
        int rowH = 26, gap = 6, btnW = 60;
        int inputW = width - btnW - gap;

        CustomName.Bounds = new Rectangle(x, cursor, width, rowH); cursor += rowH + 4;
        CustomId.Bounds   = new Rectangle(x, cursor, inputW, rowH);
        AddCustom.Bounds  = new Rectangle(x + inputW + gap, cursor, btnW, rowH); cursor += rowH + 12;

        Search.Bounds        = new Rectangle(x, cursor, inputW, rowH);
        SearchBtn.Bounds     = new Rectangle(x + inputW + gap, cursor, btnW, rowH); cursor += rowH + 6;
        SharedResults.Bounds = new Rectangle(x, cursor, inputW, rowH);
        UseShared.Bounds     = new Rectangle(x + inputW + gap, cursor, btnW, rowH);
    }

    public void Update(WidgetInput input)
    {
        // Update dropdown first so it captures clicks while open.
        SharedResults.Update(input);
        if (SharedResults.Open) return;

        CustomName.Update(input);
        CustomId.Update(input);
        AddCustom.Update(input);
        Search.Update(input);
        SearchBtn.Update(input);
        UseShared.Update(input);
    }

    public void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel, Rectangle panelRect)
    {
        DrawPanel(batch, pixel, panelRect);
        text.DrawString(batch, "voices: custom + shared",
            new Vector2(panelRect.X + 8, panelRect.Y + 4),
            new Color(0, 220, 255), 14);

        CustomName.Draw(batch, text, pixel);
        CustomId.Draw(batch, text, pixel);
        AddCustom.Draw(batch, text, pixel);
        Search.Draw(batch, text, pixel);
        SearchBtn.Draw(batch, text, pixel);
        UseShared.Draw(batch, text, pixel);
        SharedResults.Draw(batch, text, pixel);
    }

    public void PopulateSharedResults(IReadOnlyList<VoiceInfo> voices)
    {
        SharedResults.Options.Clear();
        foreach (var v in voices)
            SharedResults.Options.Add((v.VoiceId, v.Name));
        SharedResults.SelectedIndex = voices.Count > 0 ? 0 : -1;
    }

    private void DrawPanel(SpriteBatch b, Texture2D px, Rectangle r)
    {
        b.Draw(px, r, new Color(0, 10, 20, 180));
        b.Draw(px, new Rectangle(r.X, r.Y,          r.Width, 1), AccentColor);
        b.Draw(px, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), AccentColor);
        b.Draw(px, new Rectangle(r.X, r.Y, 1,          r.Height), AccentColor);
        b.Draw(px, new Rectangle(r.Right - 1, r.Y, 1, r.Height), AccentColor);
    }
}
