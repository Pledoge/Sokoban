using System;
using UnityEngine;

namespace Sokoban.Game
{
    using Core;

    /// <summary>PC 键盘输入（见 02-TDD §5.3）。移动端 SwipeInput 后续同接口接入。</summary>
    public class KeyboardInput : MonoBehaviour, IInputProvider
    {
        public event Action<Direction> OnDirection;
        public event Action OnUndoRequested;
        public event Action OnRedoRequested;
        public event Action OnResetRequested;
        public event Action OnNextLevelRequested;
        public event Action OnPauseRequested;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) OnDirection?.Invoke(Direction.Up);
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) OnDirection?.Invoke(Direction.Down);
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) OnDirection?.Invoke(Direction.Left);
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) OnDirection?.Invoke(Direction.Right);

            if (Input.GetKeyDown(KeyCode.Z)) OnUndoRequested?.Invoke();
            if (Input.GetKeyDown(KeyCode.Y)) OnRedoRequested?.Invoke();
            if (Input.GetKeyDown(KeyCode.R)) OnResetRequested?.Invoke();
            if (Input.GetKeyDown(KeyCode.N)) OnNextLevelRequested?.Invoke();
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) OnPauseRequested?.Invoke();
        }
    }
}
