using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.UI
{
    /// <summary>
    /// The cloud sea from above, for the briefing's start map (as CityMap is for the city): the deck a faint wash with its
    /// holes, the towers and the heaps standing up out of it brighter the higher they reach, the thunderheads a stormy
    /// violet. Made once from Skyscape's puffs and drawn under the planes, cut to what the map shows, north up.
    /// </summary>
    public static class SkyMap
    {
        const float MPerPx = 30f;
        static Texture2D tex;

        public static void Draw(Rect inner, double cxFt, double cyFt, float pxPerFt)
        {
            if (tex == null) tex = Make(Skyscape.Sky);
            double halfW = inner.width / 2 / pxPerFt * Atmosphere.MPerFt, halfH = inner.height / 2 / pxPerFt * Atmosphere.MPerFt;
            double mx = cxFt * Atmosphere.MPerFt, my = cyFt * Atmosphere.MPerFt;
            float half = tex.width * MPerPx / 2;
            float U(double m) => (float)((m + half) / (2 * half));
            float u0 = U(mx - halfW), u1 = U(mx + halfW), v0 = U(my - halfH), v1 = U(my + halfH);
            GUI.DrawTextureWithTexCoords(inner, tex, new Rect(u0, v0, u1 - u0, v1 - v0));
        }

        static Texture2D Make(Skyscape s)
        {
            int n = Mathf.CeilToInt(2 * (Skyscape.RadiusM + 2000f) / MPerPx);
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Cloud sea map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float x0 = Skyscape.CentreX - n * MPerPx / 2, z0 = Skyscape.CentreZ - n * MPerPx / 2;
            var deck = new Color(0.75f, 0.80f, 0.95f, 0.16f);
            var storm = new Color(0.62f, 0.56f, 0.92f, 0.8f);
            foreach (var p in s.puffs)
            {
                if (p.kind == Skyscape.Rain || p.kind == Skyscape.Cirrus) continue;
                bool low = p.y < Skyscape.DeckTopM + 50f;
                float top = p.y + p.r;
                Color col = low ? deck
                    : p.kind == Skyscape.Storm ? storm
                    : Color.Lerp(new Color(0.70f, 0.76f, 0.92f, 0.5f), new Color(0.97f, 0.98f, 1f, 0.85f), Mathf.Clamp01((top - 1500f) / 4000f));
                float rr = p.r * p.stretch / MPerPx;
                int ci = Mathf.RoundToInt((p.x - x0) / MPerPx), cj = Mathf.RoundToInt((p.z - z0) / MPerPx), R = Mathf.CeilToInt(rr);
                for (int j = cj - R; j <= cj + R; j++)
                    for (int i = ci - R; i <= ci + R; i++)
                    {
                        if (i < 1 || j < 1 || i >= n - 1 || j >= n - 1) continue;   // (a clear rim: past the clouds, nothing)
                        float dx = i - ci, dz = j - cj;
                        if (dx * dx + dz * dz > rr * rr) continue;
                        int k = j * n + i;
                        // the higher cloud over the lower: keep whichever is more solid
                        if (col.a > px[k].a) px[k] = col;
                    }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }
    }
}
