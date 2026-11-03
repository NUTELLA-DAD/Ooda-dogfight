using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The factory town on the farmland in the south-east, the Town Strike's target (AlpineLayout places and levels it), with a factory (long sawtooth-roofed halls, chimneys and a water tower), streets of
    /// houses round it, and a runway strip with hangars on the west side, its take-off end to the south so the fighters
    /// climb out north up the valley. Three radar sites stand on high ground round the island. The turn battle finds all of
    /// it by name under "Town": FactoryTarget, RunwayStart, RunwayEnd and RadarSite 0, 1, 2.
    /// </summary>
    internal static partial class AlpineWorld
    {
        const float RunwayWidth = 40f;

        static void MakeTown(float[,] h, WMats m, List<Vector3> rail)
        {
            var root = new GameObject("Town").transform;
            float floor = TownFloor;
            Vector3 At(Vector2 p, float up = 0) => new Vector3(p.x, SampleArr(h, p.x, p.y) + up, p.y);
            void Mark(string name, Vector3 p) { var t = new GameObject(name).transform; t.SetParent(root, false); t.position = p; }

            var concrete = TestWorldBuilder.Mat("Town_Concrete", new Color(0.36f, 0.36f, 0.35f), 0.1f);
            var brick = TestWorldBuilder.Mat("Town_Brick", new Color(0.62f, 0.30f, 0.20f), 0.1f);
            var shed = TestWorldBuilder.Mat("Town_Shed", new Color(0.42f, 0.45f, 0.40f), 0.25f, 0.3f);
            var paint = TestWorldBuilder.Mat("Town_RunwayPaint", new Color(0.92f, 0.92f, 0.88f), 0.1f);

            // the runway: a concrete strip on the flat, with white end bars and centre dashes, and two hangars to the west
            {
                var mb = new MeshBuilder(3);   // 0 concrete, 1 paint, 2 shed
                Vector2 along = (RunwayEnd - RunwayStart).normalized;
                float len = Vector2.Distance(RunwayStart, RunwayEnd);
                var rot = Quaternion.LookRotation(new Vector3(along.x, 0, along.y));
                Vector2 mid = (RunwayStart + RunwayEnd) / 2;
                mb.Box(At(mid, -0.3f), rot, new Vector3(RunwayWidth, 1.2f, len), 0);
                for (float d = 60; d < len - 60; d += 60) mb.Box(At(RunwayStart + along * d, 0.32f), rot, new Vector3(1.2f, 0.1f, 24f), 1);
                foreach (float d in new[] { 12f, len - 12f })
                    for (int k = -3; k <= 3; k++)
                        mb.Box(At(RunwayStart + along * d, 0.32f) + rot * new Vector3(k * 4.5f, 0, 0), rot, new Vector3(2.2f, 0.1f, 14f), 1);
                Vector2 west = V(-along.y, along.x);   // to the left looking up the runway
                foreach (float d in new[] { 320f, 420f })
                {
                    Vector3 c = At(RunwayStart + along * d + west * 70f);
                    mb.Box(c + Vector3.up * 5f, rot, new Vector3(30f, 10f, 40f), 2);
                    mb.Box(c + Vector3.up * 10.5f, rot * Quaternion.Euler(0, 0, 0), new Vector3(31f, 1.5f, 41f), 2);
                }
                MeshObject("Runway", SaveMesh(mb, "Runway"), new[] { concrete, paint, shed }, root, true);
                Mark("RunwayStart", At(RunwayStart));
                Mark("RunwayEnd", At(RunwayEnd));
            }

            // the factory: four long halls side by side with sawtooth roofs, two chimneys, a water tower
            {
                var mb = new MeshBuilder(3);   // 0 brick, 1 roof (dark), 2 concrete
                Vector3 c = At(FactoryAt);
                for (int k = 0; k < 4; k++)
                {
                    Vector3 hc = c + new Vector3(-54 + k * 36, 0, 0);
                    mb.Box(hc + Vector3.up * 7f, Quaternion.identity, new Vector3(34f, 14f, 150f), 0);
                    for (float z = -70; z < 75; z += 10)   // the sawtooth: steep glazed faces to the north
                        mb.Box(hc + new Vector3(0, 16.2f, z), Quaternion.Euler(-28, 0, 0), new Vector3(34f, 0.8f, 11f), 1);
                }
                foreach (var (dx, dz) in new[] { (-90f, 40f), (-90f, -30f) })
                {
                    mb.Box(c + new Vector3(dx, 26f, dz), Quaternion.identity, new Vector3(6f, 52f, 6f), 0);
                    mb.Box(c + new Vector3(dx, 52.5f, dz), Quaternion.identity, new Vector3(7.2f, 1.5f, 7.2f), 2);
                }
                Vector3 tower = c + new Vector3(95, 0, -60);
                foreach (var (lx, lz) in new[] { (-3f, -3f), (3f, -3f), (-3f, 3f), (3f, 3f) })
                    mb.Box(tower + new Vector3(lx, 12f, lz), Quaternion.identity, new Vector3(0.8f, 24f, 0.8f), 2);
                mb.Ellipsoid(tower + Vector3.up * 29f, Quaternion.identity, new Vector3(6f, 5.5f, 6f), 12, 6, 2);
                mb.Box(c + new Vector3(0, 0.2f, 0), Quaternion.identity, new Vector3(190f, 0.6f, 190f), 2);   // the yard
                MeshObject("Factory", SaveMesh(mb, "Factory"), new[] { brick, m.roof, concrete }, root, true);
                Mark("FactoryTarget", c);
            }

            // the houses: plaster and wood with red roofs, in rough streets east and south of the factory
            {
                var mb = new MeshBuilder(3);          // 0 plaster, 1 wood, 2 roof
                var rnd = new System.Random(11);
                var placed = new List<Vector2>();
                for (int tries = 0; tries < 8000 && placed.Count < 170; tries++)
                {
                    var p = V(TownCentre.x - 250 + (float)rnd.NextDouble() * 1150, TownCentre.y - 900 + (float)rnd.NextDouble() * 1600);
                    p = V(Mathf.Round(p.x / 34f) * 34f + Rand(rnd, -4, 4), Mathf.Round(p.y / 30f) * 30f + Rand(rnd, -4, 4));   // on a rough grid: streets
                    if (TownR(p.x, p.y) > 0.92f) continue;
                    if (Vector2.Distance(p, FactoryAt) < 150 || Mathf.Abs(p.x - RunwayStart.x) < 150 || NearRail(rail, p.x, p.y, 30f)) continue;
                    if (placed.Any(q => Vector2.Distance(p, q) < 24)) continue;
                    if (Mathf.Abs(Mathf.Round((p.x - TownCentre.x) / 34f)) % 4 == 0) continue;   // every fourth row is a street
                    placed.Add(p);
                    float w = 9 + (float)rnd.NextDouble() * 5, d = 8 + (float)rnd.NextDouble() * 4, tall = rnd.NextDouble() < 0.3 ? 9f : 6f;
                    var rot = Quaternion.Euler(0, rnd.Next(2) * 90 + Rand(rnd, -4, 4), 0);
                    Vector3 c = At(p);
                    mb.Box(c + Vector3.up * (tall / 2 - 0.5f), rot, new Vector3(w, tall + 1f, d), rnd.NextDouble() < 0.7 ? 0 : 1);
                    float pitch = 32f, run = (w / 2 + 1f) / Mathf.Cos(pitch * Mathf.Deg2Rad);
                    foreach (float side in new[] { -1f, 1f })
                        mb.Box(c + rot * new Vector3(side * (w / 4 + 0.3f), tall + Mathf.Tan(pitch * Mathf.Deg2Rad) * w / 4, 0), rot * Quaternion.Euler(0, 0, -side * pitch),   // a positive z turn lifts +x: -side puts the eaves down
                               new Vector3(run, 0.5f, d + 1.6f), 2);
                }
                // a church with a tall spire on the square
                Vector3 ch = At(V(TownCentre.x + 120, TownCentre.y + 420));
                mb.Box(ch + Vector3.up * 7, Quaternion.identity, new Vector3(13, 14, 26), 0);
                mb.Box(ch + new Vector3(0, 16, -18), Quaternion.identity, new Vector3(7, 32, 7), 0);
                mb.Cone(ch + new Vector3(0, 32, -18), 5f, 16f, 4, 2);
                MeshObject("Houses", SaveMesh(mb, "TownHouses"), new[] { m.plaster, m.wood, m.roof }, root, true);
                Debug.Log($"OODA: a town of {placed.Count} houses round the factory, floor {floor:F0} m.");
            }

            MakeRadars(h, root, m);
        }

        /// <summary>A radar site on high ground near each of RadarNear: a lattice tower with a wide dish on top, and a hut.</summary>
        static void MakeRadars(float[,] h, Transform root, WMats m)
        {
            var mb = new MeshBuilder(2);   // 0 steel, 1 the hut
            var steel = TestWorldBuilder.Mat("Town_RadarSteel", new Color(0.33f, 0.36f, 0.33f), 0.3f, 0.5f);
            var made = new List<Vector3>();
            for (int k = 0; k < RadarNear.Length; k++)
            {
                Vector2 c = RadarNear[k], best = c;
                float bestY = float.MinValue;
                for (float dx = -RadarSearch; dx <= RadarSearch; dx += 60)
                    for (float dz = -RadarSearch; dz <= RadarSearch; dz += 60)
                    {
                        if (dx * dx + dz * dz > RadarSearch * RadarSearch) continue;
                        float x = c.x + dx, z = c.y + dz, y = SampleArr(h, x, z);
                        if (y > RadarMaxY || y < RadarMinY || Slope(h, x, z) > 22) continue;
                        if (y > bestY) { bestY = y; best = V(x, z); }
                    }
                if (bestY == float.MinValue) { bestY = SampleArr(h, c.x, c.y); Debug.LogWarning($"OODA: no good ground for radar {k} near ({c.x:F0}, {c.y:F0})."); }
                Vector3 foot = new Vector3(best.x, bestY, best.y);
                Lattice(mb, foot, 18f, 3.2f, 1.4f, 4, 3f, 0.25f, 0);
                mb.Box(foot + Vector3.up * 19f, Quaternion.identity, new Vector3(3f, 2f, 3f), 0);
                mb.Box(foot + new Vector3(0, 23f, 0), Quaternion.Euler(-12, 30 + 50 * k, 0), new Vector3(16f, 7f, 0.6f), 0);   // the antenna array
                mb.Box(foot + new Vector3(9, 2f, 4), Quaternion.Euler(0, 30, 0), new Vector3(8, 4, 5), 1);
                var mark = new GameObject($"RadarSite {k}").transform;
                mark.SetParent(root, false);
                mark.position = foot + Vector3.up * 23f;
                made.Add(mark.position);
            }
            MeshObject("Radars", SaveMesh(mb, "Radars"), new[] { steel, m.wood }, root, true);
            Debug.Log("OODA: radar sites " + string.Join(", ", made.Select(p => $"({p.x:F0}, {p.y:F0}, {p.z:F0})")) + ".");
        }
    }
}
