using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Morpheus.Avatar;

// Stub — filled in next pass.
// Responsibilities: resolve sprite for (emotion, toolName, mouthOpen) with fallback cascade,
// load textures lazily, draw avatar frame in given rectangle.
public sealed class AvatarRenderer
{
    public void LoadAvatar(AvatarEntry entry, GraphicsDevice device) { }
    public void Draw(SpriteBatch batch, Rectangle bounds, AvatarState state) { }
}

public sealed class AvatarState
{
    public string Emotion { get; set; } = "idle";
    public string? ActiveTool { get; set; }
    public bool MouthOpen { get; set; }
}
