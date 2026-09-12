using System.Collections.Generic;

namespace Sokoban.Solver
{
    using Core;

    /// <summary>
    /// 死锁格子预计算（见 02-TDD §4.2）：一次性把「箱子推进去必死」的格子标出来，
    /// 正向 / 反向搜索共用查表，避免逐步重复计算。
    /// 当前实现：角落死锁 + 墙边死锁（直线段无终点）。
    /// </summary>
    public static class Deadlock
    {
        public static bool[] ComputeDeadSquares(WorldState s)
        {
            int w = s.Width, h = s.Height;
            var dead = new bool[w * h];
            var terrain = s.Terrain;

            bool IsWall(int idx) => idx < 0 || idx >= terrain.Length || terrain[idx] == (int)CellType.Wall;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (terrain[i] != (int)CellType.Empty) continue;   // goal / wall / obstacle 不标记

                bool up = IsWall(i - w), down = IsWall(i + w);
                bool left = IsWall(i - 1) && x > 0, right = IsWall(i + 1) && x < w - 1;
                // 界外视作墙（棋盘外围通常有外墙；即便没有，推出去也无解）

                // 角落死锁：水平方向一面墙 + 垂直方向一面墙
                if ((up || down) && ((x > 0 && IsWall(i - 1)) || (x < w - 1 && IsWall(i + 1))))
                    dead[i] = true;
                if ((left || right) && ((y > 0 && IsWall(i - w)) || (y < h - 1 && IsWall(i + w))))
                    dead[i] = true;
            }

            // 墙边死锁：贴同一面墙的连续直线段，段内无 goal → 全段 dead
            MarkWallLineDead(dead, terrain, w, h, horizontal: true);
            MarkWallLineDead(dead, terrain, w, h, horizontal: false);

            // 反向可达性：箱子「推不回任何 goal」的格子一律 dead
            MarkGoalUnreachable(dead, s);

            return dead;
        }

        /// <summary>
        /// 反向可达性剪枝（松弛模型，**安全超集**）：从所有 goal 反向拉箱，
        /// 反向一步 B → A（A = B - d）合法 ⟺ A 可站箱 **且** A - d 可站人（玩家退位格）。
        /// 这里忽略其它箱子、并假定玩家能在可走区域里自由走动（比真实规则宽松）
        /// ⟹ 求得的集合是「箱子真能推到 goal」的**超集** ⟹ 集合外的格子必定是死格。
        /// 比角落/墙线两条规则更强，且只要 O(w·h)。
        /// </summary>
        static void MarkGoalUnreachable(bool[] dead, WorldState s)
        {
            int w = s.Width, h = s.Height;
            var terrain = s.Terrain;

            bool Walk(int x, int y) => x >= 0 && y >= 0 && x < w && y < h
                && (terrain[y * w + x] == (int)CellType.Empty || terrain[y * w + x] == (int)CellType.Goal);

            var reach = new bool[w * h];
            var queue = new Queue<int>();

            for (int i = 0; i < terrain.Length; i++)
                if (terrain[i] == (int)CellType.Goal) { reach[i] = true; queue.Enqueue(i); }

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int yx = cur % w, yy = cur / w;

                for (int k = 0; k < 4; k++)
                {
                    int dx = DirectionOp.Delta[k].x, dy = DirectionOp.Delta[k].y;
                    int ax = yx - dx, ay = yy - dy;                 // 前驱箱位 A = B - d
                    int px = ax - dx, py = ay - dy;                 // 玩家退位格 A - d
                    if (!Walk(ax, ay) || !Walk(px, py)) continue;

                    int ai = ay * w + ax;
                    if (reach[ai]) continue;
                    reach[ai] = true;
                    queue.Enqueue(ai);
                }
            }

            for (int i = 0; i < dead.Length; i++)
                if (terrain[i] == (int)CellType.Empty && !reach[i]) dead[i] = true;
        }

        static void MarkWallLineDead(bool[] dead, int[] terrain, int w, int h, bool horizontal)
        {
            int outer = horizontal ? h : w;
            int inner = horizontal ? w : h;

            for (int a = 0; a < outer; a++)
            for (int b = 0; b < inner;)
            {
                // 找出贴墙的连续段
                int start = b;
                bool touchesWall = false;
                bool hasGoal = false;
                int idxOf(int k) => horizontal ? a * w + k : k * w + a;
                // horizontal：a 是行号 → 上/下邻居 ±w，用 a 判界；vertical：a 是列号 → 左/右邻居 ±1，用 a 判界。
                // （旧实现 vertical 误用 k 判界，w>h 的关卡会数组越界）
                bool wallAt(int k) => horizontal
                    ? (a > 0 && terrain[idxOf(k) - w] == (int)CellType.Wall) || (a < h - 1 && terrain[idxOf(k) + w] == (int)CellType.Wall)
                    : (a > 0 && terrain[idxOf(k) - 1] == (int)CellType.Wall) || (a < w - 1 && terrain[idxOf(k) + 1] == (int)CellType.Wall);

                // 该格能否站箱子（Empty / Goal）；墙、障碍、界外都不能 → 可以封住段的端头
                bool boxableAt(int k) => k >= 0 && k < inner
                    && (terrain[idxOf(k)] == (int)CellType.Empty || terrain[idxOf(k)] == (int)CellType.Goal);

                while (b < inner)
                {
                    int idx = idxOf(b);
                    // 箱子可占据的格 = Empty / Goal；Goal 不允许断段（否则含终点的贴墙段被误标死格 → 误判无解）
                    if (terrain[idx] != (int)CellType.Empty && terrain[idx] != (int)CellType.Goal) break;
                    if (!wallAt(b)) break;
                    touchesWall = true;
                    if (terrain[idx] == (int)CellType.Goal) hasGoal = true;
                    b++;
                }

                // 贴墙段里的箱子只能沿墙滑动（垂直方向被墙 + 玩家站位双重否决）。
                // 但只要段外相邻格还能站箱子，箱子滑出去就不再贴墙 → 可以脱离 → **不是死格**。
                // 所以只有「段内无 goal **且** 两端都封死（墙/障碍/界外）」才判死。
                // （旧实现漏了封端检查，把 microban 里大量「贴墙但可滑出」的格子误标死格 → 关卡被误判无解）
                bool sealedLeft = !boxableAt(start - 1);
                bool sealedRight = !boxableAt(b);

                if (touchesWall && !hasGoal && sealedLeft && sealedRight)
                    for (int k = start; k < b; k++) dead[idxOf(k)] = true;

                b++;  // 跳过断点
            }
        }
    }
}
