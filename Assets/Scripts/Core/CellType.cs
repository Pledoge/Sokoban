using System;

namespace Sokoban.Core
{
    /// <summary>静态地形与编辑器放置语义的单元格编码（见 02-TDD §2.1）。</summary>
    public enum CellType : int
    {
        Empty = 0,
        Wall = 1,
        Obstacle = 2,
        Goal = 3,
        // —— 以下仅用于「编辑器放置 / 关卡文件存盘」语义，加载进 WorldState 时会被分离 ——
        Box = 4,
        BoxOnGoal = 5,
        Player = 6,
        Enemy = 8,
    }

    /// <summary>整型格坐标。约定：y 向上（数学惯例），渲染层负责屏幕映射。</summary>
    [Serializable]
    public struct Vec2Int : IEquatable<Vec2Int>
    {
        public int x;
        public int y;

        public Vec2Int(int x, int y) { this.x = x; this.y = y; }

        public static Vec2Int operator +(Vec2Int a, Vec2Int b) => new Vec2Int(a.x + b.x, a.y + b.y);
        public static bool operator ==(Vec2Int a, Vec2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vec2Int a, Vec2Int b) => !(a == b);

        public bool Equals(Vec2Int other) => this == other;
        public override bool Equals(object obj) => obj is Vec2Int v && this == v;
        public override int GetHashCode() => (x << 16) ^ y;
        public override string ToString() => $"({x},{y})";
    }
}
