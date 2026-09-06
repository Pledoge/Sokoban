using System;

namespace Sokoban.Core
{
    /// <summary>撤销栈元素：该步执行前的全量动态状态（ADR 02：全量快照）。</summary>
    public class MoveSnapshot
    {
        public Vec2Int playerBefore;
        public bool hasEnemy;
        public Vec2Int enemyBefore;
        public Vec2Int[] boxesBefore;    // 全量拷贝
        public Direction playerInput;    // 玩家原始输入，调试/回放用
        public long hashBefore;          // 世界状态指纹（入栈判定依据）

        public MoveSnapshot(WorldState s, Direction input)
        {
            playerBefore = s.Player;
            hasEnemy = s.HasEnemy;
            enemyBefore = s.Enemy;
            boxesBefore = (Vec2Int[])s.Boxes.Clone();
            playerInput = input;
            hashBefore = s.Hash;
        }
    }
}
