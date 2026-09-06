using UnityEngine;

namespace UIBuilder
{
    // 注意：UIBuilderDoc 必须独占一个 .cs 文件（类名 = 文件名），
    // 否则同文件里的第二个 MonoBehaviour 无法通过 GUID 被引用（missing script 的根因）。

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
