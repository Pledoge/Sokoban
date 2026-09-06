using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Game
{
    using Core;

    /// <summary>
    /// 界面路由（见 02-TDD §5.1）：
    /// - 五个页面（主菜单 / 选关 / 游戏中 / 暂停 / 结算）各自独占一个 Canvas 根，**同屏只激活一个**；
    /// - 按钮 onClick 全部在此按名字查找绑定（不挂 prefab —— 重导入不丢，TDD 硬性规则）；
    /// - 选关页按钮为静态槽位（LevelBtn_0..4），由本类按内置关卡动态填充文本与可见性 —— 加关卡不改 UI；
    /// - HUD / 结算 / 选关文本由本类统一刷新。
    /// </summary>
    public class UIRouter : MonoBehaviour
    {
        public enum Page { MainMenu, LevelSelect, Playing, Pause, Win, LevelEditor }

        public static UIRouter I { get; private set; }

        /// <summary>当前激活页面（供输入层判断 Esc 行为等）。</summary>
        public Page CurrentPage { get; private set; } = Page.MainMenu;

        readonly Dictionary<Page, GameObject> _pages = new Dictionary<Page, GameObject>();
        GameBootstrap _game;

        Text _hudLevel, _hudSteps, _winStats;
        Text _selModeTag;

        // —— 选关：分页 + 搜索 ——
        readonly List<Button> _lsBtns = new List<Button>();
        readonly List<Text> _lsTexts = new List<Text>();
        Mode _lsMode;
        readonly List<LevelData> _lsAll = new List<LevelData>();      // 当前模式全部关卡
        readonly List<int> _lsGlobalIdx = new List<int>();            // 对应 GameBootstrap._all 的全局下标
        readonly List<int> _lsFiltered = new List<int>();             // 过滤（搜索）后的全局下标，用于分页
        int _lsPage;
        string _lsQuery = "";
        int _lsTab;                                    // 0=全部 1=内置 2=自制 3=导入
        readonly List<Button> _lsTabBtns = new List<Button>();
        readonly List<Image> _lsTabImgs = new List<Image>();
        InputField _lsSearch;
        readonly List<GameObject> _editorReturnBtns = new List<GameObject>();
        Button _lsPrev, _lsNext;
        Text _lsPageLabel;
        int LsPageSize => _lsBtns.Count > 0 ? _lsBtns.Count : 5;

        // —— 屏幕比例适配引擎 ——
        // 停用各页自带 CanvasScaler，由本引擎直接接管 scaleFactor：canvas 逻辑尺寸 = 屏幕/scale，
        // 内容按设计分辨率铺满整屏（竖屏用用户手调稿，横屏切横版预设）。单倍缩放 → 文字清晰、不裁切。
        int _lastW, _lastH;
        readonly Dictionary<string, RectTransform> _rt = new Dictionary<string, RectTransform>();
        bool _wide;                                    // 当前是否横屏
        bool _canvasReady;                             // 布局已初始化（避免每次重复设置）
        bool _poseCaptured;                            // 已记录竖版稿 pos/size

        // 每页根 Canvas 的 CanvasScaler（接管为 Constant 模式，由本引擎每帧设 scaleFactor）
        readonly Dictionary<Page, UnityEngine.UI.CanvasScaler> _pageScaler = new Dictionary<Page, UnityEngine.UI.CanvasScaler>();

        // 运行时创建的 Text 复用界面已有字体（运行期 AddComponent<Text> 不会自动分配字体，必须显式指定）
        Font _uiFont;

        // 竖版「设计分辨率」：用户按 1080x1920 手工调好的基础稿。
        // 横版页（HUD/结算/暂停）在竖屏按 405x720 设计，横屏切 1280x720 预设。
        static readonly Dictionary<Page, Vector2> _designPortrait = new Dictionary<Page, Vector2>
        {
            { Page.MainMenu,    new Vector2(720, 1280) },
            { Page.LevelSelect, new Vector2(720, 1280) },
            { Page.LevelEditor, new Vector2(720, 1280) },
            { Page.Playing,     new Vector2(405, 720)  },
            { Page.Pause,       new Vector2(405, 720)  },
            { Page.Win,         new Vector2(405, 720)  },
        };
        static readonly Dictionary<Page, Vector2> _designLandscape = new Dictionary<Page, Vector2>
        {
            { Page.MainMenu,    new Vector2(720, 1280) },   // 竖版页横屏只加宽不换设计
            { Page.LevelSelect, new Vector2(720, 1280) },
            { Page.LevelEditor, new Vector2(720, 1280) },
            { Page.Playing,     new Vector2(1280, 720) },
            { Page.Pause,       new Vector2(1280, 720) },
            { Page.Win,         new Vector2(1280, 720) },
        };

        // —— 横版预设目标（HUD / 结算 / 暂停）：切横屏时对这些节点按「横版 JSON」覆盖布局 ——
        struct NodePose { public Vector2 pos, size; public Vector2 anchorMin, anchorMax; }
        readonly Dictionary<string, RectTransform> _orientNodes = new Dictionary<string, RectTransform>();
        readonly Dictionary<string, NodePose> _portraitPose = new Dictionary<string, NodePose>();
        static readonly Dictionary<string, NodePose> LandscapePose = BuildLandscapePose();

        static Dictionary<string, NodePose> BuildLandscapePose()
        {
            var d = new Dictionary<string, NodePose>();
            // —— GameHUD（横版 1280x720，取 GameHUD.json 原始值）——
            d["HUD_InfoPanel"] = new NodePose { pos = new Vector2(20, -20),  size = new Vector2(360, 136) };
            d["HUD_BtnPause"]  = new NodePose { pos = new Vector2(-20, -20),  size = new Vector2(104, 104) };
            d["HUD_BtnUndo"]   = new NodePose { pos = new Vector2(-140, -20), size = new Vector2(104, 104) };
            d["HUD_BtnRedo"]   = new NodePose { pos = new Vector2(-260, -20), size = new Vector2(104, 104) };
            d["HUD_BtnReset"]  = new NodePose { pos = new Vector2(-380, -20), size = new Vector2(104, 104) };
            d["HUD_HintBar"]   = new NodePose { pos = new Vector2(0, 20),    size = new Vector2(760, 52) };

            // —— WinPanel / PausePanel（横版 1280x720：仅卡片尺寸换回横版默认，内部锚点自适应）——
            d["WIN_Card"]    = new NodePose { size = new Vector2(560, 620) };
            d["PAUSE_Card"]  = new NodePose { size = new Vector2(480, 540) };
            return d;
        }

        public void Init(GameBootstrap game)
        {
            I = this;
            _game = game;

            _pages[Page.MainMenu] = Root("MainMenu");
            _pages[Page.LevelSelect] = Root("LevelSelect");
            _pages[Page.Playing] = Root("GameHUD");
            _pages[Page.Pause] = Root("PausePanel");
            _pages[Page.Win] = Root("WinPanel");
            _pages[Page.LevelEditor] = Root("LevelEditor");

            // —— 主菜单 ——
            Bind(Page.MainMenu, "BtnClassic",  () => ShowLevelSelect(Mode.Classic));
            Bind(Page.MainMenu, "BtnExtended", () => ShowLevelSelect(Mode.Extended));
            // 游戏内点击 BtnEditor → 进入游戏内编辑器页（壳在 Show() 时自动 Attach）；
            // 游戏外（Unity 编辑器不处于 Play 状态）此按钮无效 —— 用 Window/推箱子/关卡编辑器 打开 EditorWindow。
            Bind(Page.MainMenu, "BtnEditor",   () => Show(Page.LevelEditor));
            Bind(Page.MainMenu, "BtnQuit",     Quit);

            // —— 选关页（返回）+ 动态关卡槽位 ——
            Bind(Page.LevelSelect, "BtnBack", () => Show(Page.MainMenu));
            BindLevelSlots();

            // —— 游戏 HUD ——
            Bind(Page.Playing, "BtnPause", () => _game.OpenPause());
            Bind(Page.Playing, "BtnUndo",  () => _game.controller.DoUndo());
            Bind(Page.Playing, "BtnRedo",  () => _game.controller.DoRedo());
            Bind(Page.Playing, "BtnReset", () => _game.controller.DoReset());

            // —— 暂停 ——
            Bind(Page.Pause, "BtnResume", () => _game.ClosePause());
            Bind(Page.Pause, "BtnReplay", () => _game.ReplayLevel());
            Bind(Page.Pause, "BtnMenu",   () => _game.BackToMenu());

            // —— 结算 ——
            Bind(Page.Win, "BtnNext",   () => _game.NextLevel());
            Bind(Page.Win, "BtnReplay", () => _game.ReplayLevel());
            Bind(Page.Win, "BtnMenu",   () => _game.BackToMenu());

            // —— 编辑器试玩：暂停/结算页的「返回编辑器」按钮（仅试玩对局显示） ——
            EnsureEditorReturnButtons();

            _hudLevel = DeepText(_pages[Page.Playing].transform, "LevelName");
            _hudSteps = DeepText(_pages[Page.Playing].transform, "StepsText");
            _winStats = DeepText(_pages[Page.Win].transform, "StatsText");
            _selModeTag = DeepText(_pages[Page.LevelSelect].transform, "ModeTag");

            CollectLayoutTargets();
            BuildLevelSelectControls();
            ApplyLayoutForAspect(Screen.width, Screen.height);

            Show(Page.MainMenu);
        }

        void Update()
        {
            // 分辨率 / 旋转变化时重适配（编辑器拖窗口、移动端旋转）
            if (Screen.width != _lastW || Screen.height != _lastH)
                ApplyLayoutForAspect(Screen.width, Screen.height);
        }

        // -------------------------------------------------- 屏幕比例适配引擎
        /// <summary>
        /// 目标：任意屏幕比例下 UI「铺满整屏、不裁切、互不重叠」，且文字清晰（单倍缩放）。
        /// 做法：停用各页自带 CanvasScaler，改由本引擎直接接管 scaleFactor —— canvas 逻辑尺寸 = 屏幕/scale，
        /// 内容按设计分辨率铺满整屏（竖屏用用户手调稿，横屏切横版预设）。这是经实测确认「各比例排版正常」的版本。
        /// - 竖版页（主菜单/选关/编辑器）：设计 720x1280，横屏按高度铺满、居中留边不裁切；
        /// - 横版页（HUD/结算/暂停）：竖屏按 405x720 设计稿，横屏切 1280x720 横版预设。
        /// 用户手工调好的 1080x1920（竖屏）数值一律保留。
        /// </summary>
        void CollectLayoutTargets()
        {
            foreach (var name in new string[] { "Logo", "BtnClassic", "BtnExtended", "BtnEditor" })
            {
                var t = _pages[Page.MainMenu] != null ? DeepFind(_pages[Page.MainMenu].transform, name) : null;
                if (t != null) _rt["MM_" + name] = t as RectTransform ?? t.GetComponent<RectTransform>();
            }
            for (int i = 0; i < 5; i++)
            {
                var t = _pages[Page.LevelSelect] != null ? DeepFind(_pages[Page.LevelSelect].transform, "LevelBtn_" + i) : null;
                if (t != null) _rt["LS_" + i] = t as RectTransform ?? t.GetComponent<RectTransform>();
            }

            // 页面根 Canvas 缓存 + 配置 CanvasScaler：接管为 Constant 模式，由本引擎每帧设 scaleFactor（铺满整屏），
            // 并保留 dynamicPixelsPerUnit 让文字清晰。
            foreach (Page p in System.Enum.GetValues(typeof(Page)))
            {
                var go = _pages[p];
                if (go == null) continue;
                var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.dynamicPixelsPerUnit = 2;
                    _pageScaler[p] = scaler;
                }
            }

            // 方向预设节点（切横屏时才覆盖 pos/size；竖屏用场景值）
            RegisterOrientNode("HUD_InfoPanel", Page.Playing, "InfoPanel");
            RegisterOrientNode("HUD_BtnPause",  Page.Playing, "BtnPause");
            RegisterOrientNode("HUD_BtnUndo",   Page.Playing, "BtnUndo");
            RegisterOrientNode("HUD_BtnRedo",   Page.Playing, "BtnRedo");
            RegisterOrientNode("HUD_BtnReset",  Page.Playing, "BtnReset");
            RegisterOrientNode("HUD_HintBar",   Page.Playing, "HintBar");
            RegisterOrientNode("WIN_Card",      Page.Win,     "Card");
            RegisterOrientNode("PAUSE_Card",    Page.Pause,   "Card");
        }

        static bool IsPortraitPage(Page p)
            => p == Page.MainMenu || p == Page.LevelSelect || p == Page.LevelEditor;

        void RegisterOrientNode(string key, Page page, string node)
        {
            var root = _pages[page];
            if (root == null) return;
            var t = DeepFind(root.transform, node);
            if (t != null) _orientNodes[key] = t as RectTransform ?? t.GetComponent<RectTransform>();
        }

        /// <summary>首次适配时记录场景（竖版稿）各方向节点的原始 pos/size —— 用户调好的基础稿。</summary>
        void CapturePortraitPoses()
        {
            if (_poseCaptured) return;
            _poseCaptured = true;
            foreach (var kv in _orientNodes)
            {
                if (kv.Value == null) continue;
                _portraitPose[kv.Key] = new NodePose
                {
                    pos = kv.Value.anchoredPosition,
                    size = kv.Value.sizeDelta,
                };
            }
        }

        void ApplyOrientationPoses(bool wide)
        {
            if (!_poseCaptured) return;
            foreach (var kv in LandscapePose)
            {
                if (!_orientNodes.TryGetValue(kv.Key, out var rt) || rt == null) continue;
                var pose = wide ? kv.Value : (_portraitPose.TryGetValue(kv.Key, out var back) ? back : kv.Value);
                rt.anchoredPosition = pose.pos;
                rt.sizeDelta = pose.size;
            }
        }

        void ApplyLayoutForAspect(int w, int h)
        {
            if (w == 0 || h == 0) return;
            if (w == _lastW && h == _lastH && _canvasReady) return;
            _lastW = w; _lastH = h;
            _canvasReady = true;

            CapturePortraitPoses();

            bool wide = w >= h;
            if (wide != _wide)
            {
                _wide = wide;
                ApplyOrientationPoses(wide);          // 方向翻转 → 切换布局（竖屏用用户稿）
            }

            // 每页：Constant 模式下直接吃 scaleFactor = min(屏/设计)，canvas 自动铺满整屏、不裁切；
            // 单倍缩放 + dppu=2 → 文字清晰。竖屏用用户手调稿，横屏切横版预设。
            foreach (Page p in System.Enum.GetValues(typeof(Page)))
            {
                if (!_pageScaler.TryGetValue(p, out var scaler) || scaler == null) continue;
                var design = (wide ? _designLandscape : _designPortrait)[p];
                scaler.scaleFactor = Mathf.Min(w / design.x, h / design.y);
            }

            // 竖版页在横屏加宽主体按钮（视觉填充，不移锚点）
            SetWidth("MM_Logo",        wide ? 440f : 380f, 340f, 380f);
            SetWidth("MM_BtnClassic",  wide ? 640f : 460f);
            SetWidth("MM_BtnExtended", wide ? 640f : 460f);
            SetWidth("MM_BtnEditor",   wide ? 640f : 460f, 100f, 100f);
            for (int i = 0; i < 5; i++)
                SetWidth("LS_" + i, wide ? 660f : 520f);
        }

        void SetWidth(string key, float w) => SetWidth(key, w, null, null);

        void SetWidth(string key, float w, float? hIfWide, float? hIfNarrow)
        {
            if (!_rt.TryGetValue(key, out var rt)) return;
            var size = rt.sizeDelta;
            size.x = w;
            if (hIfWide.HasValue && _wide) size.y = hIfWide.Value;
            if (hIfNarrow.HasValue && !_wide) size.y = hIfNarrow.Value;
            rt.sizeDelta = size;
        }


        // -------------------------------------------------- 页面切换
        public void Show(Page page)
        {
            CurrentPage = page;
            foreach (var kv in _pages)
                if (kv.Value != null) kv.Value.SetActive(kv.Key == page);

            // 「返回编辑器」按钮：仅编辑器试玩对局的 暂停/结算 页显示
            bool showBack = (page == Page.Pause || page == Page.Win)
                            && _game != null && _game.CameFromEditor;
            foreach (var b in _editorReturnBtns)
                if (b != null) b.SetActive(showBack);

            // 切换到游戏内编辑器页：自动 Attach 壳
            if (page == Page.LevelEditor)
            {
                var root = _pages[Page.LevelEditor];
                if (root != null)
                {
                    var shell = root.GetComponent<GameEditorShell>();
                    if (shell == null) shell = root.gameObject.AddComponent<GameEditorShell>();
                    shell.Attach();
                }
            }
        }

        /// <summary>在暂停/结算页克隆「返回菜单」按钮，生成「返回编辑器」（默认隐藏，试玩对局才显示）。</summary>
        void EnsureEditorReturnButtons()
        {
            foreach (var p in new[] { Page.Pause, Page.Win })
            {
                var root = _pages[p];
                if (root == null) continue;
                if (DeepFind(root.transform, "BtnBackEditor") != null) continue;   // 幂等（域重载后重跑 Init）
                var tpl = DeepFind(root.transform, "BtnMenu");
                if (tpl == null) continue;
                var go = Instantiate(tpl.gameObject, root.transform, false);
                go.name = "BtnBackEditor";
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-20, -20);
                rt.sizeDelta = new Vector2(220, 64);
                var label = go.GetComponentInChildren<Text>();
                if (label != null) label.text = "返回编辑器";
                var btn = go.GetComponent<Button>();
                if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => _game.BackToEditor()); }
                go.SetActive(false);
                _editorReturnBtns.Add(go);
            }
        }

        /// <summary>
        /// 选关页：按模式加载全部关卡，重置搜索与分页，渲染第一页。
        /// </summary>
        public void ShowLevelSelect(Mode mode)
        {
            _lsMode = mode;
            if (_selModeTag != null)
                _selModeTag.text = mode == Mode.Extended ? "拓展模式 · 邪恶推箱人" : "经典模式";

            // 收集当前模式下的全部关卡及其在 GameBootstrap._all 中的全局下标（StartLevelAt 用全局下标）
            var all = _game.AllLevels;
            _lsAll.Clear();
            _lsGlobalIdx.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].mode == mode) { _lsAll.Add(all[i]); _lsGlobalIdx.Add(i); }
            }
            _lsQuery = "";
            _lsTab = 0;
            RefreshTabVisual();
            if (_lsSearch != null) _lsSearch.text = "";
            _lsPage = 0;
            RefreshLevelList();
            Show(Page.LevelSelect);
        }

        /// <summary>通关：填充结算数据并切到结算页。</summary>
        public void ShowWin(IGameMode mode)
        {
            if (_winStats != null && mode != null)
                _winStats.text = $"步数 {mode.StepCount}     撤销 {mode.UndoCount}";
            Show(Page.Win);
        }

        /// <summary>HUD 刷新（每步 / 撤销 / 换关后调用）。</summary>
        public void RefreshHud(LevelData lv, IGameMode mode)
        {
            if (lv != null && _hudLevel != null)
                _hudLevel.text = $"[{lv.id}] {lv.name} · {(lv.mode == Mode.Extended ? "拓展" : "经典")}";
            if (mode != null && _hudSteps != null)
                _hudSteps.text = $"步数 {mode.StepCount}     撤销 {mode.UndoCount}";
        }

        /// <summary>由编辑器「试玩」调用：直接用给定关卡进入游戏（克隆数据，不依赖落盘）。</summary>
        public void PlayLevel(LevelData data)
        {
            if (data == null) return;
            _game?.PlayCustomLevel(data);
        }

        // -------------------------------------------------- 选关槽位 / 分页 / 搜索
        /// <summary>收集 LevelBtn_0..N 的 Button/Text 引用（点击在翻页时按当前页动态重绑）。</summary>
        void BindLevelSlots()
        {
            var root = _pages[Page.LevelSelect];
            if (root == null) return;
            for (int i = 0; i < 12; i++)
            {
                var t = DeepFind(root.transform, "LevelBtn_" + i);
                if (t == null) break;
                _lsBtns.Add(t.GetComponent<Button>());
                _lsTexts.Add(t.GetComponentInChildren<Text>());
            }
        }

        /// <summary>运行时创建搜索框与分页控件（直接挂到选关页根，随竖版页 CanvasScaler 一起缩放）。</summary>
        void BuildLevelSelectControls()
        {
            var root = _pages[Page.LevelSelect];
            if (root == null) return;
            var parent = root.transform;

            // 取界面已有字体（运行期 AddComponent&lt;Text&gt; 不会自动分配字体，必须显式指定，否则文字不可见）
            _uiFont = (_selModeTag != null && _selModeTag.font != null) ? _selModeTag.font
                    : (_lsTexts.Count > 0 && _lsTexts[0] != null && _lsTexts[0].font != null ? _lsTexts[0].font : null)
                      ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            // —— 搜索框（InputField）——
            if (DeepFind(parent, "LS_Search") == null)
            {
                var go = new GameObject("LS_Search");
                go.transform.SetParent(parent, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0, 372);
                rt.sizeDelta = new Vector2(560, 76);
                var img = go.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.94f);
                img.raycastTarget = true;
                var inp = go.AddComponent<InputField>();
                inp.targetGraphic = img;
                inp.transition = Selectable.Transition.ColorTint;

                var txt = new GameObject("Text");
                txt.transform.SetParent(go.transform, false);
                var trt = txt.AddComponent<RectTransform>();
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(16, 8); trt.offsetMax = new Vector2(-16, -8);
                var t = txt.AddComponent<Text>();
                t.font = _uiFont;
                t.color = new Color(0.25f, 0.14f, 0.01f);
                t.fontSize = 30; t.alignment = TextAnchor.MiddleLeft; t.text = "";

                var ph = new GameObject("Placeholder");
                ph.transform.SetParent(go.transform, false);
                var prt = ph.AddComponent<RectTransform>();
                prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
                prt.offsetMin = new Vector2(16, 8); prt.offsetMax = new Vector2(-16, -8);
                var pt = ph.AddComponent<Text>();
                pt.font = _uiFont;
                pt.color = new Color(0.55f, 0.5f, 0.45f);
                pt.fontSize = 30; pt.alignment = TextAnchor.MiddleLeft; pt.text = "搜索关卡名 / ID";

                inp.textComponent = t;
                inp.placeholder = pt;
                inp.onValueChanged.AddListener(s => { _lsQuery = s ?? ""; _lsPage = 0; RefreshLevelList(); });
                _lsSearch = inp;
            }

            // —— 分类标签：全部 / 内置 / 自制 / 导入（搜索框上方一行） ——
            if (DeepFind(parent, "LS_Tab0") == null)
            {
                string[] tabNames = { "全部", "内置", "自制", "导入" };
                for (int i = 0; i < tabNames.Length; i++)
                {
                    int tab = i;
                    var b = MakeLsButton(parent, "LS_Tab" + i, tabNames[i],
                        new Vector2(-210 + i * 140, 448),
                        () => { _lsTab = tab; _lsPage = 0; RefreshTabVisual(); RefreshLevelList(); });
                    b.GetComponent<RectTransform>().sizeDelta = new Vector2(128, 56);
                    var lab = b.GetComponentInChildren<Text>();
                    if (lab != null) lab.fontSize = 26;
                    _lsTabBtns.Add(b);
                    _lsTabImgs.Add(b.GetComponent<Image>());
                }
                RefreshTabVisual();
            }

            // —— 分页：上一页 / 页码 / 下一页 ——
            if (DeepFind(parent, "LS_Prev") == null)
            {
                _lsPrev = MakeLsButton(parent, "LS_Prev", "上一页", new Vector2(-205, -410),
                    () => { if (_lsPage > 0) { _lsPage--; RenderLevelPage(); } });
                _lsNext = MakeLsButton(parent, "LS_Next", "下一页", new Vector2(205, -410),
                    () => { if ((_lsPage + 1) * LsPageSize < _lsFiltered.Count) { _lsPage++; RenderLevelPage(); } });
                var pg = new GameObject("LS_PageLabel");
                pg.transform.SetParent(parent, false);
                var prt = pg.AddComponent<RectTransform>();
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(0.5f, 0.5f);
                prt.anchoredPosition = new Vector2(0, -410); prt.sizeDelta = new Vector2(240, 60);
                _lsPageLabel = pg.AddComponent<Text>();
                _lsPageLabel.font = _uiFont;
                _lsPageLabel.color = new Color(0.25f, 0.14f, 0.01f);
                _lsPageLabel.fontSize = 28; _lsPageLabel.alignment = TextAnchor.MiddleCenter;
            }
        }

        /// <summary>分类标签选中态着色：选中烫金，未选淡奶油。</summary>
        void RefreshTabVisual()
        {
            for (int i = 0; i < _lsTabImgs.Count; i++)
            {
                if (_lsTabImgs[i] == null) continue;
                _lsTabImgs[i].color = i == _lsTab
                    ? new Color(1f, 0.784f, 0.341f, 1f)
                    : new Color(1f, 1f, 1f, 0.75f);
            }
        }

        static bool TabMatch(LevelData lv, int tab)
        {
            switch (tab)
            {
                case 1: return lv.source != "custom" && lv.source != "imported";   // 内置（含旧档无标记）
                case 2: return lv.source == "custom";
                case 3: return lv.source == "imported";
                default: return true;
            }
        }

        Button MakeLsButton(Transform parent, string name, string label, Vector2 pos, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(170, 72);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0.784f, 0.341f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var lab = new GameObject("Text");
            lab.transform.SetParent(go.transform, false);
            var lrt = lab.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var lt = lab.AddComponent<Text>();
            lt.font = _uiFont;
            lt.color = new Color(0.25f, 0.14f, 0.01f); lt.fontSize = 28; lt.alignment = TextAnchor.MiddleCenter; lt.text = label;
            btn.onClick.AddListener(action);
            return btn;
        }

        /// <summary>按搜索词过滤当前模式关卡，并重算页码后渲染。
        /// _lsFiltered 存「模式内本地下标」(指向 _lsAll / _lsGlobalIdx，二者平行)。</summary>
        void RefreshLevelList()
        {
            _lsFiltered.Clear();
            var q = _lsQuery.Trim().ToLower();
            for (int i = 0; i < _lsAll.Count; i++)
            {
                if (!TabMatch(_lsAll[i], _lsTab)) continue;
                if (string.IsNullOrEmpty(q) ||
                    _lsAll[i].name.ToLower().Contains(q) ||
                    _lsAll[i].id.ToLower().Contains(q) ||
                    (!string.IsNullOrEmpty(_lsAll[i].author) && _lsAll[i].author.ToLower().Contains(q)))
                {
                    _lsFiltered.Add(i);
                }
            }
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)_lsFiltered.Count / LsPageSize));
            _lsPage = Mathf.Clamp(_lsPage, 0, totalPages - 1);
            RenderLevelPage();
        }

        /// <summary>渲染当前页：填充槽位文本、可见性、点击绑定（本地下标 → 全局下标 → StartLevelAt）。</summary>
        void RenderLevelPage()
        {
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)_lsFiltered.Count / LsPageSize));
            if (_lsPageLabel != null)
                _lsPageLabel.text = _lsFiltered.Count == 0 ? "无匹配" : $"第 {_lsPage + 1} / {totalPages} 页";
            if (_lsPrev != null) _lsPrev.interactable = _lsPage > 0;
            if (_lsNext != null) _lsNext.interactable = (_lsPage + 1) < totalPages;

            for (int i = 0; i < _lsBtns.Count; i++)
            {
                var btn = _lsBtns[i];
                if (btn == null) continue;
                int fIdx = _lsPage * LsPageSize + i;
                if (fIdx < _lsFiltered.Count)
                {
                    int local = _lsFiltered[fIdx];
                    int g = _lsGlobalIdx[local];
                    if (_lsTexts[i] != null) _lsTexts[i].text = $"{fIdx + 1:D2} · {_lsAll[local].name}";
                    btn.gameObject.SetActive(true);
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => _game.StartLevelAt(g));
                }
                else
                {
                    btn.gameObject.SetActive(false);
                }
            }
        }

        // -------------------------------------------------- 查找与绑定
        /// <summary>
        /// 按名查找页面根。⚠️ 必须遍历场景根节点 —— GameObject.Find 只能找到
        /// active 对象，页面根初始为隐藏态时会静默返回 null，导致整个路由瘫痪。
        /// </summary>
        static GameObject Root(string name)
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == name) return go;
            Debug.LogWarning($"[UIRouter] 场景中找不到 UI 根 '{name}'");
            return null;
        }

        // -------------------------------------------------- 编辑态预览（未进 Play 也铺满）
        /// <summary>
        /// 供 Editor 脚本在「未进 Play 的 Game 视口」调用：把各页 CanvasScaler 接管为 Constant 模式并驱动 scaleFactor，
        /// 公式与 Play 模式 ApplyLayoutForAspect 完全一致，使预览与 Play 排版一致（竖版页在 16:9 下背景铺满、不裁切）。
        /// 仅改缩放，不动任何 UI 元素 pos/size，保留用户手调布局。无 UnityEditor 依赖，可安全留在 Runtime 程序集。
        /// </summary>
        static readonly Dictionary<Page, string> _editRootName = new Dictionary<Page, string>
        {
            { Page.MainMenu, "MainMenu" }, { Page.LevelSelect, "LevelSelect" }, { Page.Playing, "GameHUD" },
            { Page.Pause, "PausePanel" }, { Page.Win, "WinPanel" }, { Page.LevelEditor, "LevelEditor" },
        };

        public static void ApplyEditPreviewScale(int w, int h)
        {
            if (w == 0 || h == 0) return;
            bool wide = w >= h;
            var design = wide ? _designLandscape : _designPortrait;
            foreach (Page p in System.Enum.GetValues(typeof(Page)))
            {
                var go = Root(_editRootName[p]);
                if (go == null) continue;
                var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler == null) continue;
                float target = Mathf.Min(w / design[p].x, h / design[p].y);
                // 仅在确有变化时才写，避免每帧把场景标记为 dirty
                if (scaler.uiScaleMode != UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize
                    || Mathf.Abs(scaler.scaleFactor - target) > 1e-3f)
                {
                    scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.dynamicPixelsPerUnit = 2;
                    scaler.scaleFactor = target;
                }
            }
        }

        void Bind(Page page, string btnName, UnityEngine.Events.UnityAction action)
        {
            var root = _pages[page];
            if (root == null) return;
            var t = DeepFind(root.transform, btnName);
            if (t == null) { Debug.LogWarning($"[UIRouter] 找不到按钮 '{btnName}'（{page}）"); return; }
            var btn = t.GetComponent<Button>();
            if (btn == null) { Debug.LogWarning($"[UIRouter] '{btnName}' 上没有 Button 组件"); return; }
            btn.onClick.AddListener(action);
        }

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

        // -------------------------------------------------- 其它入口
        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
