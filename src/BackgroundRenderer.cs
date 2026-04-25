using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Morpheus;

public sealed class BackgroundRenderer : IDisposable
{
    private BasicEffect? _effect;
    private GraphicsDevice? _device;

    private const int GridLinesX = 18;   // rails running toward vanishing point
    private const int GridLinesZ = 25;   // rungs scrolling toward viewer
    private const float GridWidth  = 10f;
    private const float GridDepth  = 50f;
    private const float GridY      = 1.4f; // floor/ceiling half-gap
    private const float ScrollSpeed = 6f;

    private const int ParticleCount = 120;
    private readonly Vector3[] _particles       = new Vector3[ParticleCount];
    private readonly float[]   _brightness      = new float[ParticleCount];
    private readonly Random    _rng             = new();

    private VertexPositionColor[] _gridVerts     = Array.Empty<VertexPositionColor>();
    private VertexPositionColor[] _particleVerts = Array.Empty<VertexPositionColor>();

    private double _scrollOffset;

    public void LoadContent(GraphicsDevice device)
    {
        _device = device;
        _effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            LightingEnabled    = false,
        };
        for (int i = 0; i < ParticleCount; i++)
            SpawnParticle(i, randomZ: true);
    }

    public void Update(double deltaSeconds)
    {
        _scrollOffset = (_scrollOffset + deltaSeconds * ScrollSpeed) % (GridDepth / GridLinesZ);

        for (int i = 0; i < ParticleCount; i++)
        {
            _particles[i].Z -= (float)(deltaSeconds * ScrollSpeed);
            if (_particles[i].Z < 0.5f) SpawnParticle(i, randomZ: false);
        }
    }

    public void Draw(Rectangle target, Color tint)
    {
        if (_effect is null || _device is null) return;
        if (target.Width <= 0 || target.Height <= 0) return;

        var saved = _device.Viewport;
        _device.Viewport = new Viewport(target);

        _effect.View = Matrix.CreateLookAt(
            new Vector3(0f, 0f, -0.5f),
            new Vector3(0f, 0f, 100f),
            Vector3.Up);
        _effect.Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(68f),
            target.Width / (float)Math.Max(1, target.Height),
            0.1f, 300f);
        _effect.World = Matrix.Identity;

        BuildGrid(tint);
        BuildParticles(tint);

        _device.BlendState        = BlendState.Additive;
        _device.DepthStencilState = DepthStencilState.None;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (_gridVerts.Length >= 2)
                _device.DrawUserPrimitives(PrimitiveType.LineList, _gridVerts, 0, _gridVerts.Length / 2);
            if (_particleVerts.Length >= 2)
                _device.DrawUserPrimitives(PrimitiveType.LineList, _particleVerts, 0, _particleVerts.Length / 2);
        }

        _device.BlendState        = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.Viewport          = saved;
    }

    private void BuildGrid(Color tint)
    {
        int totalLines = (GridLinesX + GridLinesZ) * 2; // *2 for floor + ceiling
        if (_gridVerts.Length != totalLines * 2)
            _gridVerts = new VertexPositionColor[totalLines * 2];

        int v = 0;
        float rungSpacing = GridDepth / GridLinesZ;

        foreach (float y in new[] { -GridY, GridY })
        {
            // Rails — lines running along Z at fixed X (converge to vanishing point)
            for (int i = 0; i < GridLinesX; i++)
            {
                float t = i / (float)(GridLinesX - 1);
                float x  = MathHelper.Lerp(-GridWidth, GridWidth, t);
                float edge = 1f - Math.Abs(t * 2f - 1f); // bright near centre rails
                var col = Tinted(tint, 0.15f + edge * 0.45f);
                _gridVerts[v++] = new VertexPositionColor(new Vector3(x, y,  1f),        col);
                _gridVerts[v++] = new VertexPositionColor(new Vector3(x, y, GridDepth),  col);
            }

            // Rungs — lines running along X at fixed Z, scrolling toward viewer
            for (int i = 0; i < GridLinesZ; i++)
            {
                float z = 1f + i * rungSpacing - (float)_scrollOffset;
                if (z < 1f) z += GridDepth;
                float nearness = 1f - (z / GridDepth);
                var col = Tinted(tint, nearness * 0.55f);
                _gridVerts[v++] = new VertexPositionColor(new Vector3(-GridWidth, y, z), col);
                _gridVerts[v++] = new VertexPositionColor(new Vector3( GridWidth, y, z), col);
            }
        }
    }

    private void BuildParticles(Color tint)
    {
        // 4 verts per star (horizontal arm + vertical arm)
        if (_particleVerts.Length != ParticleCount * 4)
            _particleVerts = new VertexPositionColor[ParticleCount * 4];

        for (int i = 0; i < ParticleCount; i++)
        {
            var p = _particles[i];
            float nearness = 1f - p.Z / GridDepth;
            float bright = _brightness[i] * nearness * nearness;
            // Near-white with a very faint cyan tint
            var col = new Color(
                (int)(Math.Min(1f, bright * 0.95f) * 255),
                (int)(Math.Min(1f, bright * 1.00f) * 255),
                (int)(Math.Min(1f, bright * 1.10f) * 255));
            float r = 0.04f + nearness * 0.12f;
            // Horizontal arm
            _particleVerts[i * 4]     = new VertexPositionColor(new Vector3(p.X - r, p.Y,      p.Z), col);
            _particleVerts[i * 4 + 1] = new VertexPositionColor(new Vector3(p.X + r, p.Y,      p.Z), col);
            // Vertical arm (shorter — grid is wider than tall)
            _particleVerts[i * 4 + 2] = new VertexPositionColor(new Vector3(p.X, p.Y - r * 0.5f, p.Z), col);
            _particleVerts[i * 4 + 3] = new VertexPositionColor(new Vector3(p.X, p.Y + r * 0.5f, p.Z), col);
        }
    }

    private static Color Tinted(Color tint, float alpha)
    {
        float a = Math.Clamp(alpha, 0f, 1f);
        return new Color((int)(tint.R * a), (int)(tint.G * a), (int)(tint.B * a));
    }

    private static Color HsvToColor(float h, float s, float v, float alpha)
    {
        h = ((h % 1f) + 1f) % 1f;
        int i = (int)(h * 6);
        float f = h * 6f - i;
        float p = v * (1f - s);
        float q = v * (1f - f * s);
        float t = v * (1f - (1f - f) * s);
        float r, g, b;
        switch (i % 6)
        {
            case 0:  r = v; g = t; b = p; break;
            case 1:  r = q; g = v; b = p; break;
            case 2:  r = p; g = v; b = t; break;
            case 3:  r = p; g = q; b = v; break;
            case 4:  r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        int a = (int)(Math.Clamp(alpha, 0f, 1f) * 255);
        return new Color((int)(r * 255), (int)(g * 255), (int)(b * 255), a);
    }

    private void SpawnParticle(int i, bool randomZ)
    {
        _particles[i] = new Vector3(
            (float)(_rng.NextDouble() * GridWidth * 2 - GridWidth),
            (float)(_rng.NextDouble() * GridY * 2    - GridY),
            randomZ ? (float)(_rng.NextDouble() * GridDepth) : GridDepth);
        _brightness[i] = (float)(_rng.NextDouble() * 0.6 + 0.4);
    }

    // Spinning vortex tunnel — concentric rotating rings connected by spoke lines.
    private VertexPositionColor[] _vortexVerts = Array.Empty<VertexPositionColor>();
    private double _vortexTime;
    private const int VortexRings    = 12;
    private const int VortexSegments = 48; // smoothness of each ring
    private const int VortexSpokes   = 8;  // connector lines between rings

    public void UpdateDiagonal(double deltaSeconds) =>
        _vortexTime += deltaSeconds;

    public void DrawDiagonal(Rectangle target, Color tint, float hueOffset = 0f)
    {
        if (_effect is null || _device is null) return;
        if (target.Width <= 0 || target.Height <= 0) return;

        float cx = target.Width  * 0.5f;
        float cy = target.Height * 0.5f;
        float maxR = Math.Min(target.Width, target.Height) * 0.48f;

        // Vertex budget:
        //   Rings: VortexRings * VortexSegments * 2 (line-list around each ring)
        //   Spokes: VortexSpokes * (VortexRings - 1) * 2 (line between adjacent rings)
        int ringVerts  = VortexRings * VortexSegments * 2;
        int spokeVerts = VortexSpokes * (VortexRings - 1) * 2;
        int total = ringVerts + spokeVerts;
        if (_vortexVerts.Length != total)
            _vortexVerts = new VertexPositionColor[total];

        int v = 0;
        float t = (float)_vortexTime;

        // Pre-compute ring positions (angle offsets and radii)
        Span<float> radii   = stackalloc float[VortexRings];
        Span<float> angles  = stackalloc float[VortexRings];
        for (int r = 0; r < VortexRings; r++)
        {
            float frac = r / (float)(VortexRings - 1);           // 0=inner 1=outer
            radii[r]  = maxR * (frac * frac * 0.95f + 0.02f);   // quadratic, perspective feel
            // Inner rings spin faster in one direction, outer slower in opposite
            angles[r] = t * MathHelper.TwoPi * (0.6f - frac * 0.5f);
        }

        // Draw rings — each ring gets a prismatic hue that slowly rotates over time
        for (int r = 0; r < VortexRings; r++)
        {
            float frac  = r / (float)(VortexRings - 1);
            float alpha = 0.12f + frac * 0.45f;
            float hue   = (((float)(r / (float)VortexRings) + (float)(_vortexTime * 0.08) + hueOffset) % 1f + 1f) % 1f;
            var col = HsvToColor(hue, 1f, 1f, alpha);

            for (int s = 0; s < VortexSegments; s++)
            {
                float a0 = angles[r] + s       / (float)VortexSegments * MathHelper.TwoPi;
                float a1 = angles[r] + (s + 1) / (float)VortexSegments * MathHelper.TwoPi;
                var p0 = new Vector2(cx + MathF.Cos(a0) * radii[r], cy + MathF.Sin(a0) * radii[r]);
                var p1 = new Vector2(cx + MathF.Cos(a1) * radii[r], cy + MathF.Sin(a1) * radii[r]);
                _vortexVerts[v++] = new VertexPositionColor(PixelToNdc(p0, target), col);
                _vortexVerts[v++] = new VertexPositionColor(PixelToNdc(p1, target), col);
            }
        }

        // Draw spokes — interpolate hue between adjacent rings
        for (int sp = 0; sp < VortexSpokes; sp++)
        {
            float spokeAngleBase = sp / (float)VortexSpokes * MathHelper.TwoPi;
            for (int r = 0; r < VortexRings - 1; r++)
            {
                float frac  = r / (float)(VortexRings - 1);
                float alpha = 0.08f + frac * 0.22f;
                float hue   = (((float)(r / (float)VortexRings) + (float)(_vortexTime * 0.08) + hueOffset) % 1f + 1f) % 1f;
                var col = HsvToColor(hue, 1f, 1f, alpha);

                float a0 = spokeAngleBase + angles[r];
                float a1 = spokeAngleBase + angles[r + 1];
                var p0 = new Vector2(cx + MathF.Cos(a0) * radii[r],     cy + MathF.Sin(a0) * radii[r]);
                var p1 = new Vector2(cx + MathF.Cos(a1) * radii[r + 1], cy + MathF.Sin(a1) * radii[r + 1]);
                _vortexVerts[v++] = new VertexPositionColor(PixelToNdc(p0, target), col);
                _vortexVerts[v++] = new VertexPositionColor(PixelToNdc(p1, target), col);
            }
        }

        var saved = _device.Viewport;
        _device.Viewport = new Viewport(target);
        _effect.View       = Matrix.Identity;
        _effect.Projection = Matrix.Identity;
        _effect.World      = Matrix.Identity;

        _device.BlendState        = BlendState.Additive;
        _device.DepthStencilState = DepthStencilState.None;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (v >= 2)
                _device.DrawUserPrimitives(PrimitiveType.LineList, _vortexVerts, 0, v / 2);
        }

        _device.BlendState        = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.Viewport          = saved;
    }

    // TV static — white/gray translucent noise dots drawn each frame.
    private const int StaticDotCount = 1800;
    private readonly Random _staticRng = new();

    // Called from SpriteBatch.Begin..End block; pixel is a 1×1 white texture.
    public void DrawStatic(SpriteBatch batch, Texture2D pixel, Rectangle target)
    {
        for (int i = 0; i < StaticDotCount; i++)
        {
            int x = target.X + _staticRng.Next(target.Width - 1);
            int y = target.Y + _staticRng.Next(target.Height - 1);
            int alpha  = _staticRng.Next(10, 54);
            int bright = (int)(_staticRng.Next(160, 256) * alpha / 255f); // premultiply for AlphaBlend
            int size   = _staticRng.Next(100) < 8 ? 3 : 2; // occasional larger fleck
            batch.Draw(pixel, new Rectangle(x, y, size, size), new Color(bright, bright, bright, alpha));
        }
    }

    // Maps a pixel in local rect-space [0,w]x[0,h] to NDC [-1,1]
    private static Vector3 PixelToNdc(Vector2 p, Rectangle rect)
    {
        float nx = p.X / rect.Width  * 2f - 1f;
        float ny = 1f - p.Y / rect.Height * 2f;
        return new Vector3(nx, ny, 0f);
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _effect = null;
    }
}
