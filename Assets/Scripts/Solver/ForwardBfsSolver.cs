using System.Collections.Generic;
using System.Diagnostics;

namespace Sokoban.Solver
{
    using Core;

    /// <summary>
    /// 正向 BFS（见 02-TDD §4.3）。
    /// 状态转移**只调用 SokobanRules.Step** —— 与运行时同一套规则，绝不另写（硬性规则）。
    /// 经典 / 拓展通用：拓展模式的状态哈希天然含敌人坐标。
    /// </summary>
    public class ForwardBfsSolver : ISokobanSolver
    {
        static readonly Direction[] AllDirs =
        {
            Direction.Up, Direction.Down, Direction.Left, Direction.Right
        };

        class Node
        {
            public WorldState State;
            public int Parent;          // 在 _nodes 里的下标，-1 为根
            public byte Move;           // 从父节点到本节点的 Direction
        }

        public SolveResult Solve(LevelData level, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            var result = new SolveResult { strategy = SolveStrategy.ForwardBfs };

            if (level.playerSpawn.x < 0 || level.playerSpawn.x >= level.width)
            {
                result.status = SolveStatus.Error;
                return result;
            }

            var dead = Deadlock.ComputeDeadSquares(new WorldState(level));

            var nodes = new List<Node> { new Node { State = new WorldState(level), Parent = -1, Move = 0 } };
            var visited = new HashSet<long> { nodes[0].State.Hash };

            // 初始箱子就落在死格 → 直接无解（编辑器快速反馈）
            foreach (var b in nodes[0].State.Boxes)
            {
                if (nodes[0].State.IsGoal(b)) continue;   // goal 上不算死
                if (dead[nodes[0].State.Index(b)])
                {
                    result.status = SolveStatus.Unsolvable;
                    result.elapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }
            }

            if (nodes[0].State.AllBoxesOnGoal())
            {
                result.status = SolveStatus.Solvable;
                result.steps = 0;
                result.path = new List<Direction>();
                result.elapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            for (int head = 0; head < nodes.Count; head++)
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                {
                    result.status = SolveStatus.Timeout;
                    result.visitedStates = visited.Count;
                    result.elapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }

                var cur = nodes[head].State;
                foreach (var dir in AllDirs)
                {
                    var next = cur.Clone();
                    var r = SokobanRules.Step(next, dir, out _);

                    // 自环（玩家+敌人都没动）→ 不入队，避免死循环
                    if (r == StepResult.NoOp) continue;
                    long h = next.Hash;
                    if (!visited.Add(h)) continue;

                    // 玩家推出去的箱子落在死格 → 剪枝
                    if (next.Boxes.Length > 0)
                    {
                        // 只检查本步玩家推的箱子：next.player 前一格
                        var pushed = next.Player;
                        // 敌人推的箱子也可能落死格 —— 全量检查（箱子数 ≤ 12，代价可忽略）
                        foreach (var b in next.Boxes)
                            if (!next.IsGoal(b) && dead[next.Index(b)])
                                goto skip;
                    }

                    nodes.Add(new Node { State = next, Parent = head, Move = (byte)dir });

                    if (next.AllBoxesOnGoal())
                    {
                        result.status = SolveStatus.Solvable;
                        result.steps = Reconstruct(nodes, nodes.Count - 1, out var path);
                        result.path = path;
                        result.visitedStates = visited.Count;
                        result.elapsedMs = sw.ElapsedMilliseconds;
                        return result;
                    }

                    skip: ;
                }
            }

            result.status = SolveStatus.Unsolvable;
            result.visitedStates = visited.Count;
            result.elapsedMs = sw.ElapsedMilliseconds;
            return result;
        }

        static int Reconstruct(List<Node> nodes, int idx, out List<Direction> path)
        {
            path = new List<Direction>();
            int steps = 0;
            while (nodes[idx].Parent >= 0)
            {
                path.Add((Direction)nodes[idx].Move);
                idx = nodes[idx].Parent;
                steps++;
            }
            path.Reverse();
            return steps;
        }
    }
}
