using System.Linq;
using UnityEngine;

namespace Sokoban.Game
{
    using Core;

    /// <summary>
    /// 游戏引导：加载内置关卡 → 按模式组队 → 通关走结算页。
    /// 页面切换 / 按钮绑定在 <see cref="UIRouter"/>。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public BoardRenderer board;
        public GameController controller;

        LevelData[] _all;
        LevelData[] _queue;          // 当前模式的关卡序列
        int _idx;
        Mode _mode;                  // 选关页/当前模式
        LevelData _current;

        void Start()
        {
            _all = LoadBuiltInLevels();
            controller = controller ?? gameObject.AddComponent<GameController>();
            board = board ?? gameObject.AddComponent<BoardRenderer>();

            var kb = gameObject.AddComponent<KeyboardInput>();
            controller.BindInput(kb);
            kb.OnUndoRequested += controller.DoUndo;
            kb.OnRedoRequested += controller.DoRedo;
            kb.OnResetRequested += controller.DoReset;
            kb.OnNextLevelRequested += NextLevel;
            kb.OnPauseRequested += () =>
            {
                if (UIRouter.I != null && controller.Mode != null)
                {
                    if (UIRouter.I.CurrentPage == UIRouter.Page.Pause) ClosePause();
                    else OpenPause();
                }
            };

            // —— 音频：BGM 常驻，音效跟随事件 ——
            // 只在「走了一步」时跟踪箱子归位，撤销/重置/读关不会误报归位音。
            var audio = AudioManager.Ensure();
            audio.PlayBgm();
            kb.OnMuteRequested += audio.ToggleMute;
            kb.OnUndoRequested += audio.PlayUndo;
            kb.OnRedoRequested += audio.PlayUndo;
            kb.OnResetRequested += audio.PlayUndo;

            controller.OnWorldChanged += HandleWorldChanged;
            controller.OnStepTaken += _ => HandleWorldChanged();
            controller.OnStepTaken += audio.PlayStep;
            controller.OnStepTaken += _ => audio.TrackBoard(controller.Mode?.State);
            controller.OnStepBlocked += audio.PlayBlocked;
            controller.OnWin += audio.PlayWin;
            controller.OnWin += HandleWin;

            GetComponent<UIRouter>().Init(this);
        }

        // -------------------------------------------------- 对外入口（UIRouter 调用）
        /// <summary>全部内置关卡（按 id 排序），供选关页填充。</summary>
        public System.Collections.Generic.List<LevelData> AllLevels =>
            _all != null ? new System.Collections.Generic.List<LevelData>(_all) : new System.Collections.Generic.List<LevelData>();

        /// <summary>从主菜单进入某模式的选关页（每次重扫，包含本会话新保存的关卡）。</summary>
        public void ShowLevelSelect(Mode mode)
        {
            ReloadLevels();
            _mode = mode;
            UIRouter.I.ShowLevelSelect(mode);
        }

        /// <summary>重扫 Resources/Levels（保存新关后立即生效，无需重启）。</summary>
        public void ReloadLevels() => _all = LoadBuiltInLevels();

        /// <summary>直接试玩一个自定义关卡（编辑器「试玩」按钮调用，无需落盘）。</summary>
        public bool CameFromEditor { get; private set; }   // 当前对局是否来自编辑器试玩（暂停/结算页显示「返回编辑器」）

        public void PlayCustomLevel(LevelData data)
        {
            if (data == null) return;
            _current = data;
            _mode = data.mode;
            _queue = new LevelData[] { data };
            _idx = 0;
            CameFromEditor = true;
            LoadCurrent();
            UIRouter.I.Show(UIRouter.Page.Playing);
        }

        /// <summary>从试玩对局（暂停/结算页）返回关卡编辑器。编辑器壳未销毁，直接切页即可续编。</summary>
        public void BackToEditor()
        {
            CameFromEditor = false;
            controller.Pause(false);
            UIRouter.I.Show(UIRouter.Page.LevelEditor);
        }

        /// <summary>从选关页进入全列表第 index 关。</summary>
        public void StartLevelAt(int index)
        {
            if (_all == null || index < 0 || index >= _all.Length) return;
            var lv = _all[index];
            _mode = lv.mode;
            _queue = System.Array.FindAll(_all, l => l.mode == _mode);
            _idx = System.Array.IndexOf(_queue, lv);
            CameFromEditor = false;
            LoadCurrent();
            UIRouter.I.Show(UIRouter.Page.Playing);
        }

        /// <summary>直接进入某模式的第一关（保留：N 键连跳 / 兼容入口）。</summary>
        public void StartMode(Mode mode)
        {
            _queue = _all == null ? null : System.Array.FindAll(_all, l => l.mode == mode).ToArray();
            if (_queue == null || _queue.Length == 0)
            {
                Debug.LogWarning($"没有 {mode} 模式关卡");
                return;
            }
            _mode = mode;
            _idx = 0;
            CameFromEditor = false;
            LoadCurrent();
            UIRouter.I.Show(UIRouter.Page.Playing);
        }

        /// <summary>暂停（HUD 按钮或 Esc）。</summary>
        public void OpenPause()
        {
            controller.Pause(true);
            AudioManager.I?.SetPaused(true);            // BGM 压低而非切断
            UIRouter.I.Show(UIRouter.Page.Pause);
        }

        /// <summary>从暂停继续。</summary>
        public void ClosePause()
        {
            controller.Pause(false);
            AudioManager.I?.SetPaused(false);
            UIRouter.I.Show(UIRouter.Page.Playing);
        }

        public void ReplayLevel()
        {
            AudioManager.I?.ResetTracking();             // 重玩 → 归位音对比基准清零
            controller.LoadLevel(_current);              // LoadLevel 内部重建模式（步数/撤销清零）
            board.Bind(controller.Mode.State, _current);
            controller.Pause(false);                     // 从暂停页重玩时解除暂停
            AudioManager.I?.SetPaused(false);
            UIRouter.I.RefreshHud(_current, controller.Mode);
            UIRouter.I.Show(UIRouter.Page.Playing);      // 从结算/暂停页切回游戏页
        }

        public void NextLevel()
        {
            _idx++;
            if (_queue == null || _idx >= _queue.Length)
            {
                if (CameFromEditor) { BackToEditor(); return; }   // 试玩单关通关 → 回编辑器而非主菜单
                BackToMenu(); return;                             // 通关全部 → 回菜单
            }
            LoadCurrent();
            UIRouter.I.Show(UIRouter.Page.Playing);      // 从结算页切回游戏页
        }

        public void BackToMenu() => UIRouter.I.Show(UIRouter.Page.MainMenu);

        // -------------------------------------------------- 内部
        void LoadCurrent()
        {
            _current = _queue[_idx];
            controller.LoadLevel(_current);
            board.Bind(controller.Mode.State, _current);
            FitCamera(_current);
            AudioManager.I?.ResetTracking();               // 新关卡 → 归位音对比基准清零
            UIRouter.I.RefreshHud(_current, controller.Mode);
        }

        void HandleWorldChanged()
        {
            if (_current == null || controller.Mode == null) return;
            // 重置会新建 WorldState 对象，必须让 Board 重新指向最新状态，否则棋盘视觉不刷新
            board.Bind(controller.Mode.State, _current);
            UIRouter.I.RefreshHud(_current, controller.Mode);
        }

        void HandleWin()
        {
            board.Refresh(_current);
            UIRouter.I.ShowWin(controller.Mode);
        }

        static LevelData[] LoadBuiltInLevels()
        {
            var texts = Resources.LoadAll<TextAsset>("Levels");
            var list = new System.Collections.Generic.List<LevelData>();
            foreach (var t in texts)
            {
                try { list.Add(EditorCore.LevelIO.FromJson(t.text)); }
                catch (System.Exception e) { Debug.LogWarning($"关卡 {t.name} 解析失败: {e.Message}"); }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return list.ToArray();
        }

        static void FitCamera(LevelData lv)
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.orthographic = true;
            float aspect = (float)Screen.width / Screen.height;
            float halfH = lv.height * 0.5f + 1.2f;
            float halfW = lv.width * 0.5f + 1.2f;
            cam.orthographicSize = Mathf.Max(halfH, halfW / aspect);
            cam.transform.position = new Vector3(0, 0, -10);
        }
    }
}
