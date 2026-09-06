using System;
using System.Collections.Generic;

namespace Sokoban.EditorCore
{
    using Core;

    /// <summary>
    /// 关卡静态校验：对象合法性 + 外墙闭合性（flood fill 从边界淹外部）。
    /// 纯逻辑，不依赖 Unity。
    /// </summary>
    public static class LevelValidator
    {
        public static ValidationResult Validate(LevelData lv)
        {
            var r = new ValidationResult();

            if (lv.cells == null || lv.cells.Length != lv.width * lv.height)
            {
                r.Error("棋盘数据缺失或尺寸不匹配");
                return r;
            }

            int players = 0, boxes = 0, goals = 0, enemies = 0;
            var board = new CellType[lv.width, lv.height];
            for (int y = 0; y < lv.height; y++)
            for (int x = 0; x < lv.width; x++)
            {
                var c = lv.GetCell(x, y);
                board[x, y] = c;
                switch (c)
                {
                    case CellType.Player: players++; break;
                    case CellType.Box:
                    case CellType.BoxOnGoal: boxes++; break;
                    case CellType.Goal: goals++; break;
                    case CellType.Enemy: enemies++; break;
                }
            }

            if (players == 0) r.Error("缺少玩家出生点");
            if (boxes != goals) r.Error($"箱子数({boxes}) 与 终点数({goals}) 不相等");
            if (boxes == 0) r.Warn("没有箱子 —— 关卡无挑战");

            if (lv.mode == Mode.Extended && enemies == 0)
                r.Warn("拓展模式未放置邪恶推箱人 —— 将退化为经典玩法");
            if (lv.mode == Mode.Classic && enemies > 0)
                r.Error("经典模式不能放置邪恶推箱人");

            // 外墙闭合性：从棋盘边界所有可通行格做 flood fill（4 连通）。
            // 阻挡格 = Wall + Obstacle（与 SokobanRules.IsBlocked 语义一致：障碍不可穿越，也能围出区域），
            // 被淹到的 = 「外部」。对象（玩家/箱子/终点/敌人）落在外部 → 外墙未把它们围住。
            var flooded = FloodOutside(board, lv.width, lv.height);

            bool anyInterior = false;
            for (int y = 0; y < lv.height; y++)
            for (int x = 0; x < lv.width; x++)
            {
                if (board[x, y] == CellType.Wall) continue;
                if (!flooded[x, y]) anyInterior = true;

                var c = board[x, y];
                bool isObject = c == CellType.Player || c == CellType.Box || c == CellType.BoxOnGoal
                             || c == CellType.Goal || c == CellType.Enemy;
                if (isObject && flooded[x, y])
                    r.Error($"({x},{y}) 的 {c} 在外墙之外 —— 外墙必须闭合");
            }

            if (!anyInterior)
                r.Error("外墙未围出任何内部区域 —— 请先用外墙工具圈出可玩区域");

            return r;
        }

        /// <summary>从边界非墙格 flood fill；返回 flooded[x,y] = true 表示属于「外部」。</summary>
        static bool[,] FloodOutside(CellType[,] board, int w, int h)
        {
            var flooded = new bool[w, h];
            var queue = new Queue<(int x, int y)>();

            void TryEnqueue(int x, int y)
            {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            if (flooded[x, y]) return;
            if (board[x, y] == CellType.Wall || board[x, y] == CellType.Obstacle) return;   // 障碍同墙，阻挡洪水
                flooded[x, y] = true;
                queue.Enqueue((x, y));
            }

            for (int x = 0; x < w; x++) { TryEnqueue(x, 0); TryEnqueue(x, h - 1); }
            for (int y = 0; y < h; y++) { TryEnqueue(0, y); TryEnqueue(w - 1, y); }

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                TryEnqueue(x + 1, y); TryEnqueue(x - 1, y);
                TryEnqueue(x, y + 1); TryEnqueue(x, y - 1);
            }
            return flooded;
        }
    }
}
