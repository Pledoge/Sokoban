using System.Collections.Generic;
using UnityEngine;

namespace Sokoban.Game
{
    using Core;

    /// <summary>
    /// 棋盘渲染器：把 WorldState 画成 SpriteRenderer 网格。
    /// 纹理全部程序化生成（纯色/圆角），零美术依赖 —— 美术到位后只替换 Sprite 引用。
    /// </summary>
    public class BoardRenderer : MonoBehaviour
    {
        public float cellSize = 1f;

        WorldState _state;
        readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        Dictionary<CellType, Sprite> _sprites;

        static readonly Dictionary<CellType, Color> Colors = new Dictionary<CellType, Color>
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

                CellType visual = t;
                if (box) visual = CellType.Box;
                if (isEnemy) visual = CellType.Enemy;
                if (isPlayer) visual = CellType.Player;
                if (visual == CellType.Empty && !box && !isPlayer && !isEnemy) continue; // 空地不画

                var r = GetRenderer(need++);
                r.transform.localPosition = new Vector3(origin.x + x, origin.y + y, 0);
                r.sprite = _sprites[visual];
                r.sortingOrder = (visual == CellType.Box || visual == CellType.Player || visual == CellType.Enemy) ? 1 : 0;
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
            foreach (var kv in Colors)
            {
                bool rounded = kv.Key == CellType.Player || kv.Key == CellType.Enemy
                            || kv.Key == CellType.Box || kv.Key == CellType.Goal;
                _sprites[kv.Key] = MakeSprite(kv.Value, rounded);
            }
        }

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
