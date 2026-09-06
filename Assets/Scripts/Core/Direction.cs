namespace Sokoban.Core
{
    /// <summary>四方向。Delta 与 Opposite 是唯一事实来源（见 02-TDD §2.5）。</summary>
    public enum Direction : byte { Up = 0, Down = 1, Left = 2, Right = 3 }

    public static class DirectionOp
    {
        /// <summary>y 向上：Up=(0,1)。</summary>
        public static readonly Vec2Int[] Delta =
        {
            new Vec2Int(0, 1),   // Up
            new Vec2Int(0, -1),  // Down
            new Vec2Int(-1, 0),  // Left
            new Vec2Int(1, 0),   // Right
        };

        /// <summary>邪恶推箱人的镜像映射。</summary>
        public static readonly Direction[] Opposite =
        {
            Direction.Down, Direction.Up, Direction.Right, Direction.Left
        };

        public static Direction OppositeOf(Direction d) => Opposite[(int)d];
    }
}
