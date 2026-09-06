namespace Sokoban.Game
{
    using Core;

    /// <summary>经典模式：无敌人，纯玩家推箱。</summary>
    public class ClassicMode : GameModeBase
    {
        public ClassicMode(LevelData level) : base(level) { }
        // 全部逻辑在基类 —— SokobanRules.Step 内部对无敌人世界自动跳过敌人
    }

    /// <summary>拓展模式：邪恶推箱人镜像移动（规则同样在 SokobanRules，不在此处）。</summary>
    public class ExtendedMode : GameModeBase
    {
        public ExtendedMode(LevelData level) : base(level) { }
    }

    /// <summary>模式工厂。</summary>
    public static class GameModeFactory
    {
        public static IGameMode Create(LevelData level)
        {
            return level.mode == Mode.Extended
                ? (IGameMode)new ExtendedMode(level)
                : new ClassicMode(level);
        }
    }
}
