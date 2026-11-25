using System.Runtime.CompilerServices;

namespace TMLDroid.Probes;

/// <summary>Methods that probes hook. NoInlining keeps call sites real calls, like tML targets.</summary>
public class Targets
{
    public int Value = 3;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int AddOne(int x) => x + 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int AddOneForIL(int x) => x + 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual int GetValue() => Value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Thrower(int x) => x > 0 ? throw new InvalidOperationException("boom from Thrower") : x;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string GenericName<T>(T value) => typeof(T).Name;

    /// <summary>Like XNA Vector2: two floats, an HFA on arm64 (returned in s0/s1).</summary>
    public struct Vec2
    {
        public float X, Y;
        public Vec2(float x, float y) { X = x; Y = y; }
    }

    /// <summary>Larger than 16 bytes: returned via a hidden return buffer (x8 on arm64).</summary>
    public struct Big
    {
        public long A, B, C, D;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Vec2 MakeVec(float x, float y) => new(x, y);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Big MakeBig(long a) => new() { A = a, B = a + 1, C = a + 2, D = a + 3 };
}
