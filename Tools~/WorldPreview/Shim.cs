// Just enough of UnityEngine for the island's layout code (Editor/AlpineLayout, AlpineMountains, AlpineDress) to run
// outside Unity. Mathf.PerlinNoise is Ken Perlin's improved noise, not Unity's own, so the hills differ in detail from the
// real build; everything placed by design (valleys, arenas, the rail, the river, the town) is the same.
using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator *(float k, Vector2 a) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator /(Vector2 a, float k) => new Vector2(a.x / k, a.y / k);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : new Vector2(0, 0); } }
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static float Angle(Vector2 a, Vector2 b) => (float)(Math.Acos(Mathf.Clamp(Dot(a.normalized, b.normalized), -1, 1)) * 180 / Math.PI);
        public override string ToString() => $"({x:F1}, {y:F1})";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator *(float k, Vector3 a) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator /(Vector3 a, float k) => new Vector3(a.x / k, a.y / k, a.z / k);
        public static readonly Vector3 up = new Vector3(0, 1, 0), down = new Vector3(0, -1, 0), forward = new Vector3(0, 0, 1), zero = new Vector3(0, 0, 0), one = new Vector3(1, 1, 1);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
        public override string ToString() => $"({x:F1}, {y:F1}, {z:F1})";
    }

    public struct Quaternion
    {
        // kept as a rotation matrix: plenty for turning vectors
        float m00, m01, m02, m10, m11, m12, m20, m21, m22;
        public static Quaternion Euler(float x, float y, float z)
        {
            // Unity: z, then x, then y
            Quaternion Rx(float a) { float c = Mathf.Cos(a), s = Mathf.Sin(a); return new Quaternion { m00 = 1, m11 = c, m12 = -s, m21 = s, m22 = c }; }
            Quaternion Ry(float a) { float c = Mathf.Cos(a), s = Mathf.Sin(a); return new Quaternion { m00 = c, m02 = s, m11 = 1, m20 = -s, m22 = c }; }
            Quaternion Rz(float a) { float c = Mathf.Cos(a), s = Mathf.Sin(a); return new Quaternion { m00 = c, m01 = -s, m10 = s, m11 = c, m22 = 1 }; }
            return Ry(y * Mathf.Deg2Rad) * Rx(x * Mathf.Deg2Rad) * Rz(z * Mathf.Deg2Rad);
        }
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion
        {
            m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20, m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21, m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,
            m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20, m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21, m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,
            m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20, m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21, m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22,
        };
        public static Vector3 operator *(Quaternion q, Vector3 v) => new Vector3(q.m00 * v.x + q.m01 * v.y + q.m02 * v.z, q.m10 * v.x + q.m11 * v.y + q.m12 * v.z, q.m20 * v.x + q.m21 * v.y + q.m22 * v.z);
    }

    public struct Bounds
    {
        public Vector3 min, max;
        public Vector3 size => max - min;
        public Vector3 center => (min + max) / 2;
    }

    public class Mesh { public Vector3[] vertices; public int[] triangles; public Bounds bounds; }
    public class Material { }

    public static class Debug
    {
        public static bool quiet;
        public static void Log(object m) { if (!quiet) Console.WriteLine(m); }
        public static void LogWarning(object m) => Console.WriteLine("WARNING " + m);
        public static void LogError(object m) => Console.WriteLine("ERROR " + m);
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI, Deg2Rad = PI / 180f, Rad2Deg = 180f / PI;
        public static float Sin(float a) => (float)Math.Sin(a);
        public static float Cos(float a) => (float)Math.Cos(a);
        public static float Tan(float a) => (float)Math.Tan(a);
        public static float Atan(float a) => (float)Math.Atan(a);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sqrt(float a) => (float)Math.Sqrt(a);
        public static float Abs(float a) => Math.Abs(a);
        public static int Abs(int a) => Math.Abs(a);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Exp(float a) => (float)Math.Exp(a);
        public static float Log(float a) => (float)Math.Log(a);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Min(params float[] v) { float m = v[0]; foreach (var x in v) m = x < m ? x : m; return m; }
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Max(params float[] v) { float m = v[0]; foreach (var x in v) m = x > m ? x : m; return m; }
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); }
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.ToEven);
        public static float Round(float v) => (float)Math.Round(v, MidpointRounding.ToEven);
        public static float Floor(float v) => (float)Math.Floor(v);

        static readonly int[] P = MakeP();
        static int[] MakeP()
        {
            int[] p = { 151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180 };
            var q = new int[512];
            for (int i = 0; i < 512; i++) q[i] = p[i & 255];
            return q;
        }
        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        static double Grad(int h, double x, double y) { switch (h & 7) { case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y; case 4: return x; case 5: return -x; case 6: return y; default: return -y; } }
        public static float PerlinNoise(float fx, float fy)
        {
            double x = fx, y = fy;
            int X = (int)Math.Floor(x) & 255, Y = (int)Math.Floor(y) & 255;
            x -= Math.Floor(x); y -= Math.Floor(y);
            double u = Fade(x), v = Fade(y);
            int a = P[X] + Y, b = P[X + 1] + Y;
            double n = Lerp(Lerp(Grad(P[a], x, y), Grad(P[b], x - 1, y), u), Lerp(Grad(P[a + 1], x, y - 1), Grad(P[b + 1], x - 1, y - 1), u), v);
            return (float)(0.5 + 0.5 * n);
        }
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
