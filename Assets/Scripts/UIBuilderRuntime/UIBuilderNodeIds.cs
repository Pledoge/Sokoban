using UnityEngine;

namespace UIBuilder
{
    // ============================================================
    // 注意：这两个 MonoBehaviour 必须留在【运行时】程序集（Assets/Scripts/ 下，
    // 归 Sokoban.Runtime.asmdef）。放在 Assets/Editor/ 会导致编辑器程序集里的
    // 组件在打包/域重载后丢失引用（Unity 的 Editor 程序集不进构建）。
    // 导入器（Sokoban.Editor）引用本文件 —— asmdef 已依赖 Sokoban.Runtime。
    // ============================================================

    /// <summary>
    /// 挂到每个由导入器生成的 GameObject 上，记录网页端节点 id，
    /// 供增量重导入时按 id 匹配（从而保留程序在 Unity 中手动添加的内容）。
    /// </summary>
    public class UIBuilderNodeId : MonoBehaviour
    {
        public string id;
        public string type;     // panel | button | text | image
        public string sprite;   // none | round | pill | ring（仅 panel/button 有意义）
    }

    /// <summary>
    /// 挂在每套 UI 的根 GameObject 上，记录 docId 与 docName。
    /// 导入时据此判断「同一套 UI」：命中 docId → 就地更新该根；未命中 → 新建根。
    /// </summary>
    public class UIBuilderDoc : MonoBehaviour
    {
        public string docId;
        public string docName;
    }
}
