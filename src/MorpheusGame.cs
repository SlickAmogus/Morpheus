using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Morpheus;

public class MorpheusGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sprite = null!;
    private Color _bg = Color.Black;

    public MorpheusGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1024,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = true,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Morpheus";
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        _sprite = new SpriteBatch(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape)) Exit();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(_bg);
        base.Draw(gameTime);
    }
}
