using System;
using UnityEngine;

namespace Sokoban.EditorCore
{
    using Core;

    /// <summary>
    /// 关卡 JSON 序列化（见 02-TDD §5.0）：只产出/消费字符串，不碰文件系统。
    /// JsonUtility 不支持锯齿数组，LevelData 内部即扁平存储，直接可用。
    /// </summary>
    public static class LevelIO
    {
        public static string ToJson(LevelData lv) => JsonUtility.ToJson(lv, prettyPrint: true);

        public static LevelData FromJson(string json)
        {
            var lv = JsonUtility.FromJson<LevelData>(json);
            if (lv == null) throw new ArgumentException("非法的关卡 JSON");
            if (lv.cells == null || lv.cells.Length != lv.width * lv.height)
            {
                // 数据损坏时给一块空白棋盘兜底，避免编辑器崩溃
                var fallback = LevelData.CreateBlank(lv?.mode ?? Mode.Classic,
                                                     lv?.width ?? 16, lv?.height ?? 12);
                fallback.id = lv?.id ?? "broken_00";
                fallback.name = lv?.name ?? "损坏关卡";
                return fallback;
            }
            return lv;
        }
    }
}
