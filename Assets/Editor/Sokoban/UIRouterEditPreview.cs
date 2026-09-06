#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace Sokoban.EditorTools
{
    /// <summary>
    /// 编辑态（未进 Play）预览驱动：让 Game 视口的 UI 比例与 Play 模式完全一致。
    /// 关键：尺寸来源必须用「真正的 Game 视口分辨率」，不能用水 Screen.width/height —— 后者在编辑器里
    /// 会随当前被重绘的视口（Scene / Game）跳动，鼠标在视口间移动时 scaleFactor 跟着波动，UI 就会「飘」。
    /// 这里用反射读取 GameView.targetSize/currentGameViewSize（只取决于你设的分辨率/比例，与鼠标无关），
    /// 并加变化检测：尺寸没变就不重设，避免每帧/随鼠标重算。
    /// </summary>
    [InitializeOnLoad]
    public static class UIRouterEditPreview
    {
        static int _lastW, _lastH;
        static System.Type _gvType;
        static PropertyInfo _targetSize;

        static UIRouterEditPreview()
        {
            ResolveGameView();
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (Application.isPlaying) return;                       // Play 模式由 UIRouter.Init 接管
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            var size = GetGameViewSize();
            if (size == null) return;                               // 没有可见的 Game 视口 → 不处理
            int w = Mathf.RoundToInt(size.Value.x);
            int h = Mathf.RoundToInt(size.Value.y);
            if (w <= 0 || h <= 0) return;
            if (w == _lastW && h == _lastH) return;                  // 尺寸没变就不重设 → 鼠标移动也不会飘
            _lastW = w; _lastH = h;
            Sokoban.Game.UIRouter.ApplyEditPreviewScale(w, h);
        }

        // 取真正 Game 视口的渲染分辨率（稳定，不随鼠标在 Scene/Game 视口间移动而波动）。
        static Vector2? GetGameViewSize()
        {
            if (_gvType == null) ResolveGameView();
            if (_gvType == null) return null;

            UnityEditor.EditorWindow gv = null;
            foreach (var win in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
            {
                if (win.GetType() == _gvType) { gv = win; break; }   // 该版本 EditorWindow 无公开 isVisible，取第一个即可
            }
            if (gv == null) return null;

            if (_targetSize != null && _targetSize.PropertyType == typeof(Vector2))
            {
                var v = (Vector2)_targetSize.GetValue(gv, null);
                if (v.x > 0 && v.y > 0) return v;
            }
            // 兜底：用窗口矩形尺寸（含标题栏，足够稳定，仅缩放档位下略有偏差）
            return new Vector2(gv.position.width, gv.position.height);
        }

        static void ResolveGameView()
        {
            _gvType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (_gvType == null) return;
            // 优先取精确游戏分辨率（targetSize / currentGameViewSize，公开或内部均可）
            foreach (var name in new[] { "targetSize", "currentGameViewSize" })
            {
                var p = _gvType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(Vector2)) { _targetSize = p; return; }
            }
        }
    }
}
#endif
