using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ooda.UI
{
    /// <summary>
    /// The first thing the game shows: Kenny's blue and red fighters chasing round their turn circles on navy, the two
    /// circles all but touching, while the title loads behind. It lives in <c>Scenes/Boot.unity</c> (made by
    /// OODA > Build Boot Screen, and by every Build Windows Game, which puts it first in the build), loads
    /// <see cref="nextScene"/> in the background, holds for at least <see cref="minSeconds"/>, and fades out over the
    /// title once that has drawn. The planes are <c>Resources/Loading/PlaneBlue.png</c> and <c>PlaneRed.png</c>, nose up;
    /// the circles' fading trails are drawn here.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        [Tooltip("The scene to load behind the spinning planes.")]
        public string nextScene = "Title";
        [Tooltip("The shortest time the planes spin, seconds, so a quick load doesn't just flash.")]
        public float minSeconds = 2f;
        [Tooltip("Seconds for one lap of the circle.")]
        public float lapSeconds = 2.4f;
        [Tooltip("Seconds to fade out over the title.")]
        public float fadeSeconds = 0.5f;

        static readonly Color Navy = new Color32(5, 19, 64, 255);
        static readonly Color Blue = new Color32(0, 196, 240, 255);
        static readonly Color Red = new Color32(242, 104, 84, 255);

        Texture2D planeBlue, planeRed, trail, fill;
        GUIStyle nameStyle;
        float spin, alpha = 1f;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);   // stays drawn over the title while it fades in
            planeBlue = Resources.Load<Texture2D>("Loading/PlaneBlue");
            planeRed = Resources.Load<Texture2D>("Loading/PlaneRed");
            fill = new Texture2D(1, 1);
            fill.SetPixel(0, 0, Color.white);
            fill.Apply();
            trail = MakeTrail(512);
        }

        IEnumerator Start()
        {
            float t0 = Time.realtimeSinceStartup;
            yield return null;   // draw the planes once before the load starts taking the frames
            var op = SceneManager.LoadSceneAsync(nextScene);
            if (op == null) { Debug.LogWarning($"OODA: the loading screen can't find the scene {nextScene}."); Destroy(gameObject); yield break; }
            op.allowSceneActivation = false;
            while (op.progress < 0.9f || Time.realtimeSinceStartup - t0 < minSeconds) yield return null;
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;
            for (int i = 0; i < 3; i++) yield return null;   // let the title build its first frames under cover
            for (float t = 0; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                alpha = 1f - t / fadeSeconds;
                yield return null;
            }
            Destroy(gameObject);
        }

        void Update() => spin = (spin + Time.unscaledDeltaTime / Mathf.Max(0.1f, lapSeconds) * 360f) % 360f;

        void OnDestroy()
        {
            if (trail != null) Destroy(trail);
            if (fill != null) Destroy(fill);
        }

        void OnGUI()
        {
            GUI.depth = -1000;   // over the title's own GUI while fading
            float sw = Screen.width, sh = Screen.height;
            var keep = GUI.matrix;
            GUI.color = new Color(Navy.r, Navy.g, Navy.b, alpha);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), fill);

            // two circles side by side, a hair apart, both flown clockwise, the planes half a lap apart
            float r = Mathf.Min(sw * 0.17f, sh * 0.24f);
            float gap = r * 0.06f;
            var cBlue = new Vector2(sw / 2 - r - gap / 2, sh * 0.47f);
            var cRed = new Vector2(sw / 2 + r + gap / 2, sh * 0.47f);
            Circle(cBlue, r, spin + 135f, Blue, planeBlue);
            Circle(cRed, r, spin + 315f, Red, planeRed);
            GUI.matrix = keep;

            if (nameStyle == null)
            {
                nameStyle = BriefingStyle.Heading(GUI.skin.label, 48);
                nameStyle.alignment = TextAnchor.MiddleCenter;
            }
            nameStyle.fontSize = Mathf.RoundToInt(r * 0.28f);
            GUI.color = new Color(1f, 0.97f, 0.9f, alpha);
            nameStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(0, cBlue.y + r * 1.35f, sw, r * 0.4f), TitleScreen.GameName.ToUpperInvariant(), nameStyle);
            GUI.color = Color.white;
        }

        /// <summary>One circle: the trail fading back from the plane, and the plane on the rim heading clockwise.
        /// <paramref name="angle"/> is the plane's place, degrees clockwise from the top.</summary>
        void Circle(Vector2 centre, float r, float angle, Color tint, Texture2D plane)
        {
            var keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, centre);
            float ring = r * 1.0f + r * 0.05f;   // the trail texture's outer edge sits just outside the path
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.DrawTexture(new Rect(centre.x - ring, centre.y - ring, ring * 2, ring * 2), trail);
            if (plane != null)
            {
                var at = new Vector2(centre.x, centre.y - r);   // the top of the circle, in the rotated frame
                float size = r * 0.62f;
                GUIUtility.RotateAroundPivot(90f, at);           // nose along the path, clockwise
                GUI.color = new Color(1, 1, 1, alpha);
                GUI.DrawTexture(new Rect(at.x - size / 2, at.y - size / 2, size, size), plane);
            }
            GUI.matrix = keep;
        }

        /// <summary>A white ring, bright at the top (where the plane is) and fading away anticlockwise behind it, gone
        /// by about seven eighths of the way round. Tinted per side when drawn.</summary>
        static Texture2D MakeTrail(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float outer = n / 2f, path = outer / 1.05f, half = path * 0.045f;   // the ring's half width
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - outer, dy = y + 0.5f - outer;   // texture rows run bottom up: dy > 0 is the top
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Clamp01(half + 1f - Mathf.Abs(d - path));   // anti-aliased band
                    // angle clockwise from the top; the trail is behind (anticlockwise of) the plane
                    float a = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;   // -180..180, + is clockwise on screen
                    float behind = a <= 0 ? -a : 360f - a;           // 0 at the plane, growing back along the path
                    float k = Mathf.Clamp01(1f - behind / 315f);    // nothing ahead of the plane: that is 360 behind
                    byte b = (byte)Mathf.RoundToInt(255 * Mathf.Lerp(0.55f, 1f, k));
                    px[y * n + x] = new Color32(b, b, b, (byte)Mathf.RoundToInt(255 * edge * k * k));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
