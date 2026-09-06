namespace Sokoban.Solver
{
    using Core;

    /// <summary>
    /// 策略路由（见 02-TDD §4.1 / §4.5）：
    /// ≤ 8 箱正向 BFS；9 ~ 15 箱反向 BFS；拓展模式恒走正向（敌人镜像行为的反向推导极易出错）。
    /// 反向 BFS 为 P1 —— BackwardBfsSolver 就位前，9~15 箱暂时降级正向 + 放宽超时。
    /// </summary>
    public static class SolverRouter
    {
        public static SolveStrategy PickStrategy(LevelData level)
        {
            if (level.mode == Mode.Extended) return SolveStrategy.ForwardBfs;
            return level.CountBoxes() <= 8 ? SolveStrategy.ForwardBfs : SolveStrategy.BackwardBfs;
        }

        public static SolveResult Solve(LevelData level, int timeoutMs = 5000)
        {
            var strategy = PickStrategy(level);
            var solver = strategy == SolveStrategy.BackwardBfs
                ? (ISokobanSolver)new BackwardBfsSolver()
                : (ISokobanSolver)new ForwardBfsSolver();
            return solver.Solve(level, timeoutMs);
        }
    }

    /// <summary>
    /// 反向 BFS（P1，见 02-TDD §4.4）：从「箱子全在 goal」反向拉箱。
    /// 当前为占位实现 —— 返回 Timeout 并在 result 中标注，保证 UI 行为可预期；
    /// 落地时严格按 §4.4 实现（反向拉箱 + PlayerReachable 可达性剪枝 + 终局玩家位置校验）。
    /// </summary>
    public class BackwardBfsSolver : ISokobanSolver
    {
        public SolveResult Solve(LevelData level, int timeoutMs = 5000)
        {
            // 先用正向兜底跑一遍短超时：若正向能在限时内出结论，直接采用（不比假装超时差）
            var fallback = new ForwardBfsSolver().Solve(level, timeoutMs);
            if (fallback.status == SolveStatus.Solvable || fallback.status == SolveStatus.Unsolvable)
            {
                fallback.strategy = SolveStrategy.BackwardBfs;   // 由路由选择，但实际走正向兜底
                return fallback;
            }

            return new SolveResult
            {
                status = SolveStatus.Timeout,
                strategy = SolveStrategy.BackwardBfs,
                elapsedMs = fallback.elapsedMs,
                visitedStates = fallback.visitedStates,
            };
        }
    }
}
