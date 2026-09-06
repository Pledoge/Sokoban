namespace Sokoban.Core
{
    public enum StepResult : byte
    {
        NoOp = 0,   // 玩家与敌人都没动，世界无变化 → 不入撤销栈、不计步
        Ok = 1,     // 世界发生了变化
    }

    /// <summary>一步的细分结果，供动画层判断该播谁的反馈（见 02-TDD §5.1）。</summary>
    public struct StepDetail
    {
        public bool playerMoved;
        public bool enemyMoved;
        public bool playerPushedBox;
        public bool enemyPushedBox;
    }

    /// <summary>
    /// 规则引擎 —— 全项目移动逻辑的唯一实现。
    /// GameMode（运行时）与 Solver（求解）都必须经由此类执行状态转移，
    /// 绝不允许另写一份（否则规则漂移，会把有解关卡判成无解 —— 见 02-TDD §4.3）。
    /// </summary>
    public static class SokobanRules
    {
        // ------------------------------------------------------------------
        //  一步 = 玩家尝试移动 + 敌人无条件镜像尝试（v2 解耦规则）
        // ------------------------------------------------------------------
        public static StepResult Step(WorldState s, Direction dir, out StepDetail detail)
        {
            long before = s.Hash;

            detail = default;
            detail.playerMoved = TryMovePlayer(s, dir, out detail.playerPushedBox);

            // v2：敌人执行与玩家是否移动解耦 —— 无条件尝试
            detail.enemyMoved = TryMoveEnemy(s, DirectionOp.OppositeOf(dir), out detail.enemyPushedBox);

            // ADR 09：世界哈希变化才有效
            return s.Hash != before ? StepResult.Ok : StepResult.NoOp;
        }

        // ------------------------------------------------------------------
        //  玩家移动（见 02-TDD §3.1）
        // ------------------------------------------------------------------
        public static bool TryMovePlayer(WorldState s, Direction dir, out bool pushedBox)
        {
            pushedBox = false;
            var next = s.Player + DirectionOp.Delta[(int)dir];

            if (IsBlocked(s, next)) return false;            // 墙 / 障碍 / 界外

            if (s.HasEnemy && next == s.Enemy) return false; // 撞敌人 → 不动（双向不可重合）

            int bi = s.BoxAt(next);
            if (bi >= 0)
            {
                var after = next + DirectionOp.Delta[(int)dir];
                if (IsBlocked(s, after)) return false;
                if (s.HasEnemy && after == s.Enemy) return false;  // 不许把箱子推到敌人身上
                if (s.BoxAt(after) >= 0) return false;             // 连环箱不允许
                s.Boxes[bi] = after;
                s.SortBoxes();
                pushedBox = true;
            }

            s.Player = next;
            return true;
        }

        // ------------------------------------------------------------------
        //  邪恶推箱人移动（见 02-TDD §3.3）：与玩家同一套判定 + 撞玩家取消
        // ------------------------------------------------------------------
        public static bool TryMoveEnemy(WorldState s, Direction dir, out bool pushedBox)
        {
            pushedBox = false;
            if (!s.HasEnemy) return false;

            var next = s.Enemy + DirectionOp.Delta[(int)dir];

            if (IsBlocked(s, next)) return false;
            if (next == s.Player) return false;              // 撞玩家 → 整步取消

            int bi = s.BoxAt(next);
            if (bi >= 0)
            {
                var after = next + DirectionOp.Delta[(int)dir];
                if (IsBlocked(s, after)) return false;
                if (after == s.Player) return false;               // 不许把玩家挤走
                if (s.BoxAt(after) >= 0) return false;
                s.Boxes[bi] = after;
                s.SortBoxes();
                pushedBox = true;
            }

            s.Enemy = next;
            return true;
        }

        static bool IsBlocked(WorldState s, Vec2Int p)
        {
            var t = s.At(p);
            return t == CellType.Wall || t == CellType.Obstacle;
        }
    }
}
