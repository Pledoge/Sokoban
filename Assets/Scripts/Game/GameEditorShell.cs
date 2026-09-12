using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Sokoban.Game
{
    using Core;
    using EditorCore;

    /// <summary>
    /// 运行时关卡编辑器壳（游戏内编辑器，见 GDD §4.3 / ADR 10）：
    /// 共用 <see cref="LevelEditorCore"/> 纯 C# 逻辑，绘制 + 输入 + 文件 IO 全部在本类。
    /// 由 <see cref="UIRouter"/> 在切到 LevelEditor 页时调用 <see cref="Attach"/>；
    /// 离开页面时 <see cref="Detach"/> 清理。
    /// </summary>
    public class GameEditorShell : MonoBehaviour
    {
        const float CellSize = 64f;        // 单格逻辑尺寸（grid 容器 640 宽，16 宽关卡刚好 64）
        const float CellGap = 2f;           // 格间距

        LevelEditorCore _core = new LevelEditorCore(Mode.Classic);
        RectTransform _grid;
        Transform[] _toolBtns;
        Text _status, _sizeLabel;
        InputField _nameInput;             // 关卡名称输入框（保存时写入 Data.name）
        Image[,] _cells;                   // 行优先：[y, x]
        Button[] _modeBtns;
        int _toolBtnCount;

        // —— 按住拖动连续放置 ——
        bool _dragSetup;                                       // Update 委托已挂
        int _hoverX = -1, _hoverY = -1;
        readonly HashSet<long> _strokeCells = new HashSet<long>();   // 本笔划已涂过的格子（每格每笔只变一次）

        bool _initialized;

        /// <summary>由 UIRouter.Show(Page.LevelEditor) 调用，确保面板就绪。</summary>
        public void Attach()
        {
            if (_initialized) { RefreshAll(); return; }
            _initialized = true;

            _core = new LevelEditorCore(Mode.Classic);
            _grid = DeepFind(transform, "GridContainer") as RectTransform;

            // 7 个工具按钮 + 名称映射
            var map = new System.Collections.Generic.List<(EditorTool, string, string)>
            {
                (EditorTool.Wall,      "ToolWall",      "墙"),
                (EditorTool.Player,    "ToolPlayer",    "玩家"),
                (EditorTool.Enemy,     "ToolEnemy",     "敌人"),
                (EditorTool.Box,       "ToolBox",       "箱子"),
                (EditorTool.Goal,      "ToolGoal",      "终点"),
                (EditorTool.Obstacle,  "ToolObstacle",  "障碍"),
                (EditorTool.Erase,     "ToolErase",     "橡皮"),
            };
            _toolBtns = new Transform[map.Count];
            for (int i = 0; i < map.Count; i++)
            {
                var t = DeepFind(transform, map[i].Item2);
                _toolBtns[i] = t;
                var tool = map[i].Item1;
                var btn = t.GetComponent<Button>();
                if (btn != null)
                {
                    var capturedTool = tool;
                    btn.onClick.AddListener(() => SelectTool(capturedTool));
                }
            }
            _toolBtnCount = map.Count;

            _status = DeepText(transform, "StatusText");
            _sizeLabel = DeepText(transform, "SizeLabel");

            // 模式切换
            _modeBtns = new[]
            {
                DeepFind(transform, "BtnModeClassic")?.GetComponent<Button>(),
                DeepFind(transform, "BtnModeExtended")?.GetComponent<Button>(),
            };
            if (_modeBtns[0] != null) _modeBtns[0].onClick.AddListener(() => SetMode(Mode.Classic));
            if (_modeBtns[1] != null) _modeBtns[1].onClick.AddListener(() => SetMode(Mode.Extended));

            // 操作按钮
            BindBtn("BtnUndo",     DoUndo);
            BindBtn("BtnValidate", DoValidate);
            BindBtn("BtnSave",     DoSave);
            BindBtn("BtnNew",      DoNew);
            BindBtn("BtnWidth",    () => { Resize(+1, 0); });
            BindBtn("BtnHeight",   () => { Resize(0, +1); });
            BindBtn("BtnBack",     () => Sokoban.Game.UIRouter.I.Show(UIRouter.Page.MainMenu));
            BindNameInput();
            BindPlayButton();

            RebuildGrid();
            RefreshToolHighlight();
            UpdateStatus("就绪");
        }

        public void Detach() { /* 隐藏即可 */ }

        // ------------------------------------------------------- 按住拖动连续放置
        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _strokeCells.Clear();                          // 新的一笔
                TryPaintCell(_hoverX, _hoverY);                // 原地按下也立即放置
            }
            else if (Input.GetMouseButtonUp(0))
            {
                _strokeCells.Clear();
            }
        }

        /// <summary>格子指针进入（由 CellDrag 回调）：按住左键时连续放置，本笔划内每格只变一次。</summary>
        public void OnCellHover(int x, int y)
        {
            _hoverX = x; _hoverY = y;
            if (Input.GetMouseButton(0)) TryPaintCell(x, y);
        }

        /// <summary>指针离开格子：清除 hover，防止随后点工具按钮时误在旧格子落笔。</summary>
        public void OnCellLeave(int x, int y)
        {
            if (_hoverX == x && _hoverY == y) { _hoverX = -1; _hoverY = -1; }
        }

        void TryPaintCell(int x, int y)
        {
            if (x < 0 || y < 0 || _cells == null
                || y >= _cells.GetLength(0) || x >= _cells.GetLength(1)) return;
            long key = (long)y * _core.Data.width + x;
            if (!_strokeCells.Add(key)) return;                // 本笔已涂过
            if (_core.TryPaint(x, y))
            {
                // 玩家/敌人是全局唯一对象：放置会清掉旧位置的数据，需整盘刷新颜色（否则旧格残留绿色）
                if (_core.CurrentTool == EditorTool.Player || _core.CurrentTool == EditorTool.Enemy)
                    RefreshAll();
                else
                    RefreshCell(x, y);
                UpdateStatus("已放置 " + ToolName(_core.CurrentTool));
            }
            else
                UpdateStatus($"({x},{y}) 拒绝：不允许该放置");
        }

        /// <summary>挂在每个格子上的指针进入/离开转发器（运行时 AddComponent，不入 prefab）。</summary>
        class CellDrag : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public int x, y;
            public GameEditorShell shell;
            public void OnPointerEnter(PointerEventData e) { if (shell != null) shell.OnCellHover(x, y); }
            public void OnPointerExit(PointerEventData e)  { if (shell != null) shell.OnCellLeave(x, y); }
        }

        // ------------------------------------------------------- 名称输入框 / 试玩按钮（prefab 预置节点，按名字绑定）
        // 两个控件已固化为 LevelEditor.prefab 内的节点（Header 下），位置可在编辑器手动调整；
        // 运行时只做查找绑定，绝不改它们的 transform。

        /// <summary>绑定 prefab 内的 LE_Name 输入框，保存/试玩时写入 Data.name。</summary>
        void BindNameInput()
        {
            _nameInput = DeepFind(transform, "LE_Name")?.GetComponent<InputField>();
            if (_nameInput == null)
                Debug.LogWarning("[GameEditorShell] LevelEditor prefab 缺少 LE_Name（InputField），关卡名将退回默认");
        }

        /// <summary>绑定 prefab 内的 BtnPlay → 试玩。</summary>
        void BindPlayButton()
        {
            var t = DeepFind(transform, "BtnPlay");
            if (t == null) { Debug.LogWarning("[GameEditorShell] LevelEditor prefab 缺少 BtnPlay，试玩不可用"); return; }
            var b = t.GetComponent<Button>();
            if (b != null) b.onClick.AddListener(DoPlayTest);
        }

        /// <summary>把输入框内容写进 Data.name；id 为默认值时派生一个稳定 id（ASCII 名直接用，否则时间戳）。</summary>
        void ApplyNameToData()
        {
            var name = _nameInput != null ? _nameInput.text.Trim() : "";
            if (string.IsNullOrEmpty(_core.Data.name) || _core.Data.name == "未命名关卡")
                _core.Data.name = string.IsNullOrEmpty(name) ? "未命名关卡" : name;
            else if (!string.IsNullOrEmpty(name))
                _core.Data.name = name;

            if (string.IsNullOrEmpty(_core.Data.id) || _core.Data.id == "custom_00")
            {
                var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_]", "");
                _core.Data.id = sanitized.Length > 0
                    ? "custom_" + sanitized.ToLower()
                    : "custom_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }
        }

        void DoPlayTest()
        {
            var r = _core.Validate();
            if (!r.Ok) { UpdateStatus("校验失败，无法试玩：" + string.Join("; ", r.Errors)); return; }
            ApplyNameToData();                     // 试玩 HUD 显示当前输入的关卡名
            var data = _core.Data.DeepClone();
            Sokoban.Game.UIRouter.I.PlayLevel(data);
        }

        void BindBtn(string name, System.Action a)
        {
            var t = DeepFind(transform, name);
            if (t == null) return;
            var b = t.GetComponent<Button>();
            if (b != null) b.onClick.AddListener(() => a());
        }

        // ------------------------------------------------------- 工具选择
        void SelectTool(EditorTool tool)
        {
            _core.CurrentTool = tool;
            RefreshToolHighlight();
            UpdateStatus("当前工具：" + ToolName(tool));
        }

        void RefreshToolHighlight()
        {
            for (int i = 0; i < _toolBtnCount; i++)
            {
                var btn = _toolBtns[i];
                if (btn == null) continue;
                var img = btn.GetComponent<Image>();
                if (img == null) continue;
                img.color = i == (int)_core.CurrentTool ? Color.white : new Color(1, 1, 1, 0.78f);
            }
        }

        static string ToolName(EditorTool t)
        {
            switch (t)
            {
                case EditorTool.Wall: return "墙";
                case EditorTool.Player: return "玩家";
                case EditorTool.Enemy: return "敌人";
                case EditorTool.Box: return "箱子";
                case EditorTool.Goal: return "终点";
                case EditorTool.Obstacle: return "障碍";
                case EditorTool.Erase: return "橡皮";
            }
            return "?";
        }

        // ------------------------------------------------------- 网格
        void RebuildGrid()
        {
            if (_grid == null) return;

            for (int i = _grid.childCount - 1; i >= 0; i--)
                DestroyImmediate(_grid.GetChild(i).gameObject);

            var w = _core.Data.width;
            var h = _core.Data.height;
            _cells = new Image[h, w];
            float cellSize = CellSize;
            // 若关卡尺寸超过容器 640，按比例缩小
            float fit = Mathf.Min(640f / (w * cellSize + (w - 1) * CellGap),
                                  640f / (h * cellSize + (h - 1) * CellGap));
            float cs = cellSize * fit;
            float totalW = w * cs + (w - 1) * CellGap;
            float totalH = h * cs + (h - 1) * CellGap;
            // grid 容器大小自适应
            _grid.sizeDelta = new Vector2(totalW, totalH);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var go = new GameObject("c_" + x + "_" + y, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(_grid, false);
                rt.sizeDelta = new Vector2(cs, cs);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                // 渲染采用 y 向上（与游戏 BoardRenderer 一致）：y=0 在底部，y 增大向上。
                // 这样编辑器预览与游玩方向完全一致（修复导出关卡上下翻转的问题）。
                rt.anchoredPosition = new Vector2(x * (cs + CellGap) - totalW / 2 + cs / 2,
                                                   y * (cs + CellGap) - totalH / 2 + cs / 2);
                var img = go.GetComponent<Image>();
                img.color = CellColor(_core.Data.GetCell(x, y));
                img.raycastTarget = true;
                _cells[y, x] = img;

                int cx = x, cy = y;
                var btn = go.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => OnCellClick(cx, cy));

                // 拖动连刷：指针进入回调（OnCellClick 只处理单击落笔，滑动由 OnCellHover 接管）
                var drag = go.AddComponent<CellDrag>();
                drag.x = cx; drag.y = cy; drag.shell = this;
            }

            UpdateSizeLabel();
        }

        void OnCellClick(int x, int y)
        {
            _hoverX = x; _hoverY = y;
            TryPaintCell(x, y);                    // 单击与拖动统一走去重路径，避免同格重复绘制
        }

        void RefreshCell(int x, int y)
        {
            if (_cells == null) return;
            if (y < 0 || y >= _cells.GetLength(0) || x < 0 || x >= _cells.GetLength(1)) return;
            _cells[y, x].color = CellColor(_core.Data.GetCell(x, y));
        }

        void RefreshAll()
        {
            if (_cells == null) return;
            var w = _core.Data.width;
            var h = _core.Data.height;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                _cells[y, x].color = CellColor(_core.Data.GetCell(x, y));
        }

        static Color CellColor(CellType c) => c switch
        {
            CellType.Wall => new Color(0.36f, 0.36f, 0.40f),
            CellType.Obstacle => new Color(0.36f, 0.25f, 0.20f),
            CellType.Goal => new Color(0.91f, 0.73f, 0.19f),
            CellType.Box => new Color(0.65f, 0.44f, 0.26f),
            CellType.Player => new Color(0.50f, 0.78f, 0.35f),
            CellType.Enemy => new Color(0.64f, 0.29f, 0.43f),
            _ => new Color(0.97f, 0.96f, 0.93f),
        };

        // ------------------------------------------------------- 动作
        void SetMode(Mode mode)
        {
            _core.Data.mode = mode;
            if (mode == Mode.Classic)
            {
                // 清敌人必须「格子 + 出生点」一起清：
                // 只清格子的话 enemySpawn 会残留，存出来的经典关卡仍带着敌人出生点。
                for (int i = 0; i < _core.Data.cells.Length; i++)
                    if (_core.Data.cells[i] == (int)CellType.Enemy) _core.Data.cells[i] = 0;
                _core.Data.enemySpawn = new Vec2Int(-99, -99);
            }
            RefreshAll();
            UpdateStatus("模式：" + (mode == Mode.Extended ? "拓展（邪恶推箱人）" : "经典"));
        }

        void DoUndo() { _core.UndoEdit(); RefreshAll(); UpdateStatus("撤销成功"); }
        void DoValidate()
        {
            var r = _core.Validate();
            if (!r.Ok)
            {
                UpdateStatus("校验失败：" + string.Join("; ", r.Errors));
                return;
            }

            // 结构合法后再做真·有解性检测：复用运行时同一套 SokobanRules.Step 的正向 BFS（障碍=不可通行，语义与游玩一致）
            var solve = Sokoban.Solver.SolverRouter.Solve(_core.Data, 3000);
            var warn = r.Warnings.Count > 0 ? "（" + string.Join("; ", r.Warnings) + "）" : "";
            switch (solve.status)
            {
                case Sokoban.Solver.SolveStatus.Solvable:
                    UpdateStatus($"校验通过{warn}，且有解（约 {solve.steps} 步）");
                    break;
                case Sokoban.Solver.SolveStatus.Unsolvable:
                    UpdateStatus("校验通过，但【无解】—— 请调整箱子/终点/障碍布局（含障碍阻挡判断）");
                    break;
                default:
                    UpdateStatus("校验通过" + warn + "；求解超时（关卡较大，暂不能确定是否有解）");
                    break;
            }
        }
        void DoSave()
        {
            var r = _core.Validate();
            if (!r.Ok) { UpdateStatus("校验失败，禁止保存：" + string.Join("; ", r.Errors)); return; }
            ApplyNameToData();
            _core.Data.source = "custom";          // 编辑器保存的关卡标记为「自制」，选关页与导入关卡分开
            var dir = Path.Combine(Application.dataPath, "Resources/Levels");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, _core.Data.id + ".json");
            File.WriteAllText(path, _core.ToJson());
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
            UpdateStatus($"已保存 {_core.Data.id}.json（{_core.Data.name}）");
        }
        void DoNew()
        {
            _core = new LevelEditorCore(_core.Data.mode, 16, 12);
            RebuildGrid();
            UpdateStatus("已新建 16×12 关卡");
        }
        void Resize(int dw, int dh)
        {
            var lv = _core.Data;
            int nw = Mathf.Clamp(lv.width + dw, 6, 30);
            int nh = Mathf.Clamp(lv.height + dh, 6, 30);
            if (nw == lv.width && nh == lv.height) return;
            var old = lv.cells;
            int ow = lv.width, oh = lv.height;
            var newCells = new int[nw * nh];
            for (int y = 0; y < Mathf.Min(oh, nh); y++)
            for (int x = 0; x < Mathf.Min(ow, nw); x++)
                newCells[y * nw + x] = old[y * ow + x];
            lv.width = nw; lv.height = nh; lv.cells = newCells;
            RebuildGrid();
            UpdateSizeLabel();
        }
        void UpdateSizeLabel()
        {
            if (_sizeLabel != null) _sizeLabel.text = $"尺寸 {_core.Data.width} × {_core.Data.height}";
        }
        void UpdateStatus(string msg) { if (_status != null) _status.text = msg; }

        // ------------------------------------------------------- 查找
        static Transform DeepFind(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var r = DeepFind(parent.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
        static Text DeepText(Transform parent, string name)
        {
            var t = DeepFind(parent, name);
            return t != null ? t.GetComponent<Text>() : null;
        }
    }
}