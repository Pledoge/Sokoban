using System.Collections.Generic;
using UnityEngine;

namespace Sokoban.Game
{
    using Core;

    /// <summary>
    /// 音频总管：BGM + 音效。
    /// 自举 —— 进 Play 时自动创建，不需要往场景里拖任何对象（AssetBundle/重导入都不会丢）。
    /// 音频资源放在 Assets/Resources/Audio/（.wav），按名字懒加载并缓存。
    /// 静音开关持久化在 PlayerPrefs。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        const string AudioDir = "Audio/";
        const string MutePref = "sokoban.muted";
        public const string BgmMain = "bgm_main";

        // 音效名（与 Resources/Audio/*.wav 文件名一一对应）
        public const string SfxMove = "sfx_move";
        public const string SfxPush = "sfx_push";
        public const string SfxGoal = "sfx_goal";
        public const string SfxWin = "sfx_win";
        public const string SfxUndo = "sfx_undo";
        public const string SfxBlocked = "sfx_blocked";
        public const string SfxClick = "sfx_click";
        public const string SfxEnemy = "sfx_enemy";

        public static AudioManager I { get; private set; }

        AudioSource _bgm;
        AudioSource _sfx;
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        float _bgmVolume = 0.42f;
        bool _paused;
        bool _muted;
        int _goalsOnBoard = -1;      // -1 = 未跟踪（进关卡/重置时清），用于识别"箱子刚归位"
        float _lastStepAt = -1f;     // 移动音效节流（长按方向键时不糊成一片）

        public bool Muted => _muted;

        // -------------------------------------------------- 自举
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot() => Ensure();

        /// <summary>取（必要时创建）唯一实例。任何入口都可安全调用。</summary>
        public static AudioManager Ensure()
        {
            if (I != null) return I;
            var go = new GameObject("~AudioManager");
            DontDestroyOnLoad(go);
            return go.AddComponent<AudioManager>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;

            _bgm = gameObject.AddComponent<AudioSource>();
            _bgm.loop = true;
            _bgm.playOnAwake = false;
            _bgm.spatialBlend = 0f;

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.loop = false;
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;

            _muted = PlayerPrefs.GetInt(MutePref, 0) == 1;
            ApplyVolume();

            // 场景里没有任何 AudioListener 时补一个，否则整局静音
            if (FindObjectOfType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        }

        void OnDestroy() { if (I == this) I = null; }

        /// <summary>
        /// BGM 自愈看门狗。
        /// 域重载/进场景后头几帧调 Play() 可能被音频系统丢弃（输出设备尚未就绪），
        /// 切后台再回来时源也可能停在停止态 —— 两种情况都会让 BGM 永久静音。
        /// 这里只在「有曲子、确实没在播、窗口有焦点、音频没被暂停」时补一次 Play()，
        /// 每帧成本只是一个 bool 判断。
        /// </summary>
        void Update()
        {
            if (_bgm == null || _bgm.clip == null || _bgm.isPlaying) return;
            if (!Application.isFocused) return;   // 失焦时 Unity 会挂起音频，此时补播无效且会空转
            if (AudioListener.pause) return;      // Game 视图 Mute Audio / 编辑器暂停
            _bgm.Play();
        }

        // -------------------------------------------------- BGM
        public void PlayBgm(string name = BgmMain)
        {
            var clip = Clip(name);
            if (clip == null || _bgm == null) return;
            if (_bgm.clip == clip && _bgm.isPlaying) return;
            _bgm.clip = clip;
            _bgm.Play();
        }

        /// <summary>暂停时把 BGM 压低（不切断，回来时接得上）。</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            ApplyVolume();
        }

        public void ToggleMute()
        {
            _muted = !_muted;
            PlayerPrefs.SetInt(MutePref, _muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyVolume();
        }

        void ApplyVolume()
        {
            if (_bgm != null) _bgm.volume = _bgmVolume * (_paused ? 0.35f : 1f) * (_muted ? 0f : 1f);
            if (_sfx != null) _sfx.mute = _muted;
        }

        // -------------------------------------------------- 音效
        public void PlaySfx(string name, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (_muted || _sfx == null) return;
            var clip = Clip(name);
            if (clip == null) return;
            _sfx.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            _sfx.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>按钮点击（UI 层统一调用）。</summary>
        public void PlayClick() => PlaySfx(SfxClick, 0.55f, 0.03f);

        public static void Click() { if (I != null) I.PlayClick(); }

        /// <summary>走一步的反馈：推箱子 / 普通脚步 / 敌人挪动。</summary>
        public void PlayStep(StepDetail d)
        {
            if (Time.unscaledTime - _lastStepAt < 0.045f) return;   // 节流
            _lastStepAt = Time.unscaledTime;

            if (d.playerPushedBox || d.enemyPushedBox) PlaySfx(SfxPush, 0.60f, 0.08f);
            else if (d.playerMoved) PlaySfx(SfxMove, 0.34f, 0.10f);
            else if (d.enemyMoved) PlaySfx(SfxEnemy, 0.30f, 0.05f);
        }

        /// <summary>无效操作（撞墙/推不动）。</summary>
        public void PlayBlocked() => PlaySfx(SfxBlocked, 0.45f, 0.06f);

        public void PlayUndo() => PlaySfx(SfxUndo, 0.55f, 0.04f);
        public void PlayWin() => PlaySfx(SfxWin, 0.85f, 0.0f);

        /// <summary>进关卡/重置时调用，避免把"开局就有箱子在终点上"误报成归位音。</summary>
        public void ResetTracking() => _goalsOnBoard = -1;

        /// <summary>每次棋盘变化后调用：箱子数「刚归位」的那一下播奖励音。</summary>
        public void TrackBoard(WorldState s)
        {
            if (s == null) return;
            int c = 0;
            for (int i = 0; i < s.Boxes.Length; i++) if (s.IsGoal(s.Boxes[i])) c++;
            if (_goalsOnBoard >= 0 && c > _goalsOnBoard) PlaySfx(SfxGoal, 0.70f, 0.0f);
            _goalsOnBoard = c;
        }

        // -------------------------------------------------- 资源
        AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>(AudioDir + name);
            if (c == null) Debug.LogWarning($"[Audio] 找不到 Resources/{AudioDir}{name}.wav");
            _clips[name] = c;
            return c;
        }
    }
}
