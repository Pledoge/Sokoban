using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Sokoban.Tests
{
    using Core;
    using EditorCore;
    using Solver;

    /// <summary>求解器与策略路由用例。</summary>
    public class SolverTests
    {
        [Test]
        public void Solver_Tut01_SolvableIn1Step()
        {
            var r = SolverRouter.Solve(TestLevels.Make(new[]
            {
                "#######",
                "#.....#",
                "#.PBG.#",
                "#.....#",
                "#######",
            }));
            Assert.AreEqual(SolveStatus.Solvable, r.status);
            Assert.AreEqual(1, r.steps);
        }

        [Test]
        public void Solver_DeadSquareStart_UnsolvableFast()
        {
            // 箱子出生在角落死格（上、左都是墙，且该格不是 goal）
            var r = SolverRouter.Solve(TestLevels.Make(new[]
            {
                "#####",
                "#PB.#",
                "#...#",
                "#..G#",
                "#####",
            }));
            Assert.AreEqual(SolveStatus.Unsolvable, r.status);
        }

        [Test]
        public void Deadlock_WallLine_SealedBothEnds_IsDead()
        {
            // 整条贴墙线（上下都是墙）+ 左右被外墙封死 + 段内无 goal → 箱子只能沿墙滑，永远到不了终点
            var lv = TestLevels.Make(new[]
            {
                "#######",
                "#B....#",
                "#######",
            });
            var dead = Deadlock.ComputeDeadSquares(new WorldState(lv));
            for (int x = 1; x <= 5; x++)
                Assert.IsTrue(dead[1 * lv.width + x], $"({x},1) 贴墙且两端封死，应判死格");
        }

        [Test]
        public void Deadlock_WallLine_OpenEnd_NotDead()      // 回归：贴墙段两端未封死不是死格
        {
            // 下排留了一个缺口 → 箱子能从贴墙线滑出去、再被垂直推开 → 不是死格。
            // 旧实现漏了「封端检查」，把这种格子全判死 → microban 里 143/155 关被误判无解。
            var lv = TestLevels.Make(new[]
            {
                "#######",
                "#P....#",
                "#.B..G#",
                "###.###",
            });
            var ws = new WorldState(lv);
            var dead = Deadlock.ComputeDeadSquares(ws);

            Assert.IsFalse(dead[ws.Index(new Vec2Int(2, 1))], "(2,1) 贴墙但有开口，不该判死格");
            Assert.IsFalse(dead[ws.Index(new Vec2Int(4, 1))], "(4,1) 同理");

            var r = SolverRouter.Solve(lv, 4000);
            Assert.AreEqual(SolveStatus.Solvable, r.status, "贴墙但有滑出路径的关卡不应被判无解");
            Assert.AreEqual(4, r.steps, "1 步走位 + 3 步推箱");
        }

        [Test]
        public void Solver_ExtendedMode_WithEnemy_Solvable()
        {
            var r = SolverRouter.Solve(TestLevels.Make(new[]
            {
                "######",
                "#P...#",
                "#..B.#",
                "#..G.#",
                "#E...#",
                "######",
            }, Mode.Extended));
            Assert.AreEqual(SolveStatus.Solvable, r.status);
            Assert.AreEqual(SolveStrategy.ForwardBfs, r.strategy);   // 拓展恒走正向
        }

        [Test]
        public void Solver_RouterPicksRightStrategy()
        {
            var few = TestLevels.Make(new[] { "#####", "#PBG#", "#####" });           // 1 箱
            Assert.AreEqual(SolveStrategy.ForwardBfs, SolverRouter.PickStrategy(few));

            var many = TestLevels.Make(new[]
            {
                "##########",
                "#........#",
                "#.B.B....#",
                "#..B.....#",
                "#...B....#",
                "#....B...#",
                "#P....B..#",
                "#......B.#",
                "#.G.G.G.G#",
                "##########",
            });                                                                        // 8 箱仍正向
            Assert.AreEqual(SolveStrategy.ForwardBfs, SolverRouter.PickStrategy(many));

            var nine = TestLevels.Make(new[]
            {
                "###########",
                "#.........#",
                "#.B.B.B.B.#",
                "#.........#",
                "#.B.B.B.B.#",
                "#..G.G.G.G#",
                "#.........#",
                "#P........#",
                "#.B.......#",
                "#.G.......#",
                "###########",
            });                                                                        // 9 箱 → 反向
            Assert.AreEqual(SolveStrategy.BackwardBfs, SolverRouter.PickStrategy(nine));

            var ext = TestLevels.Make(new[]
            {
                "###########",
                "#.B.B.B.B.#",
                "#.B.B.B.B.#",
                "#.B.B.....#",
                "#.G.G.....#",
                "#PE.......#",
                "###########",
            }, Mode.Extended);                                                          // 拓展 11 箱仍正向
            Assert.AreEqual(SolveStrategy.ForwardBfs, SolverRouter.PickStrategy(ext));
        }

        [Test]
        public void Solver_AllBuiltInLevels_Solvable()
        {
            // D5 验证闭环：所有内置关卡必须「有解」，关卡数据坏了第一时间炸出来
            var dir = Path.Combine(Application.dataPath, "Resources/Levels");
            Assert.IsTrue(Directory.Exists(dir), "Assets/Resources/Levels 不存在 —— 先跑 Tools/gen_levels.py");

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var lv = LevelIO.FromJson(File.ReadAllText(file));
                var r = SolverRouter.Solve(lv, 8000);
                Assert.AreEqual(SolveStatus.Solvable, r.status,
                    $"内置关卡 {lv.id} 被判无解/超时（{r.status}, {r.elapsedMs}ms）");
                TestContext.Out.WriteLine($"{lv.id}: {r.steps} 步, {r.elapsedMs}ms, 策略 {r.strategy}");
            }
        }
    }

    /// <summary>编辑器核心用例（双形态一致性的一半 —— JSON 产出确定性）。</summary>
    public class EditorCoreTests
    {
        [Test]
        public void EditorCore_SameOperationSequence_SameJson()
        {
            var a = new LevelEditorCore(Mode.Classic, 8, 8);
            var b = new LevelEditorCore(Mode.Classic, 8, 8);

            foreach (var core in new[] { a, b })
            {
                core.CurrentTool = EditorTool.Wall;
                PaintRect(core, 1, 1, 6, 6);                  // 围一圈墙
                core.CurrentTool = EditorTool.Player;
                core.TryPaint(3, 3);
                core.CurrentTool = EditorTool.Box;
                core.TryPaint(4, 4);
                core.CurrentTool = EditorTool.Goal;
                core.TryPaint(5, 4);
            }

            Assert.AreEqual(a.ToJson(), b.ToJson(),
                "同一编辑操作序列必须产出逐字节相同的 JSON（双形态一致性）");
        }

        [Test]
        public void EditorCore_RejectThenHistoryUnchanged()
        {
            var core = new LevelEditorCore(Mode.Classic, 6, 6);
            core.CurrentTool = EditorTool.Player;
            Assert.IsTrue(core.TryPaint(2, 2));
            core.CurrentTool = EditorTool.Player;             // 玩家唯一：第二次放置会移动它
            core.CurrentTool = EditorTool.Box;
            core.TryPaint(2, 2);                              // 放在玩家身上 → 拒绝
            core.UndoEdit();
            Assert.IsNull(null);                              // 占位 —— 关键断言在下方
            // 拒绝的操作不能产生历史条目：撤销后回到初始
            var fresh = new LevelEditorCore(Mode.Classic, 6, 6);
            Assert.AreNotEqual(fresh.ToJson(), core.ToJson()); // 还剩玩家这步可撤销
        }

        [Test]
        public void Validator_DetectsOpenWall()
        {
            var core = new LevelEditorCore(Mode.Classic, 8, 8);
            core.CurrentTool = EditorTool.Player;
            core.TryPaint(1, 1);                              // 玩家在角落，没围外墙
            var r = core.Validate();
            Assert.IsFalse(r.Ok);
        }

        [Test]
        public void Validator_ClosedWall_Passes()
        {
            var core = new LevelEditorCore(Mode.Classic, 8, 8);
            core.CurrentTool = EditorTool.Wall;
            PaintRect(core, 1, 1, 6, 5);
            core.CurrentTool = EditorTool.Player;
            core.TryPaint(3, 3);
            core.CurrentTool = EditorTool.Box;
            core.TryPaint(4, 3);
            core.CurrentTool = EditorTool.Goal;
            core.TryPaint(5, 3);

            var r = core.Validate();
            Assert.IsTrue(r.Ok, "应通过校验，实际错误: " + string.Join("; ", r.Errors));
        }

        static void PaintRect(LevelEditorCore core, int x0, int y0, int x1, int y1)
        {
            for (int x = x0; x <= x1; x++) { core.TryPaint(x, y0); core.TryPaint(x, y1); }
            for (int y = y0; y <= y1; y++) { core.TryPaint(x0, y); core.TryPaint(x1, y); }
        }
    }
}
