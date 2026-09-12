using NUnit.Framework;

namespace Sokoban.Tests
{
    using Core;
    using Game;

    /// <summary>测试关卡构建器：从 ASCII 行构建 LevelData（与 Tools/gen_levels.py 同一图例）。</summary>
    public static class TestLevels
    {
        public static LevelData Make(string[] rows, Mode mode = Mode.Classic)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            var lv = LevelData.CreateBlank(mode, w, h);
            lv.id = "test";
            for (int i = 0; i < h; i++)
            {
                var row = rows[h - 1 - i];                    // y 向上：最后一行是 y=0
                for (int x = 0; x < w; x++)
                {
                    lv.cells[lv.Index(x, i)] = row[x] switch
                    {
                        '#' => (int)CellType.Wall,
                        'O' => (int)CellType.Obstacle,
                        'G' => (int)CellType.Goal,
                        'B' => (int)CellType.Box,
                        '*' => (int)CellType.BoxOnGoal,
                        'P' => (int)CellType.Player,
                        'E' => (int)CellType.Enemy,
                        _ => (int)CellType.Empty,
                    };
                    if (row[x] == 'P') lv.playerSpawn = new Vec2Int(x, i);
                    if (row[x] == 'E') lv.enemySpawn = new Vec2Int(x, i);
                }
            }
            return lv;
        }
    }

    /// <summary>规则引擎核心用例（02-TDD §8 v2/v3 必测项）。</summary>
    public class RulesTests
    {
        [Test]
        public void PushBox_OneStep_Wins()
        {
            var mode = new ClassicMode(TestLevels.Make(new[]
            {
                "#######",
                "#.....#",
                "#.PBG.#",
                "#.....#",
                "#######",
            }));
            var r = mode.Step(Direction.Right, out var d);
            Assert.AreEqual(StepResult.Ok, r);
            Assert.IsTrue(d.playerMoved && d.playerPushedBox);
            Assert.IsTrue(mode.IsWon);
            Assert.AreEqual(1, mode.StepCount);
        }

        [Test]
        public void PlayerBlocked_Wall_NoOp()
        {
            var mode = new ClassicMode(TestLevels.Make(new[]
            {
                "####",
                "#P.#",
                "####",
            }));
            var r = mode.Step(Direction.Up, out _);
            Assert.AreEqual(StepResult.NoOp, r);
            Assert.AreEqual(0, mode.StepCount);
        }

        [Test]
        public void PlayerBlocked_EnemyStillMoves()          // v2 核心用例
        {
            var mode = new ExtendedMode(TestLevels.Make(new[]
            {
                "######",
                "#P#..#",
                "#....#",
                "#.E..#",
                "#....#",
                "######",
            }, Mode.Extended));

            var eBefore = mode.State.Enemy;
            // 玩家按 Up 撞墙；敌人 Opposite(Up)=Down，方向通畅 → 敌人必须动
            var r = mode.Step(Direction.Up, out var d);

            Assert.AreEqual(StepResult.Ok, r);
            Assert.IsFalse(d.playerMoved);
            Assert.IsTrue(d.enemyMoved);
            Assert.AreEqual(eBefore + new Vec2Int(0, -1), mode.State.Enemy);
            Assert.AreEqual(1, mode.StepCount);              // 世界变了 → 计步
        }

        [Test]
        public void PlayerBlocked_EnemyBlocked_NoUndoPush()
        {
            var mode = new ExtendedMode(TestLevels.Make(new[]
            {
                "#####",
                "#P#.#",
                "#.#E#",
                "#####",
            }, Mode.Extended));

            long hBefore = mode.State.Hash;
            // 玩家按 Up 撞墙；敌人 Opposite=Down 撞墙 → 全不动
            var r = mode.Step(Direction.Up, out _);

            Assert.AreEqual(StepResult.NoOp, r);
            Assert.AreEqual(hBefore, mode.State.Hash);
            Assert.AreEqual(0, mode.StepCount);
            Assert.IsFalse(mode.CanUndo);                    // 无效输入不入栈
        }

        [Test]
        public void EnemyCannotOverlapPlayer_BothDirections()
        {
            // 场景 C：互顶 —— 玩家按 Right 朝敌人，敌人 Left 朝玩家，谁也进不去
            var mode = new ExtendedMode(TestLevels.Make(new[]
            {
                "#####",
                "#.PE#",
                "#...#",
                "#####",
            }, Mode.Extended));

            long hBefore = mode.State.Hash;
            var r = mode.Step(Direction.Right, out var d);

            Assert.AreEqual(StepResult.NoOp, r);
            Assert.IsFalse(d.playerMoved);
            Assert.IsFalse(d.enemyMoved);
            Assert.AreEqual(hBefore, mode.State.Hash);
        }

        [Test]
        public void PlayerCannotPushBoxIntoEnemy()
        {
            var mode = new ExtendedMode(TestLevels.Make(new[]
            {
                "######",
                "#PBE.#",
                "#....#",
                "######",
            }, Mode.Extended));

            long hBefore = mode.State.Hash;
            // 玩家推 B 向右，但箱子后方是敌人 → 玩家不动；敌人 Opposite(Right)=Left，
            // 敌人左边是 B → 推不动 → 全不动
            var r = mode.Step(Direction.Right, out _);
            Assert.AreEqual(StepResult.NoOp, r);
            Assert.AreEqual(hBefore, mode.State.Hash);
        }

        [Test]
        public void UndoRestoresEnemyOnlyStep()
        {
            var mode = new ExtendedMode(TestLevels.Make(new[]
            {
                "######",
                "#P#..#",
                "#....#",
                "#.E..#",
                "#....#",
                "######",
            }, Mode.Extended));

            var pBefore = mode.State.Player;
            var eBefore = mode.State.Enemy;

            mode.Step(Direction.Up, out _);                  // 仅敌人动
            Assert.IsTrue(mode.CanUndo);
            mode.Undo();

            Assert.AreEqual(pBefore, mode.State.Player);     // 玩家本来就没动
            Assert.AreEqual(eBefore, mode.State.Enemy);      // 敌人回退
            Assert.AreEqual(1, mode.UndoCount);
            Assert.IsFalse(mode.CanUndo);
        }

        [Test]
        public void ClassicLevel_StaleEnemySpawn_DoesNotSpawnEnemy()
        {
            // 编辑器把关卡从「拓展」切回「经典」时只清了格子里的敌人，enemySpawn 曾残留 →
            // 存出来的经典关卡带着敌人出生点，运行时就会冒出邪恶推箱人（BUG：经典模式的 114514 关）。
            var rows = new[]
            {
                "######",
                "#P...#",
                "#.B.G#",
                "#...E#",
                "######",
            };

            var classic = TestLevels.Make(rows);                  // 默认 Mode.Classic，但地图里有 'E'
            Assert.IsFalse(classic.HasEnemy, "经典模式不应生成敌人");
            var cm = new ClassicMode(classic);
            Assert.IsFalse(cm.State.HasEnemy);
            cm.Step(Direction.Up, out var cd);
            Assert.IsFalse(cd.enemyMoved, "经典模式不该有敌人跟随移动");

            // 同一份地图数据切到拓展模式 → 敌人照常出现（收口在 mode，而非把数据丢掉）
            var extended = TestLevels.Make(rows, Mode.Extended);
            Assert.IsTrue(extended.HasEnemy);
            var em = new ExtendedMode(extended);
            Assert.IsTrue(em.State.HasEnemy);
        }
    }

    /// <summary>计步规则用例（v3）。</summary>
    public class StepCountTests
    {
        [Test]
        public void Undo_DoesNotDecreaseStepCount()          // ADR 14
        {
            var mode = new ClassicMode(TestLevels.Make(new[]
            {
                "#######",
                "#.....#",
                "#.PBG.#",
                "#.....#",
                "#######",
            }));

            mode.Step(Direction.Right, out _);               // 1 步（推箱获胜）
            Assert.AreEqual(1, mode.StepCount);
            mode.Step(Direction.Left, out _);                // 2 步（胜利后锁操作，实际 NoOp）
            Assert.AreEqual(1, mode.StepCount);              // 胜利后不再计步
        }

        [Test]
        public void StepCount_IgnoresNoOpInput()
        {
            var mode = new ClassicMode(TestLevels.Make(new[]
            {
                "####",
                "#P.#",
                "####",
            }));
            for (int i = 0; i < 5; i++) mode.Step(Direction.Up, out _);
            Assert.AreEqual(0, mode.StepCount);
        }

        [Test]
        public void ExtendedMode_EnemyNotCountedAsStep()
        {
            var mode = new ExtendedMode(TestLevels.Make(new[]
            {
                "######",
                "#P#..#",
                "#....#",
                "#.E..#",
                "#....#",
                "######",
            }, Mode.Extended));
            // 玩家+敌人一步 → 只 +1（敌人的移动隐含在玩家步里）
            var r = mode.Step(Direction.Up, out var d);
            Assert.AreEqual(StepResult.Ok, r);
            Assert.IsTrue(d.enemyMoved);
            Assert.AreEqual(1, mode.StepCount);
        }

    }
}
