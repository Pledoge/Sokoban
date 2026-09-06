using System;

namespace Sokoban.Core
{
    public enum Mode : byte { Classic = 0, Extended = 1 }

    /// <summary>
    /// 关卡静态数据（见 02-TDD §2.2）。
    /// 存储为扁平数组（JsonUtility 不支持锯齿数组），语义仍是 cells[y][x]，index = y * width + x。
    /// 文件里允许出现 Box/Player/Enemy 等「放置语义」格子，加载时由 WorldState 分离。
    /// </summary>
    [Serializable]
    public class LevelData
    {
        public string id = "custom_00";
        public string name = "未命名关卡";
        public Mode mode = Mode.Classic;
        public int width = 16;
        public int height = 12;
        public string author = "";

        public int[] cells;                 // 扁平存储，长度 = width * height

        public Vec2Int playerSpawn = new Vec2Int(1, 1);
        public Vec2Int enemySpawn = new Vec2Int(-99, -99);   // 不存在时用界外值表示

        public bool HasEnemy => enemySpawn.x >= 0 && enemySpawn.y >= 0
                                             && enemySpawn.x < width && enemySpawn.y < height;

        public int Index(int x, int y) => y * width + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

        public CellType GetCell(int x, int y) => (CellType)cells[Index(x, y)];
        public void SetCell(int x, int y, CellType t) => cells[Index(x, y)] = (int)t;

        public static LevelData CreateBlank(Mode mode, int w = 16, int h = 12)
        {
            var lv = new LevelData
            {
                mode = mode,
                width = w,
                height = h,
                cells = new int[w * h],   // 默认全 Empty
                playerSpawn = new Vec2Int(w / 2, h / 2),
                enemySpawn = new Vec2Int(-99, -99),
            };
            return lv;
        }

        public LevelData DeepClone()
        {
            return new LevelData
            {
                id = id, name = name, mode = mode, width = width, height = height, author = author,
                cells = (int[])cells.Clone(),
                playerSpawn = playerSpawn,
                enemySpawn = enemySpawn,
            };
        }

        /// <summary>统计箱子数（路由求解策略用）。</summary>
        public int CountBoxes()
        {
            if (cells == null) return 0;
            int n = 0;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] == (int)CellType.Box || cells[i] == (int)CellType.BoxOnGoal) n++;
            return n;
        }
    }
}
