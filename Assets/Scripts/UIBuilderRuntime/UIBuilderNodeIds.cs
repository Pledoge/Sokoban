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

    // UIBuilderDoc 已拆分到独立文件 UIBuilderDoc.cs —— 一个 .cs 文件只能可靠解析一个 MonoBehaviour
    //（GUID 引用绑定「类名 = 文件名」的主类，同文件第二个类会 missing script）。
}
