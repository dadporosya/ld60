// UI shader for a full-screen fader that is punched through by an array of rectangles.
//
// The rectangles are fed from C# (see ScreenHolesFader.cs) in CANVAS LOCAL SPACE, which is the
// space UI vertices already live in when Unity batches a Canvas - so no extra transforms are
// needed in the fragment shader and everything stays correct for any canvas render mode.
//
// _Holes[i]      = (centerX, centerY, halfWidth, halfHeight)   in canvas units
// _HoleParams[i] = (cornerRadius, feather, strength, unused)   in canvas units / 0..1
Shader "UI/ScreenHoles"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            // Keep in sync with ScreenHolesFader.MaxHoles
            #define MAX_HOLES 32

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 canvasPos     : TEXCOORD1; // vertex position in canvas local space
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4    _Color;
            fixed4    _TextureSampleAdd;
            float4    _ClipRect;
            float4    _MainTex_ST;

            float4 _Holes[MAX_HOLES];
            float4 _HoleParams[MAX_HOLES];
            int    _HoleCount;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.canvasPos = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            // Signed distance to a rounded box: negative inside, 0 on the edge, positive outside.
            float RoundedBoxSdf(float2 p, float2 halfSize, float radius)
            {
                radius = min(radius, min(halfSize.x, halfSize.y));
                float2 q = abs(p) - (halfSize - radius);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // How much of the fader is cut away at this pixel: 0 = solid, 1 = fully transparent.
                float cut = 0.0;

                UNITY_LOOP
                for (int i = 0; i < _HoleCount; i++)
                {
                    float4 hole = _Holes[i];
                    float4 hp   = _HoleParams[i];

                    float d = RoundedBoxSdf(IN.canvasPos.xy - hole.xy, hole.zw, hp.x);

                    // Feather band sits just inside the hole edge, so the hole never grows.
                    float feather = max(hp.y, 0.0001);
                    float inside  = 1.0 - smoothstep(-feather, 0.0, d);

                    cut = max(cut, inside * hp.z);
                }

                color.a *= saturate(1.0 - cut);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.canvasPos.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }

    Fallback "UI/Default"
}
