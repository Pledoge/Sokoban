using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Dev
{
    using Game;

    /// <summary>
    /// 离屏 UI 截图工具（编辑器开发自检用，见 04 流程 · 工具&AI提效）：
    /// 把指定 UI 页挂到带 RenderTexture 的临时相机，按任意分辨率同步渲染成 PNG。
    /// 不依赖 Game 视图当前分辨率 —— 一键产出「各页面 × 各屏幕比例」截图流水线。
    /// 用法：var log = UIScreenshotter.RunNow(jobs);（全同步，调用即出图）
    /// </summary>
    public static class UIScreenshotter
    {
        public class Job
        {
            public string Page;          // 场景根名：MainMenu / LevelSelect / GameHUD / PausePanel / WinPanel
            public int W, H;             // 渲染分辨率（同时作为适配器输入）
            public string Path;          // PNG 输出路径
            public string Pre;           // 可选预操作：LevelSelect@classic|extended / Play@N（进入全列表第 N 关）
        }

        public static string RunNow(Job[] jobs)
        {
            var sb = new StringBuilder();
            foreach (var job in jobs)
            {
                try { sb.AppendLine(Path.GetFileName(job.Path) + " (" + job.W + "x" + job.H + ") => " + CaptureSync(job)); }
                catch (System.Exception e) { sb.AppendLine(job.Path + " => ERR: " + e.Message); }
            }
            return sb.ToString();
        }

        static string CaptureSync(Job job)
        {
            var router = UnityEngine.Object.FindObjectOfType<UIRouter>();
            var boot = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (router == null) return "no router";

            // 1) 预操作：把游戏切到目标状态（决定页面内容）
            if (!string.IsNullOrEmpty(job.Pre) && boot != null)
            {
                var parts = job.Pre.Split('@');
                if (parts[0] == "LevelSelect")
                    boot.ShowLevelSelect(parts[1] == "extended" ? Core.Mode.Extended : Core.Mode.Classic);
                else if (parts[0] == "Play")
                    boot.StartLevelAt(int.Parse(parts[1]));
            }

            // 2) 只激活目标页面
            var root = FindRoot(job.Page);
            if (root == null) return "no root " + job.Page;
            router.Show(MapPage(job.Page));
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null) return "no canvas on " + job.Page;
            var prevMode = canvas.renderMode;
            var prevWorldCam = canvas.worldCamera;
            var prevScale = canvas.scaleFactor;

            // 3) 临时相机 + RT
            var rt = new RenderTexture(job.W, job.H, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var camGo = new GameObject("_UIShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.depth = 300;
            cam.orthographic = true;
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity;
            cam.targetTexture = rt;

            // 让 ScreenSpaceCamera canvas 直接挂到这台相机
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            // 主动同步 Canvas 的 scaleFactor 与 RectTransform 到相机尺寸（绕过 1 帧延迟）
            var scaler = canvas.GetComponent<CanvasScaler>();
            var s = System.Math.Min(job.W / scaler.referenceResolution.x, job.H / scaler.referenceResolution.y);
            canvas.scaleFactor = (float)s;
            var crt = canvas.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(job.W / (float)s, job.H / (float)s);
            // 把相机视野调成 canvas 世界高度（ScreenSpaceCamera canvas 高度 = job.H/s）
            cam.orthographicSize = job.H / (2f * (float)s);

            var main = Camera.main;
            var prevMainTarget = main != null ? main.targetTexture : null;

            try
            {
                // 4) 强制适配器按目标分辨率选布局，并抑制 Update 回改
                var mApply = typeof(UIRouter).GetMethod("ApplyLayoutForAspect",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (mApply != null) mApply.Invoke(router, new object[] { job.W, job.H });
                var fW = typeof(UIRouter).GetField("_lastW", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var fH = typeof(UIRouter).GetField("_lastH", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fW != null) fW.SetValue(router, Screen.width);
                if (fH != null) fH.SetValue(router, Screen.height);

                // 5) 触发布局/缩放重算后手动渲染两相机（世界棋盘 + UI）
                Canvas.ForceUpdateCanvases();
                if (main != null)
                {
                    main.targetTexture = rt;
                    main.Render();
                    main.targetTexture = prevMainTarget;
                }
                cam.Render();

                // 6) 读回像素
                RenderTexture.active = rt;
                var tex = new Texture2D(job.W, job.H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, job.W, job.H), 0, 0, false);
                tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(Path.GetDirectoryName(job.Path));
                File.WriteAllBytes(job.Path, tex.EncodeToPNG());
                var size = new FileInfo(job.Path).Length;
                UnityEngine.Object.DestroyImmediate(tex);
                return "ok " + size + "B";
            }
            finally
            {
                canvas.renderMode = prevMode;
                canvas.worldCamera = prevWorldCam;
                canvas.scaleFactor = prevScale;
                UnityEngine.Object.DestroyImmediate(camGo);
                rt.Release();
                RenderTexture.active = null;
            }
        }

        static GameObject FindRoot(string name)
        {
            foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (g.name == name) return g;
            return null;
        }

        static UIRouter.Page MapPage(string name)
        {
            switch (name)
            {
                case "MainMenu": return UIRouter.Page.MainMenu;
                case "LevelSelect": return UIRouter.Page.LevelSelect;
                case "GameHUD": return UIRouter.Page.Playing;
                case "PausePanel": return UIRouter.Page.Pause;
                default: return UIRouter.Page.Win;
            }
        }
    }
}
