using System;

namespace Sokoban.EditorCore
{
    using Core;

    /// <summary>编辑器工具（见 GDD §4.3）。</summary>
    public enum EditorTool : byte
    {
        Wall, Player, Enemy, Box, Goal, Obstacle, Erase
    }

    /// <summary>整关校验结果。</summary>
    public class ValidationResult
    {
        public bool Ok = true;
        public System.Collections.Generic.List<string> Errors = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> Warnings = new System.Collections.Generic.List<string>();

        public void Error(string msg) { Ok = false; Errors.Add(msg); }
        public void Warn(string msg) { Warnings.Add(msg); }
    }

    /// <summary>
    /// 关卡编辑器核心 —— 双形态共用的纯 C# 逻辑（见 02-TDD §5.0）。
    /// 不引用 UnityEngine / UnityEditor；序列化只产出/消费 JSON 字符串，文件 IO 由壳负责。
    /// </summary>
    public class LevelEditorCore
    {
        public LevelData Data { get; private set; }
        public EditorTool CurrentTool { get; set; } = EditorTool.Wall;

        // 编辑操作历史（自建栈；Editor 壳如需接 Unity Undo，可在壳层包装）
        readonly System.Collections.Generic.List<string> _undoJson = new System.Collections.Generic.List<string>();
        readonly System.Collections.Generic.List<string> _redoJson = new System.Collections.Generic.List<string>();

        public bool CanUndo => _undoJson.Count > 0;
        public bool CanRedo => _redoJson.Count > 0;

        public LevelEditorCore(LevelData data) => Data = data;

        public LevelEditorCore(Mode mode, int w = 16, int h = 12) : this(LevelData.CreateBlank(mode, w, h)) { }

        /// <summary>落笔。返回 false 表示该放置被规则拒绝（互斥 / 界外）。</summary>
        public bool TryPaint(int x, int y)
        {
            if (!Data.InBounds(x, y)) return false;
            PushHistory();

            switch (CurrentTool)
            {
                case EditorTool.Wall:
                    // toggle：已是墙则擦掉
                    if (Data.GetCell(x, y) == CellType.Wall) Data.SetCell(x, y, CellType.Empty);
                    else Data.SetCell(x, y, CellType.Wall);
                    return true;

                case EditorTool.Erase:
                    // 橡皮 = 任何已放置元素（含外墙、敌人等）都能擦；落回 Empty。
                    //  整关合法性由 Save() 时 LevelValidator 把关；中途允许越界破坏，便于编辑。
                    Data.SetCell(x, y, CellType.Empty);
                    return true;

                case EditorTool.Player:
                    if (IsSolid(x, y)) return Reject();
                    ClearOther(x, y, CellType.Player);
                    Data.SetCell(x, y, CellType.Player);
                    Data.playerSpawn = new Vec2Int(x, y);
                    return true;

                case EditorTool.Enemy:
                    if (IsSolid(x, y)) return Reject();
                    ClearOther(x, y, CellType.Enemy);                  // 只允许一个敌人
                    Data.SetCell(x, y, CellType.Enemy);
                    Data.enemySpawn = new Vec2Int(x, y);
                    return true;

                case EditorTool.Box:
                    if (IsSolid(x, y)) return Reject();
                    Data.SetCell(x, y, CellType.Box);
                    return true;

                case EditorTool.Goal:
                    if (IsSolid(x, y)) return Reject();
                    Data.SetCell(x, y, CellType.Goal);
                    return true;

                case EditorTool.Obstacle:
                    if (Data.GetCell(x, y) != CellType.Empty) return Reject();
                    Data.SetCell(x, y, CellType.Obstacle);
                    return true;
            }
            return Reject();
        }

        bool IsSolid(int x, int y)
        {
            var c = Data.GetCell(x, y);
            return c == CellType.Wall || c == CellType.Obstacle;
        }

        void ClearOther(int x, int y, CellType placed)
        {
            // 同类对象全局唯一（玩家 / 敌人）：清掉旧位置
            for (int yy = 0; yy < Data.height; yy++)
            for (int xx = 0; xx < Data.width; xx++)
                if (Data.GetCell(xx, yy) == placed && !(xx == x && yy == y))
                    Data.SetCell(xx, yy, CellType.Empty);
        }

        bool Reject() { _undoJson.RemoveAt(_undoJson.Count - 1); return false; }

        void PushHistory()
        {
            _undoJson.Add(LevelIO.ToJson(Data));
            if (_undoJson.Count > 200) _undoJson.RemoveAt(0);
            _redoJson.Clear();
        }

        public void UndoEdit()
        {
            if (!CanUndo) return;
            _redoJson.Add(LevelIO.ToJson(Data));
            Data = LevelIO.FromJson(_undoJson[_undoJson.Count - 1]);
            _undoJson.RemoveAt(_undoJson.Count - 1);
        }

        public void RedoEdit()
        {
            if (!CanRedo) return;
            _undoJson.Add(LevelIO.ToJson(Data));
            Data = LevelIO.FromJson(_redoJson[_redoJson.Count - 1]);
            _redoJson.RemoveAt(_redoJson.Count - 1);
        }

        public void LoadFromJson(string json) => Data = LevelIO.FromJson(json);
        public string ToJson() => LevelIO.ToJson(Data);

        /// <summary>整关校验（外墙闭合 / 对象合法 / 箱 goal 配平）。</summary>
        public ValidationResult Validate() => LevelValidator.Validate(Data);
    }
}
