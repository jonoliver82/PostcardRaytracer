using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;

namespace PostcardSizedRaytracerDotNet
{
    public struct Vec
    {
        public float x, y, z;

        public static implicit operator Vec(float v) { return new Vec(a: v, b: v, c: v); }

        public Vec(float a, float b, float c = 0)
        {
            x = a;
            y = b;
            z = c;
        }

        public static Vec operator +(Vec q, Vec r) { return new Vec(q.x + r.x, q.y + r.y, q.z + r.z); }

        public static Vec operator *(Vec q, Vec r) { return new Vec(q.x * r.x, q.y * r.y, q.z * r.z); }

        public static float operator %(Vec q, Vec r) { return q.x * r.x + q.y * r.y + q.z * r.z; }

#if NETSTANDARD2_1 || NETCOREAPP2_0 || NETCOREAPP2_1 || NETCOREAPP2_2 || NETCOREAPP3_0
    // intnv square root
    public static Vec operator !(Vec q) {
      return q * (1.0f / MathF.Sqrt(q % q));
    }
#else
        public static Vec operator !(Vec q)
        {
            return q * (1.0f / (float)Math.Sqrt(q % q));
        }
#endif

        public override string ToString()
        {
            var format = ",10:N5"; // 5 decimal spaces, padded to 10 chars in total
            return string.Format("{{ x:{0" + format + "}, y:{1" + format + "}, z:{2" + format + "} }}", x, y, z);
        }
    }

    class Program
    {
        // Helper method to make porting easier, we can just write 'Vec(...)' instead of 'new Vec(..)'
        public static Vec Vec(float a, float b, float c = 0)
        {
            return new Vec(a, b, c);
        }

        private static float min(float l, float r) { return Math.Min(l, r); }

        private static Random random = new Random();

        private static float randomVal()
        {
            // TODO NextDouble() - 'greater than or equal to 0.0, and less than 1.0. i.e [0,1)
            return (float)random.NextDouble();
        }

        private static float fmodf(float x, float y)
        {
            return x % y; // according to https://stackoverflow.com/a/2690516
        }

        private static float fabsf(float x)
        {
            return Math.Abs(x);
        }

#if NETSTANDARD2_1 || NETCOREAPP2_0 || NETCOREAPP2_1 || NETCOREAPP2_2 || NETCOREAPP3_0
     private static float sqrtf(float x) {
      return MathF.Sqrt(x);
    }

    private static float powf(float x, float y) {
      return MathF.Pow(x, y);
    }

    private static float cosf(float x) {
      return MathF.Cos(x);
    }

    private static float sinf(float x) {
      return MathF.Sin(x);
    }
#else
        private static float sqrtf(float x)
        {
            return (float)Math.Sqrt(x);
        }

        private static float powf(float x, float y)
        {
            return (float)Math.Pow(x, y);
        }

        private static float cosf(float x)
        {
            return (float)Math.Cos(x);
        }

        private static float sinf(float x)
        {
            return (float)Math.Sin(x);
        }
#endif

        // Rectangle CSG equation. Returns minimum signed distance from
        // space carved by
        // lowerLeft vertex and opposite rectangle vertex upperRight.    
        //[MethodImpl(MethodImplOptions.AggressiveInlining)] // This causes .NET Core to be x3 slower!!
        static float BoxTest(Vec position, Vec lowerLeft, Vec upperRight)
        {
            lowerLeft = position + (lowerLeft * -1.0f);
            upperRight = upperRight + (position * -1.0f);
            return -min(
                    min(
                        min(lowerLeft.x, upperRight.x),
                        min(lowerLeft.y, upperRight.y)),
                    min(lowerLeft.z, upperRight.z));
        }

        const int HIT_NONE = 0;
        const int HIT_LETTER = 1;
        const int HIT_WALL = 2;
        const int HIT_SUN = 3;

        static char[] letters =              // 15 two points lines
               ("5O5_" + "5W9W" + "5_9_" +        // P (without curve)
                "AOEO" + "COC_" + "A_E_" +        // I
                "IOQ_" + "I_QO" +               // X
                "UOY_" + "Y_]O" + "WW[W" +        // A
                "aOa_" + "aWeW" + "a_e_" + "cWiO") // R (without curve)
            .ToCharArray();

        // Two curves (for P and R in PixaR) with hard-coded locations.
        static Vec[] curves = new[] { Vec(-11, 6), Vec(11, 6) };

        // Sample the world using Signed Distance Fields.
        //[MethodImpl(MethodImplOptions.AggressiveInlining)] // This causes .NET Core to be *way* (x5) slower!!
        static float QueryDatabase(Vec position, ref int hitType)
        {
            float distance = float.MaxValue; // 1e9;
            Vec f = position; // Flattened position (z=0)
            f.z = 0;

            for (int i = 0; i < letters.Length; i += 4)
            {
                Vec begin = Vec(letters[i] - 79, letters[i + 1] - 79) * .5f;
                Vec e = Vec(letters[i + 2] - 79, letters[i + 3] - 79) * .5f + begin * -1f;
                Vec o = f + (begin + e * min(-min((begin + f * -1) % e / (e % e),
                                                  0),
                                             1)
                            ) * -1;
                distance = min(distance, o % o); // compare squared distance.
            }
            distance = sqrtf(distance); // Get real distance, not square distance.

            for (int i = 1; i >= 0; i--)
            {
                Vec o = f + curves[i] * -1;
                // I *think* this equivalent to the C++ 'conditional expression', see https://stackoverflow.com/a/16676940
                float temp = 0.0f;
                if (o.x > 0)
                {
                    temp = fabsf(sqrtf(o % o) - 2);
                }
                else
                {
                    o.y += o.y > 0 ? -2 : 2;
                    temp = sqrtf(o % o);
                }
                distance = min(distance, temp);
            }
            distance = powf(powf(distance, 8) + powf(position.z, 8), 0.125f) - 0.5f;
            hitType = HIT_LETTER;

            float roomDist;
            roomDist = min(// min(A,B) = Union with Constructive solid geometry
                           //-min carves an empty space
                          -min(// Lower room
                               BoxTest(position, Vec(-30, -0.5f, -30), Vec(30, 18, 30)),
                               // Upper room
                               BoxTest(position, Vec(-25, 17, -25), Vec(25, 20, 25))
                          ),
                          BoxTest( // Ceiling "planks" spaced 8 units apart.
                            Vec(fmodf(fabsf(position.x), 8),
                                position.y,
                                position.z),
                            Vec(1.5f, 18.5f, -25),
                            Vec(6.5f, 20, 25)
                          )
            );
            if (roomDist < distance) { distance = roomDist; hitType = HIT_WALL; };

            float sun = 19.9f - position.y; // Everything above 19.9 is light source.
            if (sun < distance) { distance = sun; hitType = HIT_SUN; }

            return distance;
        }

        // Perform signed sphere marching
        // Returns hitType 0, 1, 2, or 3 and update hit position/normal
        static int RayMarching(Vec origin, Vec direction, ref Vec hitPos, ref Vec hitNorm)
        {
            int hitType = HIT_NONE;
            int noHitCount = 0;
            float d; // distance from closest object in world.

            // Signed distance marching
            for (float total_d = 0; total_d < 100; total_d += d)
                if ((d = QueryDatabase(hitPos = origin + direction * total_d, ref hitType)) < .01
                        || ++noHitCount > 99)
                {
                    hitNorm =
                       !Vec(QueryDatabase(hitPos + Vec(.01f, 0), ref noHitCount) - d,
                            QueryDatabase(hitPos + Vec(0, .01f), ref noHitCount) - d,
                            QueryDatabase(hitPos + Vec(0, 0, .01f), ref noHitCount) - d);
                    return hitType;
                }
            return 0;
        }

        static Vec Trace(Vec origin, Vec direction)
        {
            Vec sampledPosition = 0, normal = 0, color = 0, attenuation = 1;
            Vec lightDirection = (!Vec(.6f, .6f, 1f)); // Directional light

            for (int bounceCount = 3; bounceCount > 0; bounceCount--)
            {
                int hitType = RayMarching(origin, direction, ref sampledPosition, ref normal);
                if (hitType == HIT_NONE) break; // No hit. This is over, return color.
                if (hitType == HIT_LETTER)
                { // Specular bounce on a letter. No color acc.
                    direction = direction + normal * (normal % direction * -2);
                    origin = sampledPosition + direction * 0.1f;
                    attenuation = attenuation * 0.2f; // Attenuation via distance traveled.
                }
                if (hitType == HIT_WALL)
                { // Wall hit uses color yellow?
                    float incidence = normal % lightDirection;
                    float p = 6.283185f * randomVal();
                    float c = randomVal();
                    float s = sqrtf(1 - c);
                    float g = normal.z < 0 ? -1 : 1;
                    float u = -1 / (g + normal.z);
                    float v = normal.x * normal.y * u;
                    direction = Vec(v,
                                    g + normal.y * normal.y * u,
                                    -normal.y) * (cosf(p) * s)
                                +
                                Vec(1 + g * normal.x * normal.x * u,
                                    g * v,
                                    -g * normal.x) * (sinf(p) * s) + normal * sqrtf(c);
                    origin = sampledPosition + direction * .1f;
                    attenuation = attenuation * 0.2f;
                    if (incidence > 0 &&
                        RayMarching(sampledPosition + normal * .1f,
                                    lightDirection,
                                    ref sampledPosition,
                                    ref normal) == HIT_SUN)
                        color = color + attenuation * Vec(500, 400, 100) * incidence;
                }
                if (hitType == HIT_SUN)
                { //
                    color = color + attenuation * Vec(50, 80, 100); break; // Sun Color
                }
            }
            return color;
        }

        static void Main(string[] args)
        {
            int w = 960, h = 540, samplesCount = 8; // 1024; 
            Vec position = Vec(-22, 5, 25);
            Vec goal = !(Vec(-3, 4, 0) + position * -1);
            Vec left = !Vec(goal.z, 0, -goal.x) * (1.0f / w);

            var framework = Assembly
                      .GetEntryAssembly()?
                      .GetCustomAttribute<TargetFrameworkAttribute>()
                      ?.FrameworkName;
            Console.WriteLine($"C# Code - {framework ?? "Unknown"}");

            // Cross-product to get the up vector
            Vec up = Vec(
                goal.y * left.z - goal.z * left.y,
                goal.z * left.x - goal.x * left.z,
                goal.x * left.y - goal.y * left.x);

            string fileName = "output-C#.ppm";
            Console.WriteLine($"Width = {w}, Height = {h}, Samples = {samplesCount}");
            Console.WriteLine($"Writing data to {fileName}", fileName);

            if (File.Exists(fileName))
                File.Delete(fileName);
            using (var fileStream = File.Open(fileName, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new BinaryWriter(fileStream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes($"P6 {w} {h} 255 ")); // trailing space!!!
                for (int y = (h - 1); y >= 0; y--)
                {
                    for (int x = (w - 1); x >= 0; x--)
                    {
                        Vec color = 0;
                        for (int p = samplesCount - 1; p >= 0; p--)
                        {
                            color = color + Trace(position, !(goal + left * (x - w / 2 + randomVal()) + up * (y - h / 2 + randomVal())));
                        }

                        // Reinhard tone mapping
                        color = color * (1.0f / samplesCount) + 14.0f / 241;
                        Vec o = color + 1;
                        color = Vec(color.x / o.x, color.y / o.y, color.z / o.z) * 255;
                        writer.Write(new byte[] { (byte)(int)color.x, (byte)(int)color.y, (byte)(int)color.z });
                    }
                }
            }
        }
    }
}
