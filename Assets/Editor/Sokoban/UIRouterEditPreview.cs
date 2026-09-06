#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    /// <summary>
    /// 编辑态（未进 Play）预览驱动：让 Game 视口的 UI 比例与 Play 模式完全一致。
    /// 每帧检测 Game 视口尺寸，调用 UIRouter.ApplyEditPreviewScale 把各页 CanvasScaler 接管为 Constant 并驱动 scaleFactor，
    /// 使竖版页在 16:9 下背景铺满、不裁切；在 1080x1920 下按用户手调稿 1:1 呈现。
    /// </summary>
    [InitializeOnLoad]
    public static class UIRouterEditPreview
    {
        static UIRouterEditPreview()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (Application.isPlaying) return;                       // Play 模式由 UIRouter.Init 接管
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return; // 导入/编译期跳过，避免刷日志
            Sokoban.Game.UIRouter.ApplyEditPreviewScale(Screen.width, Screen.height);
        }
    }
}
#endif
