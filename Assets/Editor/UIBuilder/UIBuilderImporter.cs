// ============================================================
// UIBuilderImporter.cs — UI Builder 网页工具 → Unity 导入器
// ------------------------------------------------------------
// 用法：
//   1. 把本文件放到任意 Unity 项目的 Assets/Editor 目录下；
//   2. 在网页工具（ui-builder/index.html）中设计 UI 并「导出 ▾ → 📦 整包（UI + 素材）」，
//      得到 ui-package-<W>x<H>.uipack（内部是 zip：ui.json + images/*）；
//   3. Unity 菜单 UI Builder / Import from Package (.uipack)，选择该整包；
//      —— 导入器会自动把 images/* 解压到 Assets/Resources/UI（没有则创建），并标记为 Sprite；
//   4. 每套 UI 自动生成独立预制体（Assets/UIBuilder/<文件名>.prefab）并放入当前场景；
//      多套 UI 各自挂 UIBuilderDoc 组件、按 docId 区分，导入时同名则更新、异名则新建。
//   备用：菜单 UI Builder / Import from JSON 仍可用（读内嵌 dataURL 的 .json 完整包）。
//
// 增量重导入（保留手动修改）：每次导入按节点 id（导出 JSON 携带的 node.id）匹配已有的
//   UI_Builder 下的 GameObject，命中则就地更新 RectTransform 与视觉组件，不销毁对象、
//   不触碰程序手动添加的 Button.onClick / 额外组件 / 运行时引用 / 嵌套结构；仅新增 JSON 中
//   多出的节点、删除 JSON 中已移除的节点（只清理带 UIBuilderNodeId 的节点）。
//   旧版 prefab（节点无 UIBuilderNodeId）首次重导入会一次性清空子节点后重建，无重复。
//
// 一致性保证：网页端渲染采用与 Unity uGUI 完全相同的
//   RectTransform 锚点公式（anchorMin/Max、pivot、anchoredPosition、
//   sizeDelta），本导入器按同字段逐项落地，所见即所得。
//
// 资源约定：
//   整包导入后，所有美术素材都位于 Assets/Resources/UI/ 下同名文件（如 a.png），
//   导入器按 Resources.Load<Sprite>("UI/<文件名无扩展>") 解析。
//   仅网页端选了 style.sprite = round / pill / ring 时，额外尝试
//   Resources.Load<Sprite>("UI/Round")、("UI/Pill")、("UI/Ring") 作圆角九宫格，
//   找不到则回退为纯色 Simple Image 并输出警告。
//   完整包 .json 不含独立素材文件时，导入器回退用 JSON 内嵌的 dataURL 解码显示。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEditor.Events;

// ============================================================
// 常见问题（给程序 / 策划）：
//   · 中文显示：uGUI 旧版 Text 默认字体(Arial)不含中文，本导入器通过 GetUIFont()
//     自动选用中文字体——优先 Assets/Resources/UI/UIBuilderFont.ttf，否则用操作系统
//     字体(Windows 微软雅黑 / macOS 苹方)。WebGL / 移动端请把中文字体作为资源放入
//     Assets/Resources/UI/UIBuilderFont.ttf，否则中文会显示为方块。
//   · 导入策略（多套 UI）：每套 UI 在网页端是一个独立文档，导出 JSON 携带 docId + docName。
//     Unity 导入时按 docId 找根：命中 → 就地【更新同一套】；未命中 → 【新建一套】根（名为 docName），
//     根名始终与文件名一致，多套 UI 各自独立、互不干扰。同套内仍按节点 id 增量匹配：
//     命中则就地更新、未命中则新建、JSON 中已移除的则清理——【不会叠加】，程序手动添加的
//     组件 / 引用 / 嵌套会被保留。需要“每次彻底清空重来”时，把 FORCE_REBUILD 改为 true。
// ============================================================
namespace UIBuilder
{
    // ---- 与网页导出 JSON 对应的数据结构 ----
    [Serializable]
    public class RectData
    {
        public float[] anchorMin; // [x, y]
        public float[] anchorMax;
        public float[] pivot;
        public float[] pos;       // anchoredPosition
        public float[] size;      // sizeDelta
    }

    [Serializable]
    public class TextData
    {
        public string content;
        public float fontSize;
        public string color;      // "#rrggbb" 或 "rgba(r,g,b,a)"
        public string align;      // "upper-left" ...
        public bool bold;
        public float lineSpacing;
    }

    [Serializable]
    public class StyleData
    {
        public string color;      // "#rrggbb" 或 "rgba(r,g,b,a)"
        public string sprite;     // round | pill | ring | none
        public bool shadow;
        public bool raycast;
        public string bgImage;    // 美术素材替换：网页端填入的是所选文件名（即 Resources 路径，如 "bg.png"）
        public string bgImageData;// 网页端内嵌的 dataURL 预览图；Unity 优先 Resources.Load，缺失则回退解码此字段
        public string fit;        // cover | contain
    }

    [Serializable]
    public class ImageData
    {
        public string src;        // 美术资源名 / Resources 路径（网页端即所选文件名）
        public string data;       // 网页端预览用的 dataURL，Unity 导入时忽略
        public string fit;        // cover | contain
    }

    // 按钮 onClick 持久监听（回写网页 / 重导入恢复用）。
    // targetPath / argValue(object 类型) 均为相对 UI_Builder 的层级路径，重导入时按路径还原。
    [Serializable]
    public class OnClickData
    {
        public string targetPath;  // 监听目标 GameObject 相对 UI_Builder 的路径，如 "Panel/Manager"
        public string method;      // 目标上的方法名
        public string argType;      // void | string | int | float | bool | object
        public string argValue;     // object 存目标路径；其余存 ToString；void 留空
    }

    [Serializable]
    public class NodeData
    {
        public string name;
        public string type;       // panel | button | text | image
        public RectData rect;
        public TextData text;
        public StyleData style;
        public ImageData image;
        public RectData layoutOverride; // 美术包：美术移动过的节点附带的位置覆盖
        public bool locked;       // 网页端锁定：仅编辑器概念，导入时忽略
        public bool hidden;       // 网页端隐藏：导入时 SetActive(false)
        public string id;         // 网页端稳定 id：增量重导入时按此匹配已有节点
        public List<OnClickData> onClick; // 仅 button：持久监听（回写/恢复）
        public List<NodeData> children;
    }

    // 挂到每个由本导入器生成的 GameObject 上的 UIBuilderNodeId、
    // 以及挂在 UI 根上的 UIBuilderDoc，已拆到运行时程序集：
    //   Assets/Scripts/UIBuilderRuntime/UIBuilderNodeIds.cs（Sokoban.Runtime）
    // 原因：MonoBehaviour 不能定义在 Editor 程序集 —— 打包后组件引用会丢失。
    // 本文件（Sokoban.Editor）通过 asmdef 引用 Sokoban.Runtime 使用它们。

    [Serializable]
    public class ExportMeta
    {
        public float screenWidth;
        public float screenHeight;
        public string exportMode;
    }

    [Serializable]
    public class ExportRoot
    {
        public string format;
        public int version;
        public string docId;     // 该套 UI 的唯一 ID（网页端分配），用于 Unity 侧判断更新 vs 新建
        public string docName;   // 该套 UI 的文件名，Unity 中根 GameObject 以此命名
        public ExportMeta meta;
        public List<NodeData> tree;
    }

    public static class UIBuilderImporter
    {
        private const string PrefabDir = "Assets/UIBuilder";
        private const string PrefabPath = PrefabDir + "/UI.prefab";
        private static Transform s_exportRoot;   // ExportToJSON 时记录 UI_Builder 根，供 onClick 路径计算
        private static Transform s_importRoot;   // BuildFromExport 时记录 UI_Builder 根，供 onClick 路径还原

        // 导入策略：false = 增量更新（按 id 匹配，不叠加、保留手动修改）；true = 每次强制清空 UI_Builder 下所有 UI 后重建
        // 开放为 public：自动化流水线在「UI 结构大改 / 历史 id 不可信」时置 true，导完记得改回 false
        public static bool FORCE_REBUILD = false;

        // 缓存的中文字体（GetUIFont 解析一次后复用）
        private static Font s_uiFont;

        [MenuItem("UI Builder/Import from JSON")]
        public static void Import()
        {
            string path = EditorUtility.OpenFilePanel("选择 UI Builder 导出的 JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception e) { EditorUtility.DisplayDialog("UI Builder", "读取文件失败：" + e.Message, "OK"); return; }

            ExportRoot data;
            try { data = JsonUtility.FromJson<ExportRoot>(json); }
            catch (Exception e) { EditorUtility.DisplayDialog("UI Builder", "JSON 解析失败：" + e.Message, "OK"); return; }

            if (data == null || (data.format != "ui-builder" && data.format != "ui-builder-doc"))
            {
                EditorUtility.DisplayDialog("UI Builder", "不是 ui-builder 格式（缺少 format 字段）", "OK");
                return;
            }

            BuildFromExport(data);
        }

        /// <summary>
        /// 非交互导入入口：给定 JSON 路径直接导入，不弹文件面板。
        /// 供自动化流水线 / CI 使用（例如批量导入 UITools/UIBuilder/exports/ 下的各套 UI）。
        /// 返回 false 表示格式非法或解析失败。
        /// </summary>
        public static bool ImportFromPath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { error = "文件不存在: " + path; return false; }

            ExportRoot data;
            try { data = JsonUtility.FromJson<ExportRoot>(File.ReadAllText(path)); }
            catch (Exception e) { error = "JSON 解析失败: " + e.Message; return false; }

            if (data == null || (data.format != "ui-builder" && data.format != "ui-builder-doc"))
            {
                error = "不是 ui-builder 格式（缺少 format 字段）";
                return false;
            }

            BuildFromExport(data);
            return true;
        }

        // 整包导入：解压 images/* 到 Assets/Resources/UI（自动建目录），再从 ui.json 构建
        [MenuItem("UI Builder/Import from Package (.uipack)")]
        public static void ImportPackage()
        {
            string path = EditorUtility.OpenFilePanel("选择 UI Builder 整包 (.uipack/.zip)", "", "uipack,zip");
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path))
            {
                EditorUtility.DisplayDialog("UI Builder", "文件不存在：" + path, "OK");
                return;
            }

            ExportRoot data = null;
            const string uiResDir = "Assets/Resources/UI";

            try
            {
                using (var archive = ZipFile.OpenRead(path))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                        AssetDatabase.CreateFolder("Assets", "Resources");
                    if (!AssetDatabase.IsValidFolder(uiResDir))
                        AssetDatabase.CreateFolder("Assets/Resources", "UI");

                    foreach (var entry in archive.Entries)
                    {
                        bool isImage = entry.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase)
                                       && entry.FullName[entry.FullName.Length - 1] != '/';
                        bool isJson = string.Equals(entry.FullName, "ui.json", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(entry.Name, "ui.json", StringComparison.OrdinalIgnoreCase);
                        if (isImage)
                        {
                            string fname = Path.GetFileName(entry.FullName);
                            if (string.IsNullOrEmpty(fname)) continue;
                            string dest = uiResDir + "/" + fname;
                            using (var s = entry.Open())
                            using (var fs = File.Create(dest))
                            {
                                s.CopyTo(fs);
                            }
                        }
                        else if (isJson)
                        {
                            using (var s = entry.Open())
                            using (var reader = new StreamReader(s))
                            {
                                data = JsonUtility.FromJson<ExportRoot>(reader.ReadToEnd());
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("UI Builder", "读取整包失败：" + e.Message, "OK");
                return;
            }

            if (data == null || (data.format != "ui-builder" && data.format != "ui-builder-doc"))
            {
                EditorUtility.DisplayDialog("UI Builder", "整包内未找到有效的 ui.json（ui-builder 格式）", "OK");
                return;
            }

            // 让 Unity 识别新图片，并标记为 Sprite（否则 Resources.Load<Sprite> 取不到）
            AssetDatabase.Refresh();
            if (AssetDatabase.IsValidFolder(uiResDir))
            {
                foreach (var f in Directory.GetFiles(uiResDir))
                {
                    if (f.EndsWith(".meta")) continue;
                    var importer = AssetImporter.GetAtPath(f.Replace("\\", "/")) as TextureImporter;
                    if (importer != null)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.SaveAndReimport();
                    }
                }
            }
            AssetDatabase.Refresh();

            BuildFromExport(data);
            EditorUtility.DisplayDialog("UI Builder",
                $"整包导入完成：{data.tree.Count} 个顶层节点。\n素材已自动放入 Assets/Resources/UI（{uiResDir}）。", "OK");
        }

        // 根据导出数据构建 UI（JSON / 整包共用）。增量模式：复用已有 UI_Builder，
        // 按节点 id 匹配就地更新，保留程序手动添加的组件/引用/嵌套；仅增删变化的节点。
        // 根据导出数据构建 UI（JSON / 整包共用）。
        // 多套 UI：每套一个独立根 GameObject（名为 docName），按 docId 决定【更新同一套】还是【新建一套】；
        // 同套内仍为增量模式：复用该根下已有节点按 id 匹配就地更新，保留程序手动添加的组件/引用/嵌套，仅增删变化的节点。
        private static void BuildFromExport(ExportRoot data)
        {
            string docId = data.docId;
            string docName = string.IsNullOrEmpty(data.docName) ? "UI_Builder" : data.docName;
            string rootName = SanitizeForGO(docName);

            GameObject root = null;
            bool isNew = false;
            // 1) 按 docId 找已有根（增量更新同一套 UI）
            if (!string.IsNullOrEmpty(docId)) root = FindDocRootByDocId(docId);
            // 2) 退化：按名字找（兼容旧版 docId 为空的整包 / JSON）
            if (root == null) root = GameObject.Find(rootName);
            // 3) 新建一套
            if (root == null)
            {
                root = new GameObject(rootName);
                root.transform.SetParent(null, false);
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
                scaler.matchWidthOrHeight = 0.5f;
                root.AddComponent<GraphicRaycaster>();
                isNew = true;
            }
            // 记录 docId / docName（新建与更新都刷新，保证根名与文件名一致）
            var docComp = GetOrAdd<UIBuilderDoc>(root);
            docComp.docId = docId; docComp.docName = docName;
            if (root.name != rootName) root.name = rootName;   // 文件名变了也同步重命名根

            float sw = data.meta != null ? data.meta.screenWidth : 720f;
            float sh = data.meta != null ? data.meta.screenHeight : 1280f;
            var scaler2 = root.GetComponent<CanvasScaler>();
            if (scaler2 != null) scaler2.referenceResolution = new Vector2(sw, sh);
            s_importRoot = root.transform;

            // 收集已有节点（按 id），用于增量匹配（仅本套 UI 根下）
            var existing = new Dictionary<string, GameObject>();
            if (isNew || FORCE_REBUILD)
            {
                // 新建套 或 强制重建：销毁该根下所有子节点，彻底从空白开始（不含根自身与 Canvas/Scaler/UIBuilderDoc）
                var all = new List<GameObject>();
                for (int i = 0; i < root.transform.childCount; i++) all.Add(root.transform.GetChild(i).gameObject);
                foreach (var c in all) UnityEngine.Object.DestroyImmediate(c);
                existing.Clear();
                if (FORCE_REBUILD) Debug.Log($"[UIBuilder] FORCE_REBUILD=true：已清空 {rootName} 下所有旧 UI，准备重建。");
            }
            CollectIds(root.transform, existing);

            // 旧版根（节点无 UIBuilderNodeId）一次性迁移：清空子节点后重建，避免重复
            if (!isNew && existing.Count == 0)
            {
                var legacyChildren = new List<GameObject>();
                for (int i = 0; i < root.transform.childCount; i++) legacyChildren.Add(root.transform.GetChild(i).gameObject);
                foreach (var c in legacyChildren) UnityEngine.Object.DestroyImmediate(c);
            }

            var seen = new HashSet<string>();
            var parentRect = new Rect(0, 0, sw, sh);
            foreach (var node in data.tree)
            {
                SyncNode(node, root.transform, parentRect, existing, seen);
            }

            // 删除已从 JSON 中移除的节点（仅清理本导入器管理的节点）
            var toRemove = new List<GameObject>();
            foreach (var kv in existing) if (!seen.Contains(kv.Key)) toRemove.Add(kv.Value);
            foreach (var go in toRemove) UnityEngine.Object.DestroyImmediate(go);

            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets", "UIBuilder");
            string prefabPath = PrefabDir + "/" + rootName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            Debug.Log($"[UIBuilder] {(isNew ? "新建" : "更新")}完成：{data.tree.Count} 个顶层节点 -> {prefabPath}（设计分辨率 {sw}x{sh}，按 docId='{docId}' {(isNew ? "新建一套" : "更新同一套")}，保留手动修改）");
        }

        // 按 docId 查找已导入的某套 UI 根（含未激活对象）
        private static GameObject FindDocRootByDocId(string docId)
        {
            if (string.IsNullOrEmpty(docId)) return null;
            var roots = GameObject.FindObjectsOfType<UIBuilderDoc>(true);
            foreach (var r in roots) if (r.docId == docId) return r.gameObject;
            return null;
        }

        // 把文档名转成可用的 GameObject / prefab 名（保留中文，仅剔除路径分隔符）
        private static string SanitizeForGO(string s)
        {
            if (string.IsNullOrEmpty(s)) return "UI_Builder";
            s = s.Replace("\\", "_").Replace("/", "_").Trim();
            return string.IsNullOrEmpty(s) ? "UI_Builder" : s;
        }

        // 按 id 收集本导入器生成的所有节点
        private static void CollectIds(Transform t, Dictionary<string, GameObject> map)
        {
            var idc = t.GetComponent<UIBuilderNodeId>();
            if (idc != null && !string.IsNullOrEmpty(idc.id)) map[idc.id] = t.gameObject;
            for (int i = 0; i < t.childCount; i++) CollectIds(t.GetChild(i), map);
        }

        // 增量同步：匹配到已有节点则就地更新，否则新建
        private static void SyncNode(NodeData data, Transform parent, Rect parentRect, Dictionary<string, GameObject> existing, HashSet<string> seen)
        {
            GameObject go;
            if (!string.IsNullOrEmpty(data.id) && existing.TryGetValue(data.id, out var found))
            {
                go = found;
                seen.Add(data.id);
            }
            else
            {
                go = new GameObject(data.name, typeof(RectTransform));
                go.transform.SetParent(parent, false);   // 新建节点挂到 JSON 指定的父级
                if (!string.IsNullOrEmpty(data.id)) go.AddComponent<UIBuilderNodeId>().id = data.id;
            }
            ApplyNode(go, data, parentRect);
            if (data.children != null)
            {
                var childRect = RectTransformToRect(go.GetComponent<RectTransform>());
                foreach (var c in data.children) SyncNode(c, go.transform, childRect, existing, seen);
            }
        }

        // 就地更新节点的变换与组件，不销毁 GameObject、不触碰程序添加的组件/引用/嵌套
        private static void ApplyNode(GameObject go, NodeData data, Rect parentRect)
        {
            ApplyRect(go, data);
            go.SetActive(!data.hidden);
            switch (data.type)
            {
                case "text":   BuildText(go, data); break;
                case "image":  BuildImage(go, data); break;
                case "button": BuildButton(go, data); break;
                default:       BuildPanel(go, data); break;
            }
            // 记录类型与圆角形状，供 Unity→网页回写导出时反向序列化出与网页一致的字段
            var _idc = go.GetComponent<UIBuilderNodeId>();
            if (_idc != null)
            {
                _idc.type = data.type;
                _idc.sprite = (data.type == "panel" || data.type == "button")
                    ? (data.style != null && !string.IsNullOrEmpty(data.style.sprite) ? data.style.sprite : "none")
                    : "none";
            }
        }

        // 仅设置 RectTransform（rect 优先，其次美术包 layoutOverride）
        private static void ApplyRect(GameObject go, NodeData data)
        {
            var rt = go.GetComponent<RectTransform>();
            RectData r = data.rect != null ? data.rect : (data.layoutOverride != null ? data.layoutOverride : null);
            if (r != null)
            {
                rt.anchorMin = V2(r.anchorMin, new Vector2(0.5f, 0.5f));
                rt.anchorMax = V2(r.anchorMax, rt.anchorMin);
                rt.pivot = V2(r.pivot, new Vector2(0.5f, 0.5f));
                rt.anchoredPosition = V2(r.pos, Vector2.zero);
                rt.sizeDelta = V2(r.size, new Vector2(160, 40));
            }
            else
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(160, 40);
            }
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        // 中文 / Unicode 字体解析：优先项目内置资源，其次操作系统动态字体，最后回退默认（中文可能方块）
        private static Font GetUIFont()
        {
            if (s_uiFont != null) return s_uiFont;
            // 1) 项目内置中文字体：把中文字体放到 Assets/Resources/UI/UIBuilderFont.ttf（文件名可改）
            var res = Resources.Load<Font>("UI/UIBuilderFont");
            if (res != null) { s_uiFont = res; return s_uiFont; }
            // 2) 操作系统动态字体（Windows / macOS 编辑器与独立播放机可用；WebGL / 部分移动端不可用）
            foreach (var fam in new[] { "Microsoft YaHei", "Microsoft YaHei UI", "PingFang SC", "Heiti SC", "Source Han Sans SC", "Noto Sans CJK SC" })
            {
                try { var f = Font.CreateDynamicFontFromOSFont(fam, 16); if (f != null) { s_uiFont = f; return s_uiFont; } } catch { }
            }
            Debug.LogWarning("[UIBuilder] 未找到中文字体，中文可能显示为方块。请把中文字体放到 Assets/Resources/UI/UIBuilderFont.ttf（或改 GetUIFont 指定字体）。");
            return null; // 回退：可能方块
        }

        private static void BuildPanel(GameObject go, NodeData data)
        {
            var img = GetOrAdd<Image>(go);
            var col = ParseColor(data.style != null ? data.style.color : null, new Color(0.23f, 0.24f, 0.28f, 1f));
            bool useBgImage = data.style != null && !string.IsNullOrEmpty(data.style.bgImage);
            if (useBgImage)
            {
                // 美术素材替换：优先加载 bgImage 指定的资源（整包导入已在 Resources/UI 下）
                var mat = LoadSpriteByName(data.style.bgImage);
                if (mat != null)
                {
                    img.sprite = mat;
                    img.type = Image.Type.Simple;
                    img.preserveAspect = data.style.fit == "contain";
                    img.color = Color.white;
                    goto styled;
                }
                var fromData = LoadSpriteFromDataURL(data.style.bgImageData);
                if (fromData != null)
                {
                    img.sprite = fromData;
                    img.type = Image.Type.Simple;
                    img.preserveAspect = data.style.fit == "contain";
                    img.color = Color.white;
                    goto styled;
                }
                Debug.LogWarning($"[UIBuilder] 未找到美术素材 '{data.style.bgImage}'（Resources.Load<Sprite>）。已回退纯色。");
            }
            var sprite = ResolveSprite(data.style);
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.color = col;
            }
            else
            {
                img.sprite = null;
                img.type = Image.Type.Simple;
                img.color = col;
            }
            styled:
            img.raycastTarget = data.style != null && data.style.raycast;
            if (data.style != null && data.style.shadow)
            {
                var sd = GetOrAdd<Shadow>(go);
                sd.effectColor = new Color(0, 0, 0, 0.28f);
                sd.effectDistance = new Vector2(0, -3);
            }
        }

        private static void BuildButton(GameObject go, NodeData data)
        {
            BuildPanel(go, data);
            bool isNew = go.GetComponent<Button>() == null;   // 仅新建按钮时按 JSON 恢复 onClick；增量复用则保留程序现有监听
            var btn = GetOrAdd<Button>(go);
            var img = go.GetComponent<Image>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.06f, 1.06f, 1.06f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f);
            btn.colors = colors;

            // Label：若 JSON 已带文本子节点则交给子节点构建；否则补一个（更新时复用已有 Label，避免重复）
            bool hasTextChild = data.children != null && data.children.Exists(c => c.type == "text");
            if (!hasTextChild && data.text != null)
            {
                var label = go.transform.Find("Label");
                if (label == null)
                {
                    label = new GameObject("Label", typeof(RectTransform)).transform;
                    label.SetParent(go.transform, false);
                    var rt = label.GetComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                }
                var t = GetOrAdd<UnityEngine.UI.Text>(label.gameObject);
                t.font = GetUIFont();   // 显式中文字体，避免中文显示为方块
                t.fontSize = Mathf.RoundToInt(data.text.fontSize);
                t.color = ParseColor(data.text.color, Color.white);
                t.alignment = AlignMap(data.text.align);
                t.fontStyle = data.text.bold ? FontStyle.Bold : FontStyle.Normal;
                t.supportRichText = true;
                t.raycastTarget = false;
                t.text = data.text.content;
                if (data.text.lineSpacing > 0)
                    t.lineSpacing = data.text.lineSpacing;   // 旧版 Text.lineSpacing 即倍率（1=正常）
            }

            // 恢复 onClick（仅新建按钮：增量复用按钮不覆盖程序手动接线）
            if (isNew && data.onClick != null) ApplyOnClick(btn, data.onClick, s_importRoot);
        }

        private static void BuildText(GameObject go, NodeData data)
        {
            var t = GetOrAdd<UnityEngine.UI.Text>(go);
            t.font = GetUIFont();   // 显式中文字体，避免中文显示为方块
            t.fontSize = data.text != null ? Mathf.RoundToInt(data.text.fontSize) : 18;
            t.color = ParseColor(data.text != null ? data.text.color : null, Color.white);
            t.alignment = AlignMap(data.text != null ? data.text.align : null);
            t.fontStyle = data.text != null && data.text.bold ? FontStyle.Bold : FontStyle.Normal;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = data.text != null ? data.text.content : "";
            if (data.text != null && data.text.lineSpacing > 0)
                t.lineSpacing = data.text.lineSpacing;   // 旧版 Text.lineSpacing 即倍率（1=正常）
        }

        private static void BuildImage(GameObject go, NodeData data)
        {
            var img = GetOrAdd<Image>(go);
            string src = data.image != null ? data.image.src : "";
            if (!string.IsNullOrEmpty(src))
            {
                var sprite = LoadSpriteByName(src);
                if (sprite != null)
                {
                    img.sprite = sprite;
                    img.type = Image.Type.Simple;
                    img.preserveAspect = true;
                    img.color = Color.white;
                    return;
                }
                var fromData = LoadSpriteFromDataURL(data.image != null ? data.image.data : null);
                if (fromData != null)
                {
                    img.sprite = fromData;
                    img.type = Image.Type.Simple;
                    img.preserveAspect = true;
                    img.color = Color.white;
                    return;
                }
                Debug.LogWarning($"[UIBuilder] 未找到美术资源 '{src}'（尝试 Resources.Load<Sprite>('{src}')）。已回退为占位块。");
            }
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.color = new Color(0.4f, 0.4f, 0.5f, 0.6f);
        }

        // ---- 小工具 ----
        private static Vector2 V2(float[] arr, Vector2 def)
        {
            if (arr == null || arr.Length < 2) return def;
            return new Vector2(arr[0], arr[1]);
        }

        private static Rect RectTransformToRect(RectTransform rt)
        {
            // 依据父矩形 + 锚点反算当前节点矩形（与网页 computeRect 同公式）
            var parent = rt.parent as RectTransform;
            var pRect = parent != null
                ? new Rect(
                    parent.anchorMin.x * parent.rect.width  - parent.rect.width  * parent.pivot.x,
                    parent.anchorMin.y * parent.rect.height - parent.rect.height * parent.pivot.y,
                    parent.rect.width, parent.rect.height)
                : new Rect(0, 0, rt.anchorMax.x - rt.anchorMin.x, rt.anchorMax.y - rt.anchorMin.y);
            return pRect;
        }

        // 从 JSON 内嵌的 dataURL 解码素材（美术在网页导入的图会内嵌 dataURL，Unity 无需手动放 Resources 也能显示）
        private static Sprite LoadSpriteFromDataURL(string dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl) || !dataUrl.StartsWith("data:image")) return null;
            int comma = dataUrl.IndexOf(',');
            if (comma < 0) return null;
            try
            {
                byte[] bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(bytes)) { UnityEngine.Object.DestroyImmediate(tex); return null; }
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UIBuilder] dataURL 素材解码失败：{e.Message}");
                return null;
            }
        }

        // 美术素材统一解析：整包导入时已把素材放到 Assets/Resources/UI，故优先按 "UI/<文件名无扩展>" 取
        private static Sprite LoadSpriteByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string noExt = Path.GetFileNameWithoutExtension(name);
            return Resources.Load<Sprite>("UI/" + noExt);
        }

        private static Sprite ResolveSprite(StyleData style)
        {
            if (style == null || string.IsNullOrEmpty(style.sprite) || style.sprite == "none") return null;
            string name = char.ToUpperInvariant(style.sprite[0]) + style.sprite.Substring(1);
            var sprite = Resources.Load<Sprite>("UI/" + name);
            if (sprite == null)
                Debug.LogWarning($"[UIBuilder] 未找到九宫格资源 Resources/UI/{name}.png，已回退纯色（圆角需在 Unity 中补齐该资源）");
            return sprite;
        }

        // 网页对齐字符串 -> 旧版 uGUI TextAnchor（UpperLeft 等命名与旧版一致）
        private static TextAnchor AlignMap(string align)
        {
            switch (align)
            {
                case "upper-left":    return TextAnchor.UpperLeft;
                case "upper-center":  return TextAnchor.UpperCenter;
                case "upper-right":   return TextAnchor.UpperRight;
                case "middle-left":   return TextAnchor.MiddleLeft;
                case "middle-center": return TextAnchor.MiddleCenter;
                case "middle-right":  return TextAnchor.MiddleRight;
                case "lower-left":    return TextAnchor.LowerLeft;
                case "lower-center":  return TextAnchor.LowerCenter;
                case "lower-right":   return TextAnchor.LowerRight;
                default:              return TextAnchor.MiddleCenter;
            }
        }

        private static Color ParseColor(string s, Color def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            s = s.Trim();
            // rgba(r,g,b,a)
            if (s.StartsWith("rgba"))
            {
                var inner = s.Substring(5, s.Length - 6);
                var p = inner.Split(',');
                if (p.Length == 4)
                {
                    return new Color(
                        float.Parse(p[0]) / 255f,
                        float.Parse(p[1]) / 255f,
                        float.Parse(p[2]) / 255f,
                        float.Parse(p[3]));
                }
            }
            // #rrggbb
            if (s.StartsWith("#") && s.Length >= 7)
            {
                var hex = s.Substring(1);
                return new Color(
                    Convert.ToInt32(hex.Substring(0, 2), 16) / 255f,
                    Convert.ToInt32(hex.Substring(2, 2), 16) / 255f,
                    Convert.ToInt32(hex.Substring(4, 2), 16) / 255f,
                    1f);
            }
            return def;
        }

        // ============================================================
        // Unity → 网页回写：把当前 UI_Builder 子树反序列化为 ui-builder v2 JSON，
        // 结构与网页导出完全一致（同 schema、同字段），网页端「导入」即可同步 Unity 的手动修改。
        // 节点按 UIBuilderNodeId.id 保留稳定 id，因此回写后再导入网页仍能被 Unity 增量重导入识别。
        // ============================================================
        [MenuItem("UI Builder/Export to JSON (回写网页)")]
        public static void ExportToJSON()
        {
            var root = GameObject.Find("UI_Builder");
            var docComp = root != null ? root.GetComponent<UIBuilderDoc>() : null;
            if (root == null)
            {
                var roots = GameObject.FindObjectsOfType<UIBuilderDoc>(true);
                if (roots.Length > 0) { root = roots[0].gameObject; docComp = roots[0]; }
            }
            if (root == null)
            {
                EditorUtility.DisplayDialog("UI Builder", "场景中未找到 UI_Builder，请先导入一次完整包 / 整包。", "OK");
                return;
            }
            var scaler = root.GetComponent<CanvasScaler>();
            float sw = scaler != null ? scaler.referenceResolution.x : 720f;
            float sh = scaler != null ? scaler.referenceResolution.y : 1280f;
            s_exportRoot = root.transform;

            var used = new Dictionary<string, int>();
            var tree = new List<NodeData>();
            for (int i = 0; i < root.transform.childCount; i++)
            {
                tree.Add(ExportNode(root.transform.GetChild(i).gameObject, used));
            }

            var data = new ExportRoot
            {
                format = "ui-builder",
                version = 2,
                docId = docComp != null ? docComp.docId : "",
                docName = docComp != null ? docComp.docName : root.name,
                meta = new ExportMeta { screenWidth = sw, screenHeight = sh, exportMode = "unity-writeback" },
                tree = tree,
            };
            string json = JsonUtility.ToJson(data, true);

            // 递归创建目录（File.WriteAllText 不会自动建目录；AssetDatabase.CreateFolder 不建父级）
            string dir = System.IO.Path.Combine(Application.dataPath, "UIBuilderExports");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"ui-export-{sw:0}x{sh:0}.json");
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("UI Builder",
                $"已回写网页 JSON：\n{path}\n\n在网页工具点「导入」并选择此文件，即可把 Unity 里的手动修改同步回网页。",
                "OK");
            EditorUtility.RevealInFinder(path);
        }

        // 递归导出单个 GameObject（跳过无 UIBuilderNodeId 的子节点，如按钮自动生成的 Label）
        private static NodeData ExportNode(GameObject go, Dictionary<string, int> usedIds)
        {
            var idc = go.GetComponent<UIBuilderNodeId>();
            var nd = new NodeData
            {
                name = go.name,
                rect = ReadRect(go.GetComponent<RectTransform>()),
                hidden = !go.activeSelf,
                children = new List<NodeData>(),
            };
            string t = (idc != null && !string.IsNullOrEmpty(idc.type)) ? idc.type : GuessType(go);
            nd.type = t;
            nd.id = (idc != null && !string.IsNullOrEmpty(idc.id)) ? idc.id : ("u_" + SanitizeName(go.name) + "_" + usedIds.Count);
            usedIds[nd.id] = 1;

            string storedSprite = (idc != null && !string.IsNullOrEmpty(idc.sprite)) ? idc.sprite : "none";
            if (t == "text")
            {
                nd.text = ReadText(go.GetComponent<UnityEngine.UI.Text>());
            }
            else if (t == "image")
            {
                nd.image = ReadImage(go);
                nd.style = ReadStyle(go, storedSprite);
            }
            else // panel / button
            {
                nd.style = ReadStyle(go, storedSprite);
                if (t == "button")
                {
                    var label = go.transform.Find("Label");
                    nd.text = ReadText(label != null ? label.GetComponent<UnityEngine.UI.Text>() : null);
                    var btn = go.GetComponent<Button>();
                    if (btn != null) nd.onClick = ReadOnClick(btn, s_exportRoot);   // 回写程序手动接线的 onClick
                }
            }

            for (int i = 0; i < go.transform.childCount; i++)
            {
                var c = go.transform.GetChild(i).gameObject;
                if (c.GetComponent<UIBuilderNodeId>() != null)
                    nd.children.Add(ExportNode(c, usedIds));
            }
            return nd;
        }

        private static string GuessType(GameObject go)
        {
            if (go.GetComponent<Button>() != null) return "button";
            if (go.GetComponent<UnityEngine.UI.Text>() != null) return "text";
            return "panel";
        }

        private static string SanitizeName(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in s)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c); else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            return sb.Length == 0 ? "n" : sb.ToString();
        }

        private static RectData ReadRect(RectTransform rt)
        {
            if (rt == null) return new RectData();
            return new RectData
            {
                anchorMin = new[] { rt.anchorMin.x, rt.anchorMin.y },
                anchorMax = new[] { rt.anchorMax.x, rt.anchorMax.y },
                pivot = new[] { rt.pivot.x, rt.pivot.y },
                pos = new[] { rt.anchoredPosition.x, rt.anchoredPosition.y },
                size = new[] { rt.sizeDelta.x, rt.sizeDelta.y },
            };
        }

        private static TextData ReadText(UnityEngine.UI.Text t)
        {
            if (t == null) return null;
            return new TextData
            {
                content = t.text,
                fontSize = Mathf.RoundToInt(t.fontSize),
                color = ColorToWeb(t.color),
                align = ReverseAlign(t.alignment),
                bold = (t.fontStyle & FontStyle.Bold) != 0,
                lineSpacing = (float)Math.Round(t.lineSpacing, 3),   // 旧版 Text.lineSpacing 即倍率
            };
        }

        private static StyleData ReadStyle(GameObject go, string storedSprite)
        {
            var img = go.GetComponent<Image>();
            var s = new StyleData
            {
                sprite = storedSprite ?? "none",
                fit = "contain",
            };
            if (img != null)
            {
                s.color = ColorToWeb(img.color);
                s.raycast = img.raycastTarget;
                // 面板/按钮背景被美术素材替换时，内嵌 dataURL 回写，网页导入即可直接显示
                if (img.sprite != null)
                {
                    var bytes = LoadImageBytes(img.sprite.name) ?? EncodeSprite(img.sprite);
                    if (bytes != null)
                    {
                        s.bgImage = img.sprite.name + ".png";
                        s.bgImageData = "data:image/png;base64," + Convert.ToBase64String(bytes);
                    }
                }
            }
            s.shadow = go.GetComponent<Shadow>() != null;
            return s;
        }

        private static ImageData ReadImage(GameObject go)
        {
            var img = go.GetComponent<Image>();
            var d = new ImageData { fit = "contain" };
            if (img != null && img.sprite != null)
            {
                var bytes = LoadImageBytes(img.sprite.name) ?? EncodeSprite(img.sprite);
                if (bytes != null)
                {
                    d.src = img.sprite.name + ".png";
                    d.data = "data:image/png;base64," + Convert.ToBase64String(bytes);
                }
            }
            return d;
        }

        private static byte[] LoadImageBytes(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;
            string baseName = Path.GetFileNameWithoutExtension(spriteName);
            const string dir = "Assets/Resources/UI";
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            foreach (var ext in new[] { "png", "jpg", "jpeg", "webp" })
            {
                string p = $"{dir}/{baseName}.{ext}";
                if (File.Exists(p)) return File.ReadAllBytes(p);
            }
            return null;
        }

        // 纹理不可读时，经临时 RenderTexture 拷贝出可读副本再编码 PNG
        private static byte[] EncodeSprite(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;
            var tex = sprite.texture;
            if (tex.isReadable)
            {
                try { return tex.EncodeToPNG(); } catch { }
            }
            int w = tex.width, h = tex.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return read.EncodeToPNG();
        }

        private static string ColorToWeb(Color c)
        {
            string hex = ColorUtility.ToHtmlStringRGBA(c); // RRGGBBAA
            if (hex.EndsWith("FF", System.StringComparison.OrdinalIgnoreCase)) return "#" + hex.Substring(0, 6);
            return "#" + hex;
        }

        // ---- 按钮 onClick 回写 / 恢复 ----
        // 读取按钮持久监听（target 路径 + 方法 + 参数）。参数值经 SerializedProperty 读取，单次失败仅跳过该监听。
        private static List<OnClickData> ReadOnClick(Button btn, Transform root)
        {
            var list = new List<OnClickData>();
            try
            {
                var so = new SerializedObject(btn);
                var prop = so.FindProperty("m_OnClick");
                if (prop == null) return list;
                var calls = prop.FindPropertyRelative("m_PersistentCalls.m_Calls");
                if (calls == null) return list;
                for (int i = 0; i < calls.arraySize; i++)
                {
                    var call = calls.GetArrayElementAtIndex(i);
                    var targetObj = call.FindPropertyRelative("m_Target").objectReferenceValue;
                    var method = call.FindPropertyRelative("m_MethodName").stringValue;
                    if (targetObj == null || string.IsNullOrEmpty(method)) continue;
                    var go = GameObjectOf(targetObj);
                    if (go == null) continue;
                    int mode = call.FindPropertyRelative("m_Mode").intValue;
                    var args = call.FindPropertyRelative("m_Arguments");
                    var d = new OnClickData { targetPath = PathUnderRoot(go.transform, root), method = method, argType = "void", argValue = "" };
                    if (mode == 2 && args != null)      // Object
                    {
                        var o = args.FindPropertyRelative("m_ObjectArgument").objectReferenceValue;
                        d.argType = "object";
                        d.argValue = o != null ? PathUnderRoot(GameObjectOf(o).transform, root) : "";
                    }
                    else if (mode == 3 && args != null) { d.argType = "int";    d.argValue = args.FindPropertyRelative("m_IntArgument").intValue.ToString(); }
                    else if (mode == 4 && args != null) { d.argType = "float";  d.argValue = args.FindPropertyRelative("m_FloatArgument").floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture); }
                    else if (mode == 5 && args != null) { d.argType = "string"; d.argValue = args.FindPropertyRelative("m_StringArgument").stringValue; }
                    else if (mode == 6 && args != null) { d.argType = "bool";   d.argValue = args.FindPropertyRelative("m_BoolArgument").boolValue ? "true" : "false"; }
                    list.Add(d);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[UIBuilder] 读取按钮 onClick 失败（已跳过）：" + e.Message); }
            return list;
        }

        // 按 JSON 中的 onClick 描述，用 UnityEventTools 重新接线（仅在新建按钮时调用）
        private static void ApplyOnClick(Button btn, List<OnClickData> list, Transform root)
        {
            if (list == null) return;
            foreach (var d in list)
            {
                var target = ResolveByPath(d.targetPath, root);
                if (target == null) { Debug.LogWarning($"[UIBuilder] 恢复 onClick 失败：找不到目标 '{d.targetPath}'（可能已被重命名/移动）"); continue; }
                try
                {
                    switch (d.argType)
                    {
                        case "string":
                            UnityEventTools.AddStringPersistentListener(btn.onClick,
                                (UnityEngine.Events.UnityAction<string>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<string>), target, d.method),
                                d.argValue ?? "");
                            break;
                        case "int":
                            int.TryParse(d.argValue, out var iv);
                            UnityEventTools.AddIntPersistentListener(btn.onClick,
                                (UnityEngine.Events.UnityAction<int>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<int>), target, d.method),
                                iv);
                            break;
                        case "float":
                            float.TryParse(d.argValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fv);
                            UnityEventTools.AddFloatPersistentListener(btn.onClick,
                                (UnityEngine.Events.UnityAction<float>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<float>), target, d.method),
                                fv);
                            break;
                        case "bool":
                            bool.TryParse(d.argValue, out var bv);
                            UnityEventTools.AddBoolPersistentListener(btn.onClick,
                                (UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>), target, d.method),
                                bv);
                            break;
                        case "object":
                            var o = ResolveByPath(d.argValue, root);
                            if (o != null)
                                UnityEventTools.AddObjectPersistentListener<UnityEngine.Object>(btn.onClick,
                                    (UnityEngine.Events.UnityAction<UnityEngine.Object>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<UnityEngine.Object>), target, d.method),
                                    o);
                            break;
                        default:
                            UnityEventTools.AddVoidPersistentListener(btn.onClick,
                                (UnityEngine.Events.UnityAction)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), target, d.method));
                            break;
                    }
                }
                catch (System.Exception e) { Debug.LogWarning($"[UIBuilder] 恢复 onClick 监听 '{d.method}' 失败：{e.Message}"); }
            }
        }

        private static GameObject GameObjectOf(UnityEngine.Object o)
        {
            if (o == null) return null;
            if (o is GameObject g) return g;
            if (o is Component c) return c.gameObject;
            return null;
        }

        private static string PathUnderRoot(Transform t, Transform root)
        {
            var parts = new List<string>();
            var cur = t;
            while (cur != null && cur != root) { parts.Insert(0, cur.name); cur = cur.parent; }
            return string.Join("/", parts);
        }

        private static GameObject ResolveByPath(string path, Transform root)
        {
            if (string.IsNullOrEmpty(path) || root == null) return null;
            var cur = root;
            foreach (var seg in path.Split('/'))
            {
                if (cur == null) return null;
                cur = cur.Find(seg);
            }
            return cur != null ? cur.gameObject : null;
        }

        // TextAnchor -> 网页对齐字符串
        private static string ReverseAlign(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft:   return "upper-left";
                case TextAnchor.UpperCenter: return "upper-center";
                case TextAnchor.UpperRight:  return "upper-right";
                case TextAnchor.MiddleLeft:  return "middle-left";
                case TextAnchor.MiddleCenter:return "middle-center";
                case TextAnchor.MiddleRight: return "middle-right";
                case TextAnchor.LowerLeft:   return "lower-left";
                case TextAnchor.LowerCenter: return "lower-center";
                case TextAnchor.LowerRight:  return "lower-right";
                default:                     return "middle-center";
            }
        }
    }
}
