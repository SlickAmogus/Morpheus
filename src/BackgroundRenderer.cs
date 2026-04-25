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

    public void Dispose()
    {
        _effect?.Dispose();
        _effect = null;
    }
}
