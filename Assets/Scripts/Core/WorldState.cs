using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    /// <summary>
    /// 运行时世界状态（见 02-TDD §2.3）：静态地形 + 动态对象分离。
    /// 只存地形（Empty/Wall/Obstacle/Goal）与动态对象坐标；「箱子在终点上」等状态由坐标推导。
    /// </summary>
    public class WorldState
    {
        public int Width;
        public int Height;
        public int[] Terrain;          // 仅 CellType.Empty/Wall/Obstacle/Goal

        public Vec2Int Player;
        public bool HasEnemy;
        public Vec2Int Enemy;
        public Vec2Int[] Boxes;        // 始终保持按 Index 排序，保证 Hash 稳定

        bool[] _goalMask;
        int[] _sortedIdx;              // 复用缓冲，避免每次 Hash 产生 GC

        public WorldState() { }

        /// <summary>从关卡静态数据构建初始世界状态。</summary>
        public WorldState(LevelData lv)
        {
            Width = lv.width;
            Height = lv.height;
            Terrain = new int[Width * Height];
            var boxes = new List<Vec2Int>();

            Player = lv.playerSpawn;
            HasEnemy = lv.HasEnemy;
            Enemy = lv.enemySpawn;

            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var c = lv.GetCell(x, y);
                switch (c)
                {
                    case CellType.Wall:    Terrain[lv.Index(x, y)] = (int)CellType.Wall; break;
                    case CellType.Obstacle: Terrain[lv.Index(x, y)] = (int)CellType.Obstacle; break;
                    case CellType.Goal:    Terrain[lv.Index(x, y)] = (int)CellType.Goal; break;
                    case CellType.Box:
                    case CellType.BoxOnGoal:
                        boxes.Add(new Vec2Int(x, y));
                        Terrain[lv.Index(x, y)] = (int)CellType.Empty; // 箱子是动态对象，地形视为空
                        break;
                    case CellType.Player:
                    case CellType.Enemy:
                        Terrain[lv.Index(x, y)] = (int)CellType.Empty;
                        break;
                    default:
                        Terrain[lv.Index(x, y)] = (int)CellType.Empty;
                        break;
                }
            }
            Boxes = boxes.OrderBy(Index).ToArray();
            BuildGoalMask();
        }

        public int Index(Vec2Int p) => p.y * Width + p.x;
        public bool InBounds(Vec2Int p) => p.x >= 0 && p.y >= 0 && p.x < Width && p.y < Height;
        public CellType At(Vec2Int p) => InBounds(p) ? (CellType)Terrain[Index(p)] : CellType.Wall;

        void BuildGoalMask()
        {
            _goalMask = new bool[Width * Height];
            for (int i = 0; i < Terrain.Length; i++) _goalMask[i] = Terrain[i] == (int)CellType.Goal;
        }

        public bool IsGoal(Vec2Int p) => InBounds(p) && _goalMask[Index(p)];

        /// <summary>胜利：所有箱子都在终点上。无箱子的关卡不构成胜利态（否则会锁死操作）。</summary>
        public bool AllBoxesOnGoal()
        {
            if (Boxes == null || Boxes.Length == 0) return false;
            for (int i = 0; i < Boxes.Length; i++)
                if (!_goalMask[Index(Boxes[i])]) return false;
            return true;
        }

        public int BoxAt(Vec2Int p)
        {
            for (int i = 0; i < Boxes.Length; i++) if (Boxes[i] == p) return i;
            return -1;
        }

        public WorldState Clone()
        {
            return new WorldState
            {
                Width = Width, Height = Height,
                Terrain = Terrain,                      // 地形不可变，共享引用
                _goalMask = _goalMask,
                Player = Player,
                HasEnemy = HasEnemy, Enemy = Enemy,
                Boxes = (Vec2Int[])Boxes.Clone(),
            };
        }

        /// <summary>世界状态指纹（ADR 09：入栈与 BFS 去重判定依据）。</summary>
        public long Hash
        {
            get
            {
                // FNV-1a 变体；boxes 已排序，顺序无关
                unchecked
                {
                    // FNV-1a 64 offset basis 超出 long.MaxValue，需显式 ulong 转换
                    long h = unchecked((long)14695981039346656037UL);
                    h = (h ^ (Player.x + 1)) * 1099511628211L;
                    h = (h ^ (Player.y + 1)) * 1099511628211L;
                    if (HasEnemy)
                    {
                        h = (h ^ (Enemy.x + 1)) * 1099511628211L;
                        h = (h ^ (Enemy.y + 1)) * 1099511628211L;
                    }
                    for (int i = 0; i < Boxes.Length; i++)
                    {
                        h = (h ^ (Boxes[i].x + 1)) * 1099511628211L;
                        h = (h ^ (Boxes[i].y + 1)) * 1099511628211L;
                    }
                    return h;
                }
            }
        }

        public void SortBoxes() => Array.Sort(Boxes, (a, b) => Index(a).CompareTo(Index(b)));
    }
}
