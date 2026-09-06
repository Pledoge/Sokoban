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
            bool IsGoal(int idx) => !dead[idx] && terrain[idx] == (int)CellType.Goal;

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

            return dead;
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
                bool wallAt(int k) => horizontal
                    ? (a > 0 && terrain[idxOf(k) - w] == (int)CellType.Wall) || (a < h - 1 && terrain[idxOf(k) + w] == (int)CellType.Wall)
                    : (k > 0 && terrain[idxOf(k) - 1] == (int)CellType.Wall) || (k < w - 1 && terrain[idxOf(k) + 1] == (int)CellType.Wall);

                while (b < inner)
                {
                    int idx = idxOf(b);
                    if (terrain[idx] != (int)CellType.Empty) break;
                    if (!wallAt(b)) break;
                    touchesWall = true;
                    if (terrain[idx] == (int)CellType.Goal) hasGoal = true;
                    b++;
                }

                if (touchesWall && !hasGoal)
                    for (int k = start; k < b; k++) dead[idxOf(k)] = true;

                b++;  // 跳过断点
            }
        }
    }
}
