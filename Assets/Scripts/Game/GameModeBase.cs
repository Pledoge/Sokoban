using System.Collections.Generic;

namespace Sokoban.Game
{
    using Core;

    /// <summary>模式接口（见 02-TDD §5.1）。</summary>
    public interface IGameMode
    {
        WorldState State { get; }
        StepResult Step(Direction playerDir, out StepDetail detail);
        void Reset();
        void Undo();
        void Redo();

        int StepCount { get; }
        int UndoCount { get; }
        bool CanUndo { get; }
        bool CanRedo { get; }
        bool IsWon { get; }
    }

    /// <summary>
    /// 经典 / 拓展共用基类：撤销栈、计步逻辑只写一份。
    /// 状态转移全部经由 <see cref="SokobanRules"/>，本类不含任何移动判定。
    /// </summary>
    public abstract class GameModeBase : IGameMode
    {
        public WorldState State { get; protected set; }
        protected LevelData Level;

        readonly List<MoveSnapshot> _undo = new List<MoveSnapshot>();
        readonly List<MoveSnapshot> _redo = new List<MoveSnapshot>();

        public int StepCount { get; private set; }
        public int UndoCount { get; private set; }
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool IsWon => State != null && State.AllBoxesOnGoal();

        protected GameModeBase(LevelData level)
        {
            Level = level;
            State = new WorldState(level);
        }

        public StepResult Step(Direction playerDir, out StepDetail detail)
        {
            detail = default;
            if (IsWon) return StepResult.NoOp;      // 已通关后锁操作

            var snapshot = new MoveSnapshot(State, playerDir);   // 执行前抓快照（全量）
            var result = SokobanRules.Step(State, playerDir, out detail);

            if (result == StepResult.Ok)
            {
                // ADR 09：世界变了才入栈；ADR 14：撤销不减步数
                _undo.Add(snapshot);
                _redo.Clear();
                StepCount++;
            }
            return result;
        }

        public virtual void Reset()
        {
            State = new WorldState(Level);
            _undo.Clear(); _redo.Clear();
            StepCount = 0; UndoCount = 0;
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var snap = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            // 重做栈存「撤销前的当前态」
            _redo.Add(CurrentSnapshot(snap.playerInput));

            Restore(snap);
            UndoCount++;
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var snap = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);

            _undo.Add(CurrentSnapshot(snap.playerInput));
            Restore(snap);
        }

        MoveSnapshot CurrentSnapshot(Direction input) => new MoveSnapshot(State, input);

        void Restore(MoveSnapshot snap)
        {
            State.Player = snap.playerBefore;
            State.HasEnemy = snap.hasEnemy;
            State.Enemy = snap.enemyBefore;
            State.Boxes = (Vec2Int[])snap.boxesBefore.Clone();
        }
    }
}
