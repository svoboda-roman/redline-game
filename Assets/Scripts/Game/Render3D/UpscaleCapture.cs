using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Redline.Core;
using Redline.Core.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Dev-only evidence capture for the integer upscale and the menu backdrop. Active only when the environment
    /// variable <c>REDLINE_UPSCALE_CAPTURE</c> names an output directory. Drives the real <see cref="RedlineGame"/>:
    /// writes <c>menu-backdrop.png</c> (1920x1080, UI composited), then in a run renders one world frame and presents
    /// it at 1280x720, 1366x768, 1920x1080, 2560x1440 and 3840x2160 (<c>upscale-WxH.png</c>) plus the raw
    /// render target (<c>upscale-rt.png</c>), logs UPSCALE lines with each layout, and exits.
    /// </summary>
    /// <example>
    /// REDLINE_UPSCALE_CAPTURE=/abs/dir Unity -batchmode -projectPath . -executeMethod Redline.Game.RedlineCapture.RunFromCommandLine -render3d
    /// </example>
    /// <remarks>Never runs in normal play.</remarks>
    public sealed class UpscaleCapture : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_UPSCALE_CAPTURE";
        private static string Dir => Environment.GetEnvironmentVariable(EnvVar);
        private static readonly Vector2Int[] Sizes =
        {
            new Vector2Int(1280, 720), new Vector2Int(1366, 768), new Vector2Int(1920, 1080),
            new Vector2Int(2560, 1440), new Vector2Int(3840, 2160),
        };
        private RedlineGame _game;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (string.IsNullOrEmpty(Dir)) return;
            var go = new GameObject("UpscaleCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<UpscaleCapture>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            for (int i = 0; i < 3; i++) yield return null;
            _game = FindAnyObjectByType<RedlineGame>();
            var rig = FindAnyObjectByType<Pixel3DCamera>();
            if (_game == null || rig == null || !_game.Render3DActive) { Debug.LogError("UPSCALE_FAIL no 3D game"); Finish(1); yield break; }
            Call("ShowMainMenu", new object[] { null });
            yield return Wait(3.0f);   // cinematic bars in, calm grade eased out
            yield return MenuShot(rig, 1920, 1080, "menu-backdrop.png");

            Call("StartRun");
            float guard = 20f;
            while (_game.Sim.State != SimState.Fighting && guard > 0f) { guard -= Time.unscaledDeltaTime; Invuln(); yield return null; }
            float t = 0f;
            while (t < 2.5f) { t += Time.unscaledDeltaTime; Invuln(); yield return null; }

            rig.WorldCamera.Render();   // one world frame, presented at every size
            Save(ReadRt(rig.Target), "upscale-rt.png");
            foreach (var s in Sizes)
            {
                var tex = Present(rig, s.x, s.y);   // the rig re-lays out for the screen afterwards: log the pure layout
                var l = PixelUpscale.Compute(s.x, s.y, rig.Width, rig.Height, 1, 0f, 0f, 0, rig.MaxOverscan);
                Debug.Log($"UPSCALE {s.x}x{s.y} k={l.Scale} pic=({l.PictureX},{l.PictureY},{l.PictureW},{l.PictureH}) " +
                          $"visible=({l.VisibleX0},{l.VisibleY0})-({l.VisibleX1},{l.VisibleY1})");
                Save(tex, $"upscale-{s.x}x{s.y}.png");
            }
            Finish(0);
        }

        private void Invuln()
        {
            if (_game.Sim != null) _game.Sim.Player.InvulnTimer = 1f;
        }

        private static Texture2D Present(Pixel3DCamera rig, int w, int h)
        {
            var big = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rig.PresentCamera.targetTexture = big;
            rig.PresentCamera.aspect = (float)w / h;
            rig.RefreshLayout();
            rig.PresentCamera.Render();
            rig.PresentCamera.targetTexture = null;
            rig.PresentCamera.ResetAspect();
            rig.RefreshLayout();
            var t = ReadRt(big);
            big.Release(); Destroy(big);
            return t;
        }

        private IEnumerator MenuShot(Pixel3DCamera rig, int w, int h, string file)
        {
            var targets = new List<(float order, RenderTexture rt, PanelSettings ps)>();
            var seen = new HashSet<PanelSettings>();
            foreach (var d in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var ps = d.panelSettings;
                if (ps == null || !d.isActiveAndEnabled || !seen.Add(ps)) continue;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                ps.targetTexture = rt; ps.clearColor = true; ps.colorClearValue = new Color(0f, 0f, 0f, 0f);
                targets.Add((ps.sortingOrder, rt, ps));
            }
            targets.Sort((a, b) => a.order.CompareTo(b.order));
            for (int i = 0; i < 4; i++) yield return null;
            rig.WorldCamera.Render();
            var world = Present(rig, w, h);
            var px = world.GetPixels();
            foreach (var t in targets)
            {
                var uiTex = ReadRt(t.rt);
                var ui = uiTex.GetPixels();
                Destroy(uiTex);
                for (int i = 0; i < px.Length; i++)
                {
                    float a = ui[i].a;
                    if (a <= 0f) continue;
                    px[i] = new Color(ui[i].r + px[i].r * (1f - a), ui[i].g + px[i].g * (1f - a), ui[i].b + px[i].b * (1f - a), 1f);
                }
                t.ps.targetTexture = null; t.ps.clearColor = false;
                t.rt.Release(); Destroy(t.rt);
            }
            world.SetPixels(px);
            world.Apply();
            Save(world, file);
            // The backdrop alone, for the contrast measurement behind the text column.
            Save(Present(rig, w, h), "menu-backdrop-noui.png");
        }

        private static Texture2D ReadRt(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        private static void Save(Texture2D t, string file)
        {
            File.WriteAllBytes(Path.Combine(Dir, file), t.EncodeToPNG());
            Destroy(t);
            Debug.Log("UPSCALE_SHOT " + file);
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        private void Call(string method, object[] args = null)
        {
            var m = typeof(RedlineGame).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) Debug.LogWarning("UPSCALE: no method " + method);
            else m.Invoke(_game, args);
        }

        private static void Finish(int code)
        {
            Debug.Log("UPSCALE_DONE " + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit();
#endif
        }
    }
}
