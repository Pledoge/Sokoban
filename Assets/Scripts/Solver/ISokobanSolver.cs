using System.Collections.Generic;

namespace Sokoban.Solver
{
    using Core;

    public enum SolveStatus : byte { Solvable = 0, Unsolvable = 1, Timeout = 2, Error = 3 }
    public enum SolveStrategy : byte { ForwardBfs = 0, BackwardBfs = 1 }

    public class SolveResult
    {
        public SolveStatus status;
        public SolveStrategy strategy;
        public int steps = -1;                 // 最短步数（无解 / 超时为 -1）
        public long elapsedMs;
        public int visitedStates;
        public List<Direction> path;           // 最短解路径（超时/无解为 null）
    }

    public interface ISokobanSolver
    {
        SolveResult Solve(LevelData level, int timeoutMs = 5000);
    }
}
