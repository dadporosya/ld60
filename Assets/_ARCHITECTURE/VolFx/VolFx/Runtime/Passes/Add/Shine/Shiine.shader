// VolFx © NullTale - https://x.com/NullTale
Shader "Hidden/VolFx/Shine"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        ZClip False

        Pass
        {
            Name "Shine"

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _GradTex;
            sampler2D _ProjTex;

            float4 _DataA;
            float4 _DataB;

            #define _Offset   _DataA.x
            #define _Width    _DataA.y
            #define _Rotation _DataA.z
            #define _Gap      _DataA.w
            
            #define _Shine   _DataB.x * 3
            #define _Dark    _DataB.y
            #define _Replace _DataB.z
            #define _Proj    _DataB.w * 3
            
            float luma(float3 rgb)
            {
                return dot(rgb, float3(.299, .587, .114));
            }
            
            float chroma(float3 rgb)
            {
                return max(rgb.r, max(rgb.g, rgb.b));
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = v.vertex;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv - 0.5;

                float2 dir = float2(cos(_Rotation), sin(_Rotation));

                float coord = dot(uv, dir);

                coord /= max(_Width, 0.0001);
                coord += _Offset;
                coord += 0.5;

                // Infinite tiled gradient
                float gap = saturate(_Gap);
                float visible = max(1.0 - gap, 0.0001);

                float tile = frac(coord);
                float mask = step(tile, visible);

                tile /= visible;

                fixed4 src = tex2D(_MainTex, i.uv);
                fixed4 grad = tex2D(_GradTex, float2(tile, 0.5));

                grad *= mask;

                half4 proj = tex2D(_ProjTex, i.uv);
                src.rgb += (proj.rgb * proj.a * luma(src.rgb) * luma(grad.rgb) * grad.a * _Proj);
                
                src.rgb =  lerp(src.rgb,  src.rgb * grad.rgb, grad.a * _Replace);
                src.rgb += lerp(0, grad.rgb * luma(src.rgb) * grad.a, _Shine);
                src.rgb -= lerp(0, grad.rgb * grad.a * (1 - luma(src.rgb)), _Dark);

                return src;
            }

            ENDCG
        }
    }
}