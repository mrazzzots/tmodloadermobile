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
        base.Draw(gameTime);
        drawTime.Stop();
    }

    static void Log(string s) => Console.WriteLine("FNAPROBE " + s);
}
