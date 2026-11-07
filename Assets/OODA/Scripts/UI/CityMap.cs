using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.UI
{
    /// <summary>
    /// The skyscraper city from above, for the briefing's start map: a picture of the towers (brighter the taller), the
    /// river and the parks, made once from City's boxes, drawn under the planes cut to what the map shows. The map is laid
    /// out round the arena's middle in feet, north up (the city's arena runs north, so nothing turns).
    /// </summary>
    public static class CityMap
    {
        const float MPerPx = 12f;
        static Texture2D tex;

        public static void Draw(Rect inner, double cxFt, double cyFt, float pxPerFt)
        {
            if (tex == null) tex = Make(City.Skyline);
            // the map's corners in metres from the city's middle, then as a part of the picture
            double halfW = inner.width / 2 / pxPerFt * Atmosphere.MPerFt, halfH = inner.height / 2 / pxPerFt * Atmosphere.MPerFt;
            double mx = cxFt * Atmosphere.MPerFt, my = cyFt * Atmosphere.MPerFt;
            float U(double m) => (float)((1 + (m + City.HalfX) / MPerPx) / tex.width);    // past the rim pixel
            float V(double m) => (float)((1 + (m + City.HalfZ) / MPerPx) / tex.height);
            float u0 = U(mx - halfW), u1 = U(mx + halfW), v0 = V(my - halfH), v1 = V(my + halfH);
            GUI.DrawTextureWithTexCoords(inner, tex, new Rect(u0, v0, u1 - u0, v1 - v0));
        }

        static Texture2D Make(City c)
        {
            int w = Mathf.RoundToInt(2 * City.HalfX / MPerPx), h = Mathf.RoundToInt(2 * City.HalfZ / MPerPx);   // 600 by 733: whole pixels of MPerPx
            var t = new Texture2D(w + 2, h + 2, TextureFormat.RGBA32, false) { name = "City map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[(w + 2) * (h + 2)];
            var clear = new Color(0, 0, 0, 0);
            var river = new Color(0.25f, 0.45f, 0.70f, 0.55f);
            var park = new Color(0.30f, 0.55f, 0.30f, 0.45f);
            var street = new Color(0.20f, 0.24f, 0.28f, 0.35f);
            for (int j = 0; j < h + 2; j++)
                for (int i = 0; i < w + 2; i++)
                {
                    if (i == 0 || j == 0 || i == w + 1 || j == h + 1) { px[j * (w + 2) + i] = clear; continue; }   // a clear rim: past the city, nothing
                    float x = City.CentreX - City.HalfX + (i - 0.5f) * MPerPx, z = City.CentreZ - City.HalfZ + (j - 0.5f) * MPerPx;
                    double top = c.HeightM(x, z);
                    Color col;
                    if (Mathf.Abs(x - City.CentreX) < City.RiverHalfM && top < City.StreetM + 1) col = river;
                    else if (top > City.StreetM + 0.5)
                    {
                        float k = Mathf.Clamp01((float)top / 420f);
                        col = Color.Lerp(new Color(0.42f, 0.48f, 0.55f, 0.55f), new Color(0.92f, 0.95f, 1f, 0.85f), Mathf.Sqrt(k));
                    }
                    else col = street;
                    px[j * (w + 2) + i] = col;
                }
            foreach (var p in c.pads)
            {
                if (p.kind != 1) continue;
                for (int j = Mathf.FloorToInt((p.z0 - (City.CentreZ - City.HalfZ)) / MPerPx) + 1; j <= Mathf.FloorToInt((p.z1 - (City.CentreZ - City.HalfZ)) / MPerPx); j++)
                    for (int i = Mathf.FloorToInt((p.x0 - (City.CentreX - City.HalfX)) / MPerPx) + 1; i <= Mathf.FloorToInt((p.x1 - (City.CentreX - City.HalfX)) / MPerPx); i++)
                        if (i > 0 && j > 0 && i <= w && j <= h) px[j * (w + 2) + i] = park;
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }
    }
}
