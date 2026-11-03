using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The steam train that runs the railway: an olive engine with red wheels, its tender and three maroon coaches, made from
    /// boxes and cylinders in the toon way, with a RailTrain on it to drive it along the line. Scenery only: no colliders.
    /// </summary>
    internal static partial class AlpineWorld
    {
        static void MakeTrain(List<Vector3> rail)
        {
            var mats = new[]
            {
                TestWorldBuilder.Mat("Train_Olive", new Color(0.30f, 0.34f, 0.20f), 0.35f, 0.2f),
                TestWorldBuilder.Mat("Train_Black", new Color(0.08f, 0.08f, 0.09f), 0.3f, 0.3f),
                TestWorldBuilder.Mat("Train_Red", new Color(0.66f, 0.12f, 0.08f), 0.35f, 0.2f),
                TestWorldBuilder.Mat("Train_Maroon", new Color(0.46f, 0.15f, 0.11f), 0.3f),
                TestWorldBuilder.Mat("Train_Roof", new Color(0.36f, 0.36f, 0.38f), 0.2f),
                TestWorldBuilder.Mat("Train_Window", new Color(0.10f, 0.12f, 0.15f), 0.7f),
                TestWorldBuilder.Mat("Train_Brass", new Color(0.78f, 0.62f, 0.26f), 0.6f, 0.7f),
            };
            const int Olive = 0, Black = 1, Red = 2, Maroon = 3, Roof = 4, Window = 5, Brass = 6;
            var root = new GameObject("Train");
            var cars = new List<Transform>();
            var lengths = new List<float>();

            void Wheels(MeshBuilder mb, float z, float r)
            {
                foreach (float side in new[] { -1f, 1f })
                    mb.Tube(new Vector3(side * 1.05f, r, z), new Vector3(side * 1.45f, r, z), r, 12, Red);
                mb.Box(new Vector3(0, r, z), Quaternion.identity, new Vector3(2.1f, 0.18f, 0.18f), Black);   // the axle
            }
            Transform Car(string name, MeshBuilder mb, float len)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mb, "Train" + name.Replace(" ", ""));
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                cars.Add(go.transform);
                lengths.Add(len);
                return go.transform;
            }

            // the engine, 13 m: frame, boiler, smokebox, chimney, dome, cab, cowcatcher, three pairs of big red drivers
            {
                var mb = new MeshBuilder(mats.Length);
                mb.Box(new Vector3(0, 1.45f, 0), Quaternion.identity, new Vector3(2.7f, 0.5f, 12.6f), Black);
                mb.Tube(new Vector3(0, 2.95f, -1.6f), new Vector3(0, 2.95f, 5.4f), 1.25f, 16, Olive);
                mb.Tube(new Vector3(0, 2.95f, 5.4f), new Vector3(0, 2.95f, 6.2f), 1.32f, 16, Black);
                foreach (float z in new[] { 0.2f, 2.6f, 4.8f }) mb.Tube(new Vector3(0, 2.95f, z), new Vector3(0, 2.95f, z + 0.18f), 1.3f, 16, Brass);   // the boiler bands
                mb.Prism(new Vector3(0, 4.0f, 5.1f), 0.42f, 1.5f, 10, Black);
                mb.Prism(new Vector3(0, 5.45f, 5.1f), 0.6f, 0.25f, 10, Black);
                mb.Prism(new Vector3(0, 4.05f, 2.0f), 0.55f, 0.65f, 10, Brass);
                mb.Box(new Vector3(0, 3.45f, -4.4f), Quaternion.identity, new Vector3(2.9f, 3.0f, 3.4f), Olive);
                mb.Box(new Vector3(0, 5.05f, -4.4f), Quaternion.identity, new Vector3(3.3f, 0.25f, 3.9f), Black);
                foreach (float side in new[] { -1f, 1f })
                {
                    mb.Box(new Vector3(side * 1.46f, 3.9f, -4.2f), Quaternion.identity, new Vector3(0.05f, 1.0f, 1.2f), Window);
                    mb.Box(new Vector3(side * 1.36f, 2.0f, 1.5f), Quaternion.identity, new Vector3(0.25f, 0.2f, 8f), Red);   // the running board's edge
                }
                mb.Box(new Vector3(0, 0.95f, 6.6f), Quaternion.Euler(-35, 0, 0), new Vector3(2.4f, 1.4f, 0.25f), Red);      // the cowcatcher
                foreach (float z in new[] { -1.8f, 0.4f, 2.6f }) Wheels(mb, z, 0.95f);
                Wheels(mb, 5.3f, 0.5f); Wheels(mb, -5.3f, 0.55f);
                Car("Engine", mb, 13f);
            }
            // the tender, 7 m: a tank with coal heaped on it
            {
                var mb = new MeshBuilder(mats.Length);
                mb.Box(new Vector3(0, 2.35f, 0), Quaternion.identity, new Vector3(2.8f, 2.3f, 6.6f), Olive);
                mb.Box(new Vector3(0, 3.6f, 0.3f), Quaternion.identity, new Vector3(2.3f, 0.5f, 4.8f), Black);
                mb.Box(new Vector3(0, 1.1f, 0), Quaternion.identity, new Vector3(2.4f, 0.4f, 6.4f), Black);
                foreach (float z in new[] { -2.2f, 2.2f }) Wheels(mb, z, 0.55f);
                Car("Tender", mb, 7f);
            }
            // three coaches, 14 m: maroon sides, a row of windows each side, a grey roof, a bogie at each end
            for (int c = 0; c < 3; c++)
            {
                var mb = new MeshBuilder(mats.Length);
                mb.Box(new Vector3(0, 2.75f, 0), Quaternion.identity, new Vector3(2.9f, 2.7f, 13.6f), Maroon);
                mb.Box(new Vector3(0, 4.25f, 0), Quaternion.identity, new Vector3(3.05f, 0.4f, 13.9f), Roof);
                mb.Box(new Vector3(0, 1.25f, 0), Quaternion.identity, new Vector3(2.5f, 0.4f, 13.2f), Black);
                foreach (float side in new[] { -1f, 1f })
                    for (int w = 0; w < 6; w++)
                        mb.Box(new Vector3(side * 1.46f, 3.2f, -5.4f + w * 2.16f), Quaternion.identity, new Vector3(0.05f, 0.95f, 1.3f), Window);
                foreach (float z in new[] { -5.6f, -4.2f, 4.2f, 5.6f }) Wheels(mb, z, 0.5f);
                Car($"Coach {c + 1}", mb, 14f);
            }

            var train = root.AddComponent<Ooda.Fx.RailTrain>();
            train.path = rail.ToArray();
            train.cars = cars.ToArray();
            train.lengths = lengths.ToArray();
            train.chimney = new Vector3(0, 5.6f, 5.1f);
            train.Setup();
            Debug.Log($"OODA: a steam train of {cars.Count} cars, {lengths.Sum() + cars.Count * train.gapM:F0} m long, on {rail.Count * RailStep / 1000f:F1} km of line.");
        }
    }
}
