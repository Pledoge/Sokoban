using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

/// <summary>
/// 鼠标驱动的斜向分屏 + 全息炫卡光效。
/// 分屏：按像素到"过鼠标的斜线"的有向距离做 smoothstep 过渡。
/// 炫卡：光点径向扩散 + 彩虹箔(吃高光) + 均匀磨砂颗粒 + 星点闪片 + 两层视差景深。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RawImage))]
public class LenticularReveal : MonoBehaviour
{
    [Header("分屏 Split")]
    public float splitAngle = 45f;          // 斜线角度，45 = 左下/右上平分
    public float gradientWidth = 0.014f;    // 过渡带宽，越小分界越锐
    public Color seamColor = new Color(1f, 0.96f, 0.86f, 1f);
    [Range(0f, 1f)] public float seamStrength = 0.6f;

    [Header("炫卡 Holographic")]
    [Range(0f, 1f)] public float holoStrength = 0.7f;     // 彩虹箔强度
    public float holoScale = 9f;                          // 彩虹流动尺度（越大色带越密）
    public float holoGrain = 80f;                         // 磨砂细颗粒密度
    [Range(0f, 1f)] public float holoLumMin = 0.20f;      // 箔的亮度门槛下限（画面越亮越要调高）
    [Range(0f, 1f)] public float holoLumMax = 0.82f;      // 箔的亮度门槛上限
    [Range(0f, 2f)] public float glowStrength = 0.45f;    // 光晕加亮强度
    public float glowRadius = 0.55f;                      // 光晕半径（越大扩散越开）
    [Range(0f, 2f)] public float glitterStrength = 1.0f;  // 星点闪片强度
    public float glitterScale = 110f;                     // 闪片颗粒密度
    public float parallax = 0.016f;                       // 两层视差位移(景深感)
    [Range(0f, 0.2f)] public float idleShimmer = 0.05f;   // 待机微动幅度(0=完全静止)

    Material mat;
    bool _guiFresh;      // 本帧 OnGUI 是否拿到了鼠标（编辑模式主通道）

    void OnEnable()
    {
        mat = GetComponent<RawImage>().material;
        ApplyParams();
        if (mat != null) mat.SetVector("_Mouse", new Vector4(0.5f, 0.5f, 0, 0));
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorApplication.update += EditorUpdate;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorUpdate;
#endif
    }

    void ApplyParams()
    {
        if (mat == null) mat = GetComponent<RawImage>().material;
        if (mat == null) return;

        mat.SetFloat("_NormalAngle", splitAngle * Mathf.Deg2Rad);
        mat.SetFloat("_Width", gradientWidth);
        mat.SetColor("_LineColor", seamColor);
        mat.SetFloat("_LineStrength", seamStrength);

        mat.SetFloat("_HoloStrength", holoStrength);
        mat.SetFloat("_HoloScale", holoScale);
        mat.SetFloat("_HoloGrain", holoGrain);
        mat.SetFloat("_HoloLumMin", holoLumMin);
        mat.SetFloat("_HoloLumMax", holoLumMax);
        mat.SetFloat("_GlowStrength", glowStrength);
        mat.SetFloat("_GlowRadius", glowRadius);
        mat.SetFloat("_SparkleStrength", glitterStrength);
        mat.SetFloat("_SparkleScale", glitterScale);
        mat.SetFloat("_Parallax", parallax);
        mat.SetFloat("_Idle", idleShimmer);

        mat.SetFloat("_Aspect", Screen.height > 0 ? (float)Screen.width / Screen.height : 1f);
    }

    void Update()
    {
        if (!Application.isPlaying) return;   // 编辑模式交给 OnGUI / EditorApplication.update
        SetMouseFromInput();
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑模式（未进 Play）的鼠标主通道。
    /// Input.mousePosition 在编辑态只在少数 GUI 事件里刷新 —— 分屏线因此会「跳」在几个离散位置；
    /// 而 OnGUI 每次重绘都带着当前 Event.current.mousePosition，这才是连贯跟随的关键。
    /// 坐标是 GUI 空间（左上原点、单位与 Screen 一致），转归一化时把 Y 翻过来。
    /// </summary>
    void OnGUI()
    {
        if (Application.isPlaying) return;
        var e = Event.current;
        if (e == null) return;
        // Layout 阶段的 mousePosition 不可靠（可能是 0,0 或上一帧残留），
        // 只信 Repaint / 鼠标事件；Repaint 每帧都有，足够驱动平滑跟随。
        if (e.type != EventType.Repaint
            && e.type != EventType.MouseMove
            && e.type != EventType.MouseDrag) return;
        FeedMouse(e.mousePosition, guiSpace: true);
    }

    void EditorUpdate()
    {
        if (Application.isPlaying) return;
        // 兜底：Game 视图被遮挡 / 没触发重绘时 OnGUI 收不到事件，退回旧通道
        if (!_guiFresh) SetMouseFromInput();
        _guiFresh = false;
        InternalEditorUtility.RepaintAllViews();
    }
#endif

    void SetMouseFromInput()
    {
        mat = mat != null ? mat : GetComponent<RawImage>().material;
        if (mat == null) return;
        FeedMouse(Input.mousePosition, guiSpace: false);
    }

    /// <summary>把鼠标位置写进 _Mouse（归一化 0..1）。出视口就冻结，避免整屏单色。</summary>
    void FeedMouse(Vector2 p, bool guiSpace)
    {
        if (mat == null) mat = GetComponent<RawImage>().material;
        if (mat == null) return;

        mat.SetFloat("_Aspect", Screen.height > 0 ? (float)Screen.width / Screen.height : 1f);
        if (Screen.width <= 0 || Screen.height <= 0) return;

        float nx = p.x / Screen.width;
        float ny = guiSpace ? 1f - p.y / Screen.height : p.y / Screen.height;

        // 鼠标不在视图内（移到了别的窗口）就冻结，否则坐标飞出屏幕导致整屏单色
        if (nx < 0f || nx > 1f || ny < 0f || ny > 1f) return;

        mat.SetVector("_Mouse", new Vector4(nx, ny, 0f, 0f));
        _guiFresh = true;
    }
}
