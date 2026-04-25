using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Morpheus.Audio;
using Morpheus.Ui.Widgets;

namespace Morpheus.Ui;

// Left sidebar: voice picker + ElevenLabs voice_settings controls.
public sealed class VoicePanel
{
    public Dropdown Voice { get; } = new() { Label = "" };
    public Slider Stability { get; } = new() { Label = "stability" };
    public Slider Similarity { get; } = new() { Label = "similarity" };
    public Slider Style { get; } = new() { Label = "style" };
    public Toggle SpeakerBoost { get; } = new() { Label = "speaker boost" };
    public Button Save { get; } = new() { Label = "save" };
    public Button Preview { get; } = new() { Label = "preview" };
    public Button Refresh { get; } = new() { Label = "refresh" };

    public Color AccentColor { get; set; } = new Color(0, 200, 255);

    private readonly List<Widget> _widgets;

    public VoicePanel()
    {
        _widgets = new List<Widget> { Voice, Refresh, Stability, Similarity, Style, SpeakerBoost, Preview, Save };
    }

    public void Layout(int x, int y, int width)
    {
        int cursor = y + 16;
        int refreshW = 70;
        Voice.Bounds   = new Rectangle(x, cursor, width - refreshW - 6, 30);
        Refresh.Bounds = new Rectangle(x + width - refreshW, cursor, refreshW, 30); cursor += 46;
        Stability.Bounds = new Rectangle(x, cursor, width, 34);         cursor += 44;
        Similarity.Bounds = new Rectangle(x, cursor, width, 34);        cursor += 44;
        Style.Bounds = new Rectangle(x, cursor, width, 34);             cursor += 44;
        SpeakerBoost.Bounds = new Rectangle(x, cursor, width, 22);      cursor += 32;
        int half = (width - 8) / 2;
        Preview.Bounds = new Rectangle(x, cursor, half, 28);
        Save.Bounds = new Rectangle(x + half + 8, cursor, half, 28);
    }

    public void Update(WidgetInput input)
    {
        // Update Voice first so its open dropdown handles clicks before anything below.
        Voice.Update(input);
        foreach (var w in _widgets)
        {
            if (w == Voice) continue;
            if (Voice.Open) continue; // eat clicks while dropdown open
            w.Update(input);
        }
    }

    public void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel, Rectangle panelRect)
    {
        DrawPanel(batch, pixel, panelRect);
        text.DrawString(batch, "voice", new Vector2(panelRect.X + 8, panelRect.Y + 4),
            new Color(0, 220, 255), 14);

        foreach (var w in _widgets)
        {
            if (w == Voice) continue;
            w.Draw(batch, text, pixel);
        }
        // Draw Voice last so its open list renders above the other widgets.
        Voice.Draw(batch, text, pixel);
    }

    public void PopulateVoices(IReadOnlyList<VoiceInfo> voices, string? selectId)
    {
        Voice.Options.Clear();
        foreach (var v in voices)
            Voice.Options.Add((v.VoiceId, $"{v.Name}{(v.Category is { Length: > 0 } c ? $"  [{c}]" : "")}"));
        Voice.SelectById(selectId);
    }

    public void BindFromSettings(Morpheus.Ui.MorpheusSettings s)
    {
        Stability.Value = s.VoiceStability;
        Similarity.Value = s.VoiceSimilarity;
        Style.Value = s.VoiceStyle;
        SpeakerBoost.Value = s.VoiceSpeakerBoost;
        Voice.SelectById(s.ElevenLabsVoiceId);
    }

    public void WriteToSettings(Morpheus.Ui.MorpheusSettings s)
    {
        s.VoiceStability = Stability.Value;
        s.VoiceSimilarity = Similarity.Value;
        s.VoiceStyle = Style.Value;
        s.VoiceSpeakerBoost = SpeakerBoost.Value;
        if (Voice.SelectedId is { Length: > 0 } id) s.ElevenLabsVoiceId = id;
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
