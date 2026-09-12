using System;

namespace Sokoban.Game
{
    using Core;
    using UnityEngine;

    /// <summary>
    /// 游戏主控制器（薄层）：持有模式对象，分发输入，广播事件。
    /// 移动判定、撤销、计步全部在 GameModeBase / SokobanRules，不在此处。
    /// </summary>
    public class GameController : MonoBehaviour
    {
        public IGameMode Mode { get; private set; }
        public bool IsPaused { get; private set; }

        public event Action<StepDetail> OnStepTaken;
        public event Action OnStepBlocked;    // 撞墙 / 推不动（半步无效）→ 音效层播反馈音
        public event Action OnWin;
        public event Action OnWorldChanged;   // 撤销/重做/重置后视图层重刷

        public void LoadLevel(LevelData level)
        {
            Mode = GameModeFactory.Create(level);
            IsPaused = false;
            OnWorldChanged?.Invoke();
        }

        public void BindInput(IInputProvider input)
        {
            input.OnDirection += dir => Step(dir);   // lambda 丢弃返回值（方法组直挂 Action<T> 会 CS0407）
            input.OnUndoRequested += DoUndo;
            input.OnRedoRequested += DoRedo;
            input.OnResetRequested += DoReset;
        }

        /// <summary>
        /// 完整的一步：执行 → 事件广播 → 胜负判定。
        /// 输入层 / 测试 / 求解验证统一走这里，保证事件链路一致。
        /// </summary>
        public StepResult Step(Direction dir)
        {
            if (Mode == null || IsPaused) return StepResult.NoOp;

            var result = Mode.Step(dir, out var detail);
            if (result == StepResult.NoOp)
            {
                OnStepBlocked?.Invoke();
                return result;
            }

            OnStepTaken?.Invoke(detail);
            if (Mode.IsWon) OnWin?.Invoke();
            return result;
        }

        public void DoUndo() { if (Mode == null || IsPaused) return; if (!Mode.CanUndo) return; Mode.Undo(); OnWorldChanged?.Invoke(); }
        public void DoRedo() { if (Mode == null || IsPaused) return; if (!Mode.CanRedo) return; Mode.Redo(); OnWorldChanged?.Invoke(); }
        public void DoReset() { if (Mode == null) return; Mode.Reset(); IsPaused = false; OnWorldChanged?.Invoke(); }
        public void Pause(bool paused) => IsPaused = paused;
    }

    /// <summary>输入抽象（PC 键盘 / 手柄 / 移动端滑动只产出 Direction）。</summary>
    public interface IInputProvider
    {
        event Action<Direction> OnDirection;
        event Action OnUndoRequested;
        event Action OnRedoRequested;
        event Action OnResetRequested;
    }
}
