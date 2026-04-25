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
        if (_particleVerts.Length != ParticleCount * 2)
            _particleVerts = new VertexPositionColor[ParticleCount * 2];

        for (int i = 0; i < ParticleCount; i++)
        {
            var p = _particles[i];
            float nearness = 1f - p.Z / GridDepth;
            var col = Tinted(tint, _brightness[i] * nearness * nearness);
            float r = 0.05f + nearness * 0.1f; // bigger as they approach
            _particleVerts[i * 2]     = new VertexPositionColor(new Vector3(p.X - r, p.Y, p.Z), col);
            _particleVerts[i * 2 + 1] = new VertexPositionColor(new Vector3(p.X + r, p.Y, p.Z), col);
        }
    }

    private static Color Tinted(Color tint, float alpha)
    {
        float a = Math.Clamp(alpha, 0f, 1f);
        return new Color((int)(tint.R * a), (int)(tint.G * a), (int)(tint.B * a));
    }

    private void SpawnParticle(int i, bool randomZ)
    {
        _particles[i] = new Vector3(
            (float)(_rng.NextDouble() * GridWidth * 2 - GridWidth),
            (float)(_rng.NextDouble() * GridY * 2    - GridY),
            randomZ ? (float)(_rng.NextDouble() * GridDepth) : GridDepth);
        _brightness[i] = (float)(_rng.NextDouble() * 0.6 + 0.4);
    }

    // Draws a 2D diagonal scrolling grid over a screen-space rectangle.
    // Lines run at 45 degrees, scrolling toward bottom-right.
    private VertexPositionColor[] _diagVerts = Array.Empty<VertexPositionColor>();
    private double _diagOffset;
    private const float DiagSpacing = 32f; // pixels between diagonal lines
    private const float DiagSpeed   = 18f; // pixels per second

    public void UpdateDiagonal(double deltaSeconds)
    {
        _diagOffset = (_diagOffset + deltaSeconds * DiagSpeed) % DiagSpacing;
    }

    public void DrawDiagonal(Rectangle target, Color tint)
    {
        if (_effect is null || _device is null) return;
        if (target.Width <= 0 || target.Height <= 0) return;

        // Build lines in screen-space pixel coords, then project with orthographic
        // Number of lines: enough to cover width+height with spacing
        int lineCount = (int)((target.Width + target.Height) / DiagSpacing) + 2;
        int needed = lineCount * 2 * 2; // *2 for each direction (/ and \), *2 verts
        if (_diagVerts.Length != needed)
            _diagVerts = new VertexPositionColor[needed];

        float offset = (float)_diagOffset;
        int v = 0;
        float w = target.Width;
        float h = target.Height;
        var col = Tinted(tint, 0.25f);

        for (int i = 0; i < lineCount; i++)
        {
            float t = -h + i * DiagSpacing + offset;
            // Lines going top-left → bottom-right (positive diagonal)
            // Start on top edge or left edge, end on bottom or right
            float x0 = t, y0 = 0f;
            float x1 = t + h, y1 = h;
            // Clip to [0,w] x [0,h]
            if (x0 < 0) { y0 -= x0; x0 = 0; }
            if (x1 > w) { y1 -= (x1 - w); x1 = w; }

            _diagVerts[v++] = new VertexPositionColor(
                ScreenToNdc(target.X + x0, target.Y + y0, target), col);
            _diagVerts[v++] = new VertexPositionColor(
                ScreenToNdc(target.X + x1, target.Y + y1, target), col);
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
                _device.DrawUserPrimitives(PrimitiveType.LineList, _diagVerts, 0, v / 2);
        }

        _device.BlendState        = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.Viewport          = saved;
    }

    // Maps a screen pixel within `rect` to NDC [-1, 1]
    private static Vector3 ScreenToNdc(float sx, float sy, Rectangle rect)
    {
        float nx = (sx - rect.X) / rect.Width  * 2f - 1f;
        float ny = 1f - (sy - rect.Y) / rect.Height * 2f;
        return new Vector3(nx, ny, 0f);
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _effect = null;
    }
}
