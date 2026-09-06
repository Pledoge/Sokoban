using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    using Core;
    using EditorCore;
    using Solver;

    /// <summary>
    /// 关卡编辑器 · 形态 A（Editor 插件壳，见 02-TDD §5.0）。
    /// 只负责绘制 + 输入 + 文件 IO；逻辑全在 LevelEditorCore。
    /// 菜单：Window / 推箱子 / 关卡编辑器
    /// </summary>
    public class LevelEditorWindow : EditorWindow
    {
        // —— 配色（ArtBible 暖金）——
        static readonly Color C_GridBg     = new Color(0.91f, 0.85f, 0.71f);
        static readonly Color C_GridLine   = new Color(0, 0, 0, 0.10f);
        static readonly Color C_Hover      = new Color(0.91f, 0.73f, 0.19f, 0.45f);   // 悬停高亮（半透明金）
        static readonly Color C_HoverEdge  = new Color(0.91f, 0.73f, 0.19f, 1f);      // 悬停边框
        static readonly Color C_GridOuter  = new Color(0.36f, 0.36f, 0.40f);          // 棋盘外边框

        LevelEditorCore _core;
        Vector2 _scrollRight;
        Vector2 _scrollGrid;
        float _cell = 34f;
        string _statusMsg = "就绪";
        MessageType _statusType = MessageType.Info;
        SolveResult _lastSolve;
        string[] _modeNames = { "经典模式", "拓展模式" };

        // 选中状态（悬停格 + 工具高亮）
        int _hoverX = -1, _hoverY = -1;
        bool _hasHover;

        static readonly EditorTool[] Toolbar =
        {
            EditorTool.Wall, EditorTool.Player, EditorTool.Enemy,
            EditorTool.Box, EditorTool.Goal, EditorTool.Obstacle, EditorTool.Erase
        };
        static readonly string[] ToolNames = { "墙", "玩家", "敌人", "箱", "终点", "障碍", "橡皮" };
        // 每个工具对应的图标字符（替代美术 icon，简单明了）
        static readonly string[] ToolGlyphs = { "■", "♟", "♞", "□", "★", "▲", "✕" };
        // 每个工具对应的色块（与运行时编辑器一致）
        static readonly Color[] ToolSwatch =
        {
            new Color(0.36f, 0.36f, 0.40f),  // 墙
            new Color(0.50f, 0.78f, 0.35f),  // 玩家
            new Color(0.64f, 0.29f, 0.43f),  // 敌人
            new Color(0.65f, 0.44f, 0.26f),  // 箱
            new Color(0.91f, 0.73f, 0.19f),  // 终点
            new Color(0.36f, 0.25f, 0.20f),  // 障碍
            new Color(0.85f, 0.81f, 0.74f),  // 橡皮
        };

        [MenuItem("Window/推箱子/关卡编辑器")]
        public static void Open()
        {
            var w = GetWindow<LevelEditorWindow>("推箱子关卡编辑器");
            w.minSize = new Vector2(900, 560);
        }

        void OnEnable()
        {
            if (_core == null)
                _core = new LevelEditorCore(Mode.Classic);
        }

        void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            DrawGrid();
            EditorGUILayout.Space(8);
            DrawSidePanel();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(_statusMsg, _statusType);
        }

        // ------------------------------------------------------------ 顶栏
        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("模式", GUILayout.Width(34));
            int modeIdx = EditorGUILayout.Popup((int)_core.Data.mode, _modeNames, EditorStyles.toolbarPopup, GUILayout.Width(90));
            if (modeIdx != (int)_core.Data.mode)
            {
                _core.Data.mode = (Mode)modeIdx;
                if (modeIdx == 0)   // 切回经典 → 清掉敌人
                    for (int i = 0; i < _core.Data.cells.Length; i++)
                        if (_core.Data.cells[i] == (int)CellType.Enemy) _core.Data.cells[i] = 0;
            }

            GUILayout.Space(10);
            GUILayout.Label("关卡ID", GUILayout.Width(44));
            _core.Data.id = GUILayout.TextField(_core.Data.id, GUILayout.Width(110));
            GUILayout.Label("名称", GUILayout.Width(30));
            _core.Data.name = GUILayout.TextField(_core.Data.name, GUILayout.Width(120));

            GUILayout.Space(10);
            GUILayout.Label($"格子 {_cell:F0}", GUILayout.Width(64));
            _cell = GUILayout.HorizontalSlider(_cell, 18f, 56f, GUILayout.Width(120));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("撤销编辑", EditorStyles.toolbarButton)) { _core.UndoEdit(); Repaint(); }
            if (GUILayout.Button("重做", EditorStyles.toolbarButton)) { _core.RedoEdit(); Repaint(); }
            if (GUILayout.Button("新建", EditorStyles.toolbarButton))
                _core = new LevelEditorCore(_core.Data.mode, _core.Data.width, _core.Data.height);

            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------ 网格
        void DrawGrid()
        {
            var lv = _core.Data;
            float gw = lv.width * _cell, gh = lv.height * _cell;

            _scrollGrid = EditorGUILayout.BeginScrollView(_scrollGrid, GUILayout.MaxWidth(760), GUILayout.ExpandHeight(true));
            var origin = GUILayoutUtility.GetRect(gw, gh);
            HandleGridInput(origin);

            // 棋盘底色
            EditorGUI.DrawRect(origin, C_GridBg);

            // 单元格
            for (int y = 0; y < lv.height; y++)
            for (int x = 0; x < lv.width; x++)
            {
                // 渲染采用 y 向上（与游戏一致）：数据行 y=0 画在底部，屏幕 y 用 (height-1-y)。
                var rect = new Rect(origin.x + x * _cell, origin.y + (lv.height - 1 - y) * _cell, _cell + 1, _cell + 1);
                EditorGUI.DrawRect(rect, CellColor(lv.GetCell(x, y)));
            }

            // 网格线（半透黑细线）
            var lineColor = C_GridLine;
            for (int x = 1; x < lv.width; x++)
            {
                var lx = origin.x + x * _cell;
                EditorGUI.DrawRect(new Rect(lx - 0.5f, origin.y, 1, gh), lineColor);
            }
            for (int y = 1; y < lv.height; y++)
            {
                var ly = origin.y + y * _cell;
                EditorGUI.DrawRect(new Rect(origin.x, ly - 0.5f, gw, 1), lineColor);
            }
            // 棋盘外边框
            EditorGUI.DrawRect(new Rect(origin.x - 1, origin.y - 1, gw + 2, 2), C_GridOuter);                    // top
            EditorGUI.DrawRect(new Rect(origin.x - 1, origin.y + gh - 1, gw + 2, 2), C_GridOuter);               // bottom
            EditorGUI.DrawRect(new Rect(origin.x - 1, origin.y - 1, 2, gh + 2), C_GridOuter);                    // left
            EditorGUI.DrawRect(new Rect(origin.x + gw - 1, origin.y - 1, 2, gh + 2), C_GridOuter);               // right

            // 悬停高亮
            if (_hasHover && _hoverX >= 0 && _hoverY >= 0 && _hoverX < lv.width && _hoverY < lv.height)
            {
                var r = new Rect(origin.x + _hoverX * _cell, origin.y + (lv.height - 1 - _hoverY) * _cell, _cell, _cell);
                EditorGUI.DrawRect(r, C_Hover);
                // 4 边描深
                var edge = C_HoverEdge;
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2), edge);
                EditorGUI.DrawRect(new Rect(r.x, r.y + r.height - 2, r.width, 2), edge);
                EditorGUI.DrawRect(new Rect(r.x, r.y, 2, r.height), edge);
                EditorGUI.DrawRect(new Rect(r.x + r.width - 2, r.y, 2, r.height), edge);
            }

            EditorGUILayout.EndScrollView();
        }

        void HandleGridInput(Rect origin)
        {
            var e = Event.current;
            var lv = _core.Data;

            // 屏幕 y 向下、数据 y 向上：把鼠标行翻转为数据行（与渲染一致）
            int ToDataY(float my) => lv.height - 1 - Mathf.FloorToInt((my - origin.y) / _cell);

            // 1) 持续更新悬停状态
            if (origin.Contains(e.mousePosition))
            {
                int x = Mathf.FloorToInt((e.mousePosition.x - origin.x) / _cell);
                int y = ToDataY(e.mousePosition.y);
                if (x != _hoverX || y != _hoverY) { _hoverX = x; _hoverY = y; _hasHover = true; Repaint(); }
            }
            else if (_hasHover) { _hasHover = false; Repaint(); }

            // 2) 鼠标按下：落笔
            if (e.type != EventType.MouseDown || !origin.Contains(e.mousePosition)) return;
            int cx = Mathf.FloorToInt((e.mousePosition.x - origin.x) / _cell);
            int cy = ToDataY(e.mousePosition.y);

            var tool = e.button == 1 ? EditorTool.Erase : _core.CurrentTool;
            var saved = _core.CurrentTool;
            _core.CurrentTool = tool;
            bool ok = _core.TryPaint(cx, cy);
            _core.CurrentTool = saved;

            if (!ok) SetStatus($"({cx},{cy}) 不允许放置 {ToolNames[(int)tool]}", MessageType.Warning);
            else SetStatus($"已放置 {ToolNames[(int)tool]} @ ({cx},{cy})", MessageType.Info);
            Repaint();
            e.Use();
        }

        static Color CellColor(CellType c) => c switch
        {
            CellType.Wall => new Color(0.35f, 0.35f, 0.38f),
            CellType.Obstacle => new Color(0.36f, 0.25f, 0.2f),
            CellType.Goal => new Color(0.91f, 0.73f, 0.19f),
            CellType.Box => new Color(0.65f, 0.44f, 0.26f),
            CellType.Player => new Color(0.5f, 0.78f, 0.35f),
            CellType.Enemy => new Color(0.64f, 0.29f, 0.43f),
            _ => new Color(0.97f, 0.96f, 0.93f),
        };

        // ------------------------------------------------------------ 右栏
        void DrawSidePanel()
        {
            _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight, GUILayout.Width(230), GUILayout.ExpandHeight(true));

            GUILayout.Label("工具", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            // 工具按钮 2 行排布（4+3），带色块 + 图标 + 名称，选中态高亮
            for (int i = 0; i < Toolbar.Length; i++)
            {
                bool sel = _core.CurrentTool == Toolbar[i];
                var rect = GUILayoutUtility.GetRect(50, 50, GUILayout.Width(50));
                // 底色（按工具颜色）
                EditorGUI.DrawRect(rect, ToolSwatch[i]);
                // 图标
                var icon = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                var prev = GUI.contentColor;
                GUI.contentColor = i switch { 0 => Color.white, 5 => Color.white, _ => new Color(0.25f, 0.16f, 0.01f) };
                GUI.Label(rect, ToolGlyphs[i], icon);
                GUI.contentColor = prev;
                // 选中态：金色边框
                if (sel)
                {
                    var edge = new Color(0.91f, 0.73f, 0.19f);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), edge);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - 2, rect.width, 2), edge);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), edge);
                    EditorGUI.DrawRect(new Rect(rect.x + rect.width - 2, rect.y, 2, rect.height), edge);
                }
                if (GUI.Button(rect, "", GUIStyle.none)) { _core.CurrentTool = Toolbar[i]; Repaint(); }
            }
            EditorGUILayout.EndHorizontal();

            // 工具名
            EditorGUILayout.LabelField($"当前：{ToolNames[(int)_core.CurrentTool]}", EditorStyles.miniLabel);
            EditorGUILayout.Space(6);

            GUILayout.Label("操作", EditorStyles.boldLabel);
            if (GUILayout.Button("校验关卡")) RunValidate();
            if (GUILayout.Button("检测是否有解")) RunSolve();
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存")) Save();
            if (GUILayout.Button("加载…")) Load();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("打开 Assets/Resources/Levels 目录")) EditorUtility.RevealInFinder("Assets/Resources/Levels");

            EditorGUILayout.Space(8);
            GUILayout.Label("网格尺寸", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("宽", GUILayout.Width(20));
            int nw = EditorGUILayout.IntField(_core.Data.width, GUILayout.Width(50));
            if (GUILayout.Button("+", GUILayout.Width(26))) nw++;
            if (GUILayout.Button("−", GUILayout.Width(26))) nw--;
            GUILayout.Label("高", GUILayout.Width(20));
            int nh = EditorGUILayout.IntField(_core.Data.height, GUILayout.Width(50));
            if (GUILayout.Button("+", GUILayout.Width(26))) nh++;
            if (GUILayout.Button("−", GUILayout.Width(26))) nh--;
            EditorGUILayout.EndHorizontal();
            if (nw != _core.Data.width || nh != _core.Data.height)
            {
                nw = Mathf.Clamp(nw, 6, 30);
                nh = Mathf.Clamp(nh, 6, 30);
                Resize(nw, nh);
            }

            EditorGUILayout.Space(8);
            if (_lastSolve != null)
            {
                GUILayout.Label("上次求解结果", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    $"{_lastSolve.status}\n步数: {_lastSolve.steps}  耗时: {_lastSolve.elapsedMs}ms\n状态数: {_lastSolve.visitedStates}",
                    _lastSolve.status == SolveStatus.Solvable ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        void Resize(int nw, int nh)
        {
            var lv = _core.Data;
            var old = lv.cells;
            int ow = lv.width, oh = lv.height;
            var newCells = new int[nw * nh];
            for (int y = 0; y < Mathf.Min(oh, nh); y++)
            for (int x = 0; x < Mathf.Min(ow, nw); x++)
                newCells[y * nw + x] = old[y * ow + x];
            lv.width = nw; lv.height = nh; lv.cells = newCells;
        }

        // ------------------------------------------------------------ 动作
        void RunValidate()
        {
            var r = _core.Validate();
            if (r.Ok)
                SetStatus(r.Warnings.Count > 0 ? "校验通过（有警告）: " + string.Join("; ", r.Warnings) : "校验通过", MessageType.Info);
            else
                SetStatus("校验失败: " + string.Join("; ", r.Errors), MessageType.Error);
        }

        void RunSolve()
        {
            var r = _core.Validate();
            if (!r.Ok) { SetStatus("先修完校验错误再求解: " + string.Join("; ", r.Errors), MessageType.Error); return; }

            _lastSolve = SolverRouter.Solve(_core.Data, 5000);
            switch (_lastSolve.status)
            {
                case SolveStatus.Solvable:
                    SetStatus($"有解！最短 {_lastSolve.steps} 步（{_lastSolve.elapsedMs}ms, 探索 {_lastSolve.visitedStates} 状态）", MessageType.Info);
                    break;
                case SolveStatus.Unsolvable:
                    SetStatus($"无解（{_lastSolve.elapsedMs}ms）", MessageType.Error);
                    break;
                default:
                    SetStatus($"超时 —— 解空间过大，试着缩小棋盘或减少箱子", MessageType.Warning);
                    break;
            }
            Repaint();
        }

        void Save()
        {
            var r = _core.Validate();
            if (!r.Ok) { SetStatus("校验失败，禁止保存: " + string.Join("; ", r.Errors), MessageType.Error); return; }

            var dir = Path.Combine(Application.dataPath, "Resources/Levels");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, _core.Data.id + ".json"), _core.ToJson());
            AssetDatabase.Refresh();
            SetStatus($"已保存 Assets/Resources/Levels/{_core.Data.id}.json", MessageType.Info);
        }

        void Load()
        {
            var path = EditorUtility.OpenFilePanel("加载关卡", "Assets/Resources/Levels", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                _core.LoadFromJson(File.ReadAllText(path));
                _lastSolve = null;
                SetStatus($"已加载 {Path.GetFileName(path)}", MessageType.Info);
                Repaint();
            }
            catch (System.Exception ex) { SetStatus("加载失败: " + ex.Message, MessageType.Error); }
        }

        void SetStatus(string msg, MessageType type) { _statusMsg = msg; _statusType = type; }
    }
}
