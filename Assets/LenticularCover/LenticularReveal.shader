Shader "UI/LenticularReveal"
{
    Properties
    {
        _TexA ("Texture A", 2D) = "white" {}
        _TexB ("Texture B", 2D) = "white" {}
        _Mouse ("Mouse (UV)", Vector) = (0.5, 0.5, 0, 0)
        _NormalAngle ("Split Normal Angle (rad)", Float) = 0.7854
        _Width ("Gradient Width", Float) = 0.014
        _Aspect ("Aspect", Float) = 1.0
        _LineColor ("Seam Color", Color) = (1, 0.96, 0.86, 1)
        _LineStrength ("Seam Strength", Range(0, 1)) = 0.6

        _HoloStrength ("Holo Foil", Range(0, 1)) = 0.7
        _HoloScale ("Holo Flow Scale", Float) = 9
        _HoloGrain ("Holo Grain Scale", Float) = 80
        _HoloLumMin ("Holo Lum Min", Range(0, 1)) = 0.20
        _HoloLumMax ("Holo Lum Max", Range(0, 1)) = 0.82
        _GlowStrength ("Glow Strength", Range(0, 2)) = 0.45
        _GlowRadius ("Glow Radius", Float) = 0.55
        _SparkleStrength ("Glitter", Range(0, 2)) = 1.0
        _SparkleScale ("Glitter Scale", Float) = 110
        _Parallax ("Depth Parallax", Float) = 0.016
        _Idle ("Idle Shimmer", Float) = 0.05
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 screenPos : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _TexA;
            sampler2D _TexB;
            float2  _Mouse;
            float   _NormalAngle;
            float   _Width;
            float   _Aspect;
            float4  _LineColor;
            float   _LineStrength;

            float   _HoloStrength;
            float   _HoloScale;
            float   _HoloGrain;
            float   _HoloLumMin;
            float   _HoloLumMax;
            float   _GlowStrength;
            float   _GlowRadius;
            float   _SparkleStrength;
            float   _SparkleScale;
            float   _Parallax;
            float   _Idle;

            // 彩虹：余弦调色板，比 hue 分段更顺滑
            float3 Hue2RGB(float h)
            {
                return 0.5 + 0.5 * cos(6.2831853 * (h + float3(0.0, 0.33, 0.67)));
            }

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // 值噪声（双线性插值）：用来打散条纹感
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 3 阶 fbm（手写展开，避免动态循环）
            float FBM(float2 p)
            {
                float s = ValueNoise(p) * 0.5;
                p = p * 2.03 + 13.7;
                s += ValueNoise(p) * 0.25;
                p = p * 2.01 + 7.1;
                s += ValueNoise(p) * 0.125;
                return s / 0.875;
            }

            v2f vert (appdata_base v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;

                // 鼠标相对屏幕中心的偏移(-1..1)，当作"卡面倾斜/视角"的代理
                float2 mOff = (_Mouse - 0.5) * 2.0;

                // 待机微动：鼠标静止时箔面自己也在缓慢呼吸
                float idle = sin(_Time.y * 0.55) * _Idle;

                // ---------- 1. 斜向分屏 ----------
                float2 diff = uv - _Mouse;
                diff.x *= _Aspect;                                  // 宽高比修正，保住 45°
                float2 n = float2(cos(_NormalAngle), sin(_NormalAngle));
                float d = dot(diff, n);                             // 到斜线的有向距离
                float blend = smoothstep(-_Width, _Width, d);       // 0=图A(左下) 1=图B(右上)

                // ---------- 2. 视差景深：两图按鼠标反向微移 ----------
                float2 uvA = clamp(uv + mOff * _Parallax, 0.0, 1.0);
                float2 uvB = clamp(uv - mOff * _Parallax, 0.0, 1.0);
                fixed4 colA = tex2D(_TexA, uvA);
                fixed4 colB = tex2D(_TexB, uvB);
                fixed4 col  = lerp(colA, colB, blend);

                // ---------- 3. 光点：鼠标处射出一束光，向四周扩散 ----------
                // 关键：不是一道直边掠光，而是以鼠标为圆心的柔和径向光晕
                float2 lp = _Mouse + float2(idle, -idle * 0.6);
                float2 rv = uv - lp;
                rv.x *= _Aspect;
                float rad = length(rv);
                float glow = pow(saturate(1.0 - rad / max(_GlowRadius, 0.0001)), 1.7);
                // 再叠一圈更柔的外晕，彻底消除硬边
                glow = saturate(glow + 0.35 * pow(saturate(1.0 - rad / max(_GlowRadius * 1.9, 0.0001)), 1.4));

                // ---------- 4. 有机噪声：把"直条纹"打散成扩散状 ----------
                float n1 = FBM(uv * 2.6 + mOff * 1.2 + idle);
                float n2 = FBM(uv * 6.5 - mOff * 1.8 + 7.3);
                float grain = ValueNoise(uv * _HoloGrain + mOff * 3.0);

                // ---------- 5. 全息相位：同心扩散 + 角向漩涡 + 噪声扰动（无线性条纹）----------
                float ang = atan2(rv.y, rv.x);
                float holoH = rad * _HoloScale * 0.22           // 以光点为中心的同心扩散（压淡，避免同心圆太机械）
                            + ang * 0.10                        // 缓慢漩涡
                            + n1 * 1.45 + n2 * 0.60             // fbm 打散，消除直线感
                            + (grain - 0.5) * 0.22              // 细颗粒微色偏
                            + dot(mOff, float2(0.9, 0.6)) * 0.9 // 倾斜 -> 彩虹整片推移
                            + idle * 2.5;                       // 待机微动 -> 缓慢流动
                float3 holo = Hue2RGB(holoH);
                // 均匀细颗粒：让箔面像磨砂微结构，而不是平滑色块
                holo *= lerp(0.65, 1.35, grain);

                // ---------- 6. 箔只吃画面高光；基底抬平 -> 整张卡均匀上箔，光点处略强 ----------
                float lum = dot(col.rgb, float3(0.299, 0.587, 0.114));
                float gloss = smoothstep(_HoloLumMin, _HoloLumMax, lum);   // 亮部才有箔，暗部保持干净
                float holoMask = gloss * (0.45 + 0.55 * glow) * _HoloStrength * lerp(0.86, 1.12, blend);
                col.rgb = lerp(col.rgb, col.rgb * 0.50 + holo, saturate(holoMask));

                // ---------- 7. 星点闪片：均匀散布，光经过的地方才亮 ----------
                float2 sp = uv * _SparkleScale;
                float2 cellId = floor(sp);
                float rnd = Hash21(cellId);
                float isGlit = step(0.88, rnd);
                float twinkle = pow(saturate(sin(_Time.y * 2.0 + rnd * 6.2831853) * 0.5 + 0.5), 5.0);
                float2 inCell = frac(sp) - 0.5;
                float coreMask = 1.0 - smoothstep(0.0, 0.42, length(inCell));
                float glit = isGlit * twinkle * coreMask * _SparkleStrength;
                col.rgb += glit * (0.45 * glow + 0.15) * holo;

                // ---------- 8. 光本身的柔和加亮（带一点全息色散）----------
                col.rgb += glow * _GlowStrength * (0.30 + 0.70 * gloss) * lerp(float3(1.0, 1.0, 1.0), holo, 0.55);

                // ---------- 9. 分界缝：暖白金高光，带一点全息色 ----------
                float seam = 1.0 - smoothstep(0.0, max(_Width, 0.0001) * 2.2, abs(d));
                float3 seamCol = _LineColor.rgb * lerp(float3(1.0, 1.0, 1.0), holo, 0.6);
                col.rgb += seam * _LineStrength * seamCol;

                // ---------- 10. 轻压暗角，聚焦卡片主体 ----------
                float2 cOff = uv - 0.5;
                col.rgb *= 1.0 - dot(cOff, cOff) * 0.26;

                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
