#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

// 菜单：Tools > Create Lenticular Cover
// 在当前打开的场景里一键搭好封面（Canvas + RawImage + 材质 + 占位图 + 脚本）。
// 效果：鼠标驱动的斜向分屏 + 全息炫卡光效（彩虹箔 / 掠光扫过 / 星点闪片 / 视差景深）。
// 具体光效参数由 LenticularReveal 组件在 OnEnable 时写入材质。
public static class LenticularCoverInstaller
{
    [MenuItem("Tools/Create Lenticular Cover")]
    static void Create()
    {
        Shader sh = Shader.Find("UI/LenticularReveal");
        if (sh == null)
        {
            Debug.LogError("找不到 Shader \"UI/LenticularReveal\"，请确认 LenticularReveal.shader 已放入项目。");
            return;
        }

        string dir = "Assets/LenticularCover";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets", "LenticularCover");

        // 占位图：两张明显不同的纯色，方便直接看到分屏（之后换成真图即可）
        Texture2D texA = MakeTex(new Color(0.10f, 0.55f, 0.65f)); // 青
        Texture2D texB = MakeTex(new Color(0.85f, 0.35f, 0.20f)); // 橙
        // 注意：CreateAsset 需要 Unity 能序列化的资产格式，Texture2D 用 .asset（不是 .png）
        AssetDatabase.CreateAsset(texA, dir + "/TexA.asset");
        AssetDatabase.CreateAsset(texB, dir + "/TexB.asset");

        Material mat = new Material(sh);
        mat.SetTexture("_TexA", texA);
        mat.SetTexture("_TexB", texB);
        AssetDatabase.CreateAsset(mat, dir + "/CoverMaterial.mat");

        // Canvas（没有就建一个全屏 Overlay）
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject c = new GameObject("CoverCanvas");
            canvas = c.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            c.AddComponent<CanvasScaler>();
            c.AddComponent<GraphicRaycaster>();
        }

        // RawImage 铺满屏幕
        GameObject go = new GameObject("LenticularCover");
        go.transform.SetParent(canvas.transform, false);
        RawImage ri = go.AddComponent<RawImage>();
        ri.material = mat;
        ri.texture = texA;
        ri.raycastTarget = false;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        go.AddComponent<LenticularReveal>();

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Debug.Log("Lenticular Cover 已生成，进入 Play Mode 移动鼠标即可查看效果。");
    }

    static Texture2D MakeTex(Color c)
    {
        Texture2D t = new Texture2D(2, 2);
        t.SetPixels(new Color[] { c, c, c, c });
        t.Apply();
        return t;
    }
}
#endif
