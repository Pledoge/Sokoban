using System.Collections.Generic;
using UnityEngine;

namespace Sokoban.Game
{
    using Core;

    /// <summary>
    /// 棋盘渲染器：把 WorldState 画成 SpriteRenderer 网格。
    /// 素材优先从 Resources/Art/ 加载（AI 生成奶蛙暖金风）；缺失时回退程序化纯色块。
    /// 层次：地板(0) → 墙/障碍/终点标记(1) → 箱子/玩家/敌人(2)；箱子推上终点换金色皮肤。
    /// </summary>
    public class BoardRenderer : MonoBehaviour
    {
        public float cellSize = 1f;

        WorldState _state;
        readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        Dictionary<CellType, Sprite> _sprites;
        Sprite _boxOnGoal;

        static readonly Dictionary<CellType, string> ArtNames = new Dictionary<CellType, string>
        {
            { CellType.Empty,    "floor"        },   // 地板砖
            { CellType.Wall,     "wall"         },
            { CellType.Obstacle, "obstacle"     },
            { CellType.Goal,     "goal"         },
            { CellType.Box,      "box"          },
            { CellType.Player,   "player"       },
            { CellType.Enemy,    "enemy"        },
        };

        static readonly Dictionary<CellType, Color> FallbackColors = new Dictionary<CellType, Color>
        {
            { CellType.Empty,    new Color(0.97f, 0.96f, 0.90f) },   // 奶油白
            { CellType.Wall,     new Color(0.42f, 0.38f, 0.35f) },   // 灰咖
            { CellType.Obstacle, new Color(0.36f, 0.25f, 0.20f) },   // 深咖
            { CellType.Goal,     new Color(0.91f, 0.73f, 0.19f) },   // 烫金
            { CellType.Box,      new Color(0.72f, 0.50f, 0.28f) },   // 木棕
            { CellType.Player,   new Color(0.55f, 0.82f, 0.38f) },   // 奶蛙绿
            { CellType.Enemy,    new Color(0.66f, 0.31f, 0.45f) },   // 紫红
        };

        public void Bind(WorldState state, LevelData lv)
        {
            _state = state;
            EnsureSprites();
            Rebuild(lv);
        }

        /// <summary>每步后重刷动态对象位置（全量重建，格子 ≤ 900，代价可忽略）。</summary>
        public void Refresh(LevelData lv)
        {
            if (_state == null) return;
            Rebuild(lv);
        }

        void Rebuild(LevelData lv)
        {
            int w = _state.Width, h = _state.Height;
            // 以棋盘中心为原点
            Vector2 origin = new Vector2(-(w - 1) * 0.5f, -(h - 1) * 0.5f);

            int need = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vec2Int(x, y);
                var t = _state.At(p);
                bool box = _state.BoxAt(p) >= 0;
                bool isPlayer = _state.Player == p;
                bool isEnemy = _state.HasEnemy && _state.Enemy == p;

                var pos = new Vector3(origin.x + x, origin.y + y, 0);

                // —— 地板层（0）：墙/障碍之外都铺地板砖 ——
                if (t != CellType.Wall && t != CellType.Obstacle)
                {
                    var f = GetRenderer(need++);
                    f.transform.localPosition = pos;
                    f.sprite = _sprites[CellType.Empty];
                    f.sortingOrder = 0;
                }

                // —— 地形层（1）：墙 / 障碍 / 终点标记 ——
                if (t == CellType.Wall || t == CellType.Obstacle || t == CellType.Goal)
                {
                    var r = GetRenderer(need++);
                    r.transform.localPosition = pos;
                    r.sprite = _sprites[t];
                    r.sortingOrder = 1;
                }

                // —— 动态层（2）：箱子 / 玩家 / 敌人 ——
                CellType visual;
                if (isPlayer)      visual = CellType.Player;
                else if (isEnemy)  visual = CellType.Enemy;
                else if (box)      visual = CellType.Box;
                else continue;

                var d = GetRenderer(need++);
                d.transform.localPosition = pos;
                d.sprite = (visual == CellType.Box && t == CellType.Goal && _boxOnGoal != null)
                    ? _boxOnGoal : _sprites[visual];
                d.sortingOrder = 2;
            }

            for (int i = need; i < _pool.Count; i++) _pool[i].gameObject.SetActive(false);
        }

        SpriteRenderer GetRenderer(int idx)
        {
            while (_pool.Count <= idx)
            {
                var go = new GameObject("cell_" + _pool.Count);
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * cellSize;
                _pool.Add(go.AddComponent<SpriteRenderer>());
            }
            _pool[idx].gameObject.SetActive(true);
            return _pool[idx];
        }

        void EnsureSprites()
        {
            if (_sprites != null) return;
            _sprites = new Dictionary<CellType, Sprite>();
            foreach (var kv in ArtNames)
            {
                // 优先加载 AI 生成的美术素材（256px，PPU=256 → 恰好 1 格 1 单位）
                var tex = Resources.Load<Texture2D>("Art/" + kv.Value);
                _sprites[kv.Key] = tex != null
                    ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                    new Vector2(0.5f, 0.5f), tex.width)
                    : MakeSprite(FallbackColors[kv.Key], IsRounded(kv.Key));
            }
            var goldTex = Resources.Load<Texture2D>("Art/box_on_goal");
            if (goldTex != null)
                _boxOnGoal = Sprite.Create(goldTex, new Rect(0, 0, goldTex.width, goldTex.height),
                                           new Vector2(0.5f, 0.5f), goldTex.width);
        }

        static bool IsRounded(CellType t)
            => t == CellType.Player || t == CellType.Enemy || t == CellType.Box || t == CellType.Goal;

        static Sprite MakeSprite(Color c, bool rounded)
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            float r = rounded ? S * 0.28f : 0f;
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                bool inside = true;
                if (r > 0)
                {
                    float cx = Mathf.Min(x, S - 1 - x), cy = Mathf.Min(y, S - 1 - y);
                    if (cx < r && cy < r)
                    {
                        float dx = r - cx, dy = r - cy;
                        inside = dx * dx + dy * dy <= r * r;
                    }
                }
                tex.SetPixel(x, y, inside ? c : Color.clear);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        }
    }
}
