using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;

namespace TMLDroid.FnaProbe;

/// <summary>
/// Minimal FNA scene that exercises what Terraria needs: GL ES context through FNA3D,
/// SpriteBatch with the stock effect (MojoShader), render targets, FAudio output and touch.
/// </summary>
public sealed class ProbeGame : Game
{
    const int SpriteCount = 5000;

    readonly bool autorun;
    readonly GraphicsDeviceManager gdm;
    SpriteBatch batch = null!;
    Texture2D tile = null!;
    RenderTarget2D target = null!;
    DynamicSoundEffectInstance? tone;
    readonly Random rng = new(1);
    readonly Vector2[] pos = new Vector2[SpriteCount];
    readonly Vector2[] vel = new Vector2[SpriteCount];

    int frames, totalFrames, touchEvents;
    double secondAcc, elapsed;
    readonly List<double> fpsSamples = new();
    bool reported;
    readonly Stopwatch drawTime = new();
    readonly List<(string Name, bool Ok)> selfTest = new();
    BasicEffect overlayFx = null!;

    public ProbeGame(bool autorun)
    {
        this.autorun = autorun;
        gdm = new GraphicsDeviceManager(this)
        {
            IsFullScreen = true,
            SynchronizeWithVerticalRetrace = true,
            SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight,
        };
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        var pp = GraphicsDevice.PresentationParameters;
        Log($"device: backbuffer {pp.BackBufferWidth}x{pp.BackBufferHeight} {pp.BackBufferFormat}, " +
            $"adapter '{GraphicsAdapter.DefaultAdapter.Description}', profile {GraphicsDevice.GraphicsProfile}");

        batch = new SpriteBatch(GraphicsDevice);

        tile = new Texture2D(GraphicsDevice, 16, 16);
        var px = new Color[16 * 16];
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            px[y * 16 + x] = ((x / 4 + y / 4) & 1) == 0 ? Color.White : Color.Orange;
        tile.SetData(px);

        target = new RenderTarget2D(GraphicsDevice, 320, 180);
        SelfTest();
        overlayFx = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true };
        Log("selftest summary: " + string.Join(" ", selfTest.Select(t => $"{t.Name}={(t.Ok ? "OK" : "FAIL")}")));

        for (var i = 0; i < SpriteCount; i++)
        {
            pos[i] = new Vector2(rng.Next(pp.BackBufferWidth), rng.Next(pp.BackBufferHeight));
            vel[i] = new Vector2(rng.Next(-200, 200), rng.Next(-200, 200));
        }

        try
        {
            tone = new DynamicSoundEffectInstance(44100, AudioChannels.Mono) { Volume = 0.15f };
            tone.BufferNeeded += (_, _) => SubmitTone();
            SubmitTone();
            SubmitTone();
            tone.Play();
            Log("audio: FAudio DynamicSoundEffectInstance playing 440 Hz");
        }
        catch (Exception e)
        {
            Log("audio FAILED: " + e.Message);
        }
    }

    /// <summary>Renders known content into a render target and reads it back.</summary>
    void SelfTest()
    {
        void Check(string name, Action draw)
        {
            try
            {
                using var rt = new RenderTarget2D(GraphicsDevice, 64, 64);
                GraphicsDevice.SetRenderTarget(rt);
                GraphicsDevice.Clear(Color.Black);
                draw();
                GraphicsDevice.SetRenderTarget(null);
                var px = new Color[64 * 64];
                rt.GetData(px);
                var lit = px.Count(c => c.R + c.G + c.B > 30);
                var mid = px[32 * 64 + 32];
                Log($"selftest {name}: lit {lit}/{px.Length}, center=({mid.R},{mid.G},{mid.B},{mid.A}), corner=({px[0].R},{px[0].G},{px[0].B})");
                selfTest.Add((name, lit > 0));
            }
            catch (Exception e)
            {
                Log($"selftest {name} FAILED: {e.GetType().Name}: {e.Message}");
                selfTest.Add((name, false));
            }
        }

        Check("clear-red", () => GraphicsDevice.Clear(Color.Red));
        Check("spritebatch-tile", () =>
        {
            batch.Begin();
            batch.Draw(tile, new Rectangle(0, 0, 64, 64), Color.White);
            batch.End();
        });
        try
        {
            var back = new Color[16 * 16];
            tile.GetData(back);
            Log($"selftest texture-readback: white texels {back.Count(c => c == Color.White)}/256 (expect 128)");
        }
        catch (Exception e) { Log("selftest texture-readback FAILED: " + e.Message); }

        var quad = new[]
        {
            new VertexPositionColorTexture(new Vector3(-1, -1, 0), Color.Red, new Vector2(0, 1)),
            new VertexPositionColorTexture(new Vector3(-1, 1, 0), Color.Red, new Vector2(0, 0)),
            new VertexPositionColorTexture(new Vector3(1, -1, 0), Color.Red, new Vector2(1, 1)),
            new VertexPositionColorTexture(new Vector3(1, 1, 0), Color.Red, new Vector2(1, 0)),
        };
        Check("basiceffect-vertexcolor", () =>
        {
            using var fx = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true };
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            fx.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2);
        });
        Check("basiceffect-texture", () =>
        {
            using var fx = new BasicEffect(GraphicsDevice) { TextureEnabled = true, Texture = tile };
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
            fx.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2);
        });
        var indices = new short[] { 0, 1, 2, 2, 1, 3 };
        var vpc = quad.Select(v => new VertexPositionColor(v.Position, Color.Blue)).ToArray();
        void DrawBuffers(VertexBuffer vb, IndexBuffer ib)
        {
            using var fx = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true };
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.SetVertexBuffer(vb);
            GraphicsDevice.Indices = ib;
            fx.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 4, 0, 2);
            GraphicsDevice.SetVertexBuffer(null);
            GraphicsDevice.Indices = null;
        }
        Check("buffers-static", () =>
        {
            using var vb = new VertexBuffer(GraphicsDevice, typeof(VertexPositionColor), 4, BufferUsage.WriteOnly);
            using var ib = new IndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
            vb.SetData(vpc);
            ib.SetData(indices);
            DrawBuffers(vb, ib);
        });
        Check("buffers-dynamic-discard", () =>
        {
            using var vb = new DynamicVertexBuffer(GraphicsDevice, typeof(VertexPositionColor), 4, BufferUsage.WriteOnly);
            using var ib = new DynamicIndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
            vb.SetData(vpc, 0, 4, SetDataOptions.Discard);
            ib.SetData(indices, 0, 6, SetDataOptions.Discard);
            DrawBuffers(vb, ib);
        });
        Check("buffers-dynamic-nooverwrite", () =>
        {
            using var vb = new DynamicVertexBuffer(GraphicsDevice, typeof(VertexPositionColor), 4, BufferUsage.WriteOnly);
            using var ib = new IndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
            vb.SetData(vpc, 0, 4, SetDataOptions.NoOverwrite);
            ib.SetData(indices);
            DrawBuffers(vb, ib);
        });
        Check("spritebatch-tinted", () =>
        {
            batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            batch.Draw(tile, new Rectangle(16, 16, 32, 32), Color.Lime);
            batch.End();
        });
    }

    double phase;
    void SubmitTone()
    {
        const int samples = 4410;
        var buf = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var s = (short)(Math.Sin(phase) * short.MaxValue * 0.5);
            phase += 2 * Math.PI * 440 / 44100;
            buf[2 * i] = (byte)s;
            buf[2 * i + 1] = (byte)(s >> 8);
        }
        tone?.SubmitBuffer(buf);
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var pp = GraphicsDevice.PresentationParameters;
        for (var i = 0; i < SpriteCount; i++)
        {
            pos[i] += vel[i] * dt;
            if (pos[i].X < 0 || pos[i].X > pp.BackBufferWidth) vel[i].X = -vel[i].X;
            if (pos[i].Y < 0 || pos[i].Y > pp.BackBufferHeight) vel[i].Y = -vel[i].Y;
        }

        var touches = TouchPanel.GetState();
        touchEvents += touches.Count;

        frames++;
        totalFrames++;
        secondAcc += gameTime.ElapsedGameTime.TotalSeconds;
        elapsed += gameTime.ElapsedGameTime.TotalSeconds;
        if (secondAcc >= 1)
        {
            var fps = frames / secondAcc;
            fpsSamples.Add(fps);
            Log($"fps={fps:F1} draw={drawTime.Elapsed.TotalMilliseconds / Math.Max(frames, 1):F2}ms/frame sprites={SpriteCount} touches={touchEvents} t={elapsed:F0}s");
            frames = 0;
            secondAcc = 0;
            drawTime.Reset();
        }

        if (!reported && elapsed >= 15)
        {
            reported = true;
            var avg = fpsSamples.Skip(2).DefaultIfEmpty(0).Average();
            Log($"RESULT ok frames={totalFrames} avg_fps={avg:F1} audio={(tone != null ? tone.State.ToString() : "none")} autorun={autorun}");
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        drawTime.Start();
        var t = (float)gameTime.TotalGameTime.TotalSeconds;

        // Render target pass (Terraria renders lighting/water into targets)
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(new Color(0.1f, 0.2f + 0.2f * MathF.Sin(t), 0.5f));
        batch.Begin();
        batch.Draw(tile, new Rectangle(10, 10, 300, 160), Color.White * 0.5f);
        batch.End();
        GraphicsDevice.SetRenderTarget(null);

        GraphicsDevice.Clear(new Color(0.05f, 0.1f, 0.25f));
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
        var pp = GraphicsDevice.PresentationParameters;
        batch.Draw(target, new Rectangle(0, 0, pp.BackBufferWidth, pp.BackBufferHeight), Color.White);
        for (var i = 0; i < SpriteCount; i++)
            batch.Draw(tile, pos[i], Color.White);
        batch.End();
        DrawSelfTestOverlay();
        base.Draw(gameTime);
        drawTime.Stop();
    }

    /// <summary>
    /// One square per self test in the top-left corner: green = pass, red = fail. Drawn with
    /// non-indexed user primitives (the path verified to work) so it shows even if SpriteBatch fails.
    /// </summary>
    void DrawSelfTestOverlay()
    {
        var pp = GraphicsDevice.PresentationParameters;
        float w = pp.BackBufferWidth, h = pp.BackBufferHeight;
        const float size = 48, gap = 12;
        var verts = new List<VertexPositionColor>();
        void Quad(float x, float y, float s, Color c)
        {
            Vector3 P(float px, float py) => new(px / w * 2 - 1, 1 - py / h * 2, 0);
            verts.Add(new(P(x, y), c)); verts.Add(new(P(x + s, y), c)); verts.Add(new(P(x, y + s), c));
            verts.Add(new(P(x + s, y), c)); verts.Add(new(P(x + s, y + s), c)); verts.Add(new(P(x, y + s), c));
        }
        for (var i = 0; i < selfTest.Count; i++)
        {
            var x = gap + i * (size + gap);
            Quad(x - 3, gap - 3, size + 6, Color.Black);
            Quad(x, gap, size, selfTest[i].Ok ? Color.LimeGreen : Color.Red);
        }
        if (verts.Count == 0) return;
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        overlayFx.CurrentTechnique.Passes[0].Apply();
        GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, verts.ToArray(), 0, verts.Count / 3);
    }

    static void Log(string s) => Console.WriteLine("FNAPROBE " + s);
}
