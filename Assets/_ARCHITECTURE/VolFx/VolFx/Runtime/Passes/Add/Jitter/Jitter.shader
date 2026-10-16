//  JitterFx В© NullTale - https://x.com/NullTale
Shader "Hidden/VolFx/Jitter"
{
    SubShader
    {
        name "Jitter"
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 0

        ZTest Always
        ZWrite Off
        ZClip false
        Cull Off
        
        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            
            sampler2D _MainTex;
            sampler2D _JitterTex;
            sampler2D _NoiseTex;
            
            float4 _Jitter;
            float4 _R;
            float4 _G;
            float4 _B;
            
            float4 _Mask;
            
            #define _NoiseTiling     _Jitter.x
            #define _NoiseOffset     _Jitter.yz
            #define _NoisePower      _Jitter.w
            
            #define _MaskPower      _Mask.x
            #define _MaskOffset     float2(.5, .5)
            #define _MaskValue      _Mask.w
            
            #define _JitterWeight   _Mask.yz
            
            struct vert_in
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct frag_in
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            frag_in vert(vert_in v)
            {
                frag_in o;
                o.vertex = v.vertex;
                o.uv = v.uv;
                return o;
            }

            float2 MirrorUV(float2 uv)
            {
                return abs(frac(uv * 0.5) * 2.0 - 1.0);
            }

            float2 ClampUV(float2 uv)
            {
                return saturate(uv);
            }
            
            float luma(float3 rgb)
            {
                return dot(rgb, float3(.299, .587, .114));
            }
                    
            half4 frag(frag_in i) : SV_Target
            {
                // noise
                float2 noiseUV = i.uv * _NoiseTiling + _NoiseOffset;
                noiseUV = MirrorUV(noiseUV);

                float2 jitter = tex2D(_NoiseTex, noiseUV).rg * 2.0 - 1.0;

                jitter *= _NoisePower;
                jitter *= _JitterWeight;

                // mask
                float dist = distance(i.uv, _MaskOffset);
                float mask = saturate(dist / _MaskPower);
                mask = lerp(1, smoothstep(0.0, 1.0, mask), _MaskValue);

                jitter *= mask;

                // chromatic
                float2 uvR = ClampUV(i.uv + jitter * _R.w);
                float2 uvG = ClampUV(i.uv + jitter * _G.w);
                float2 uvB = ClampUV(i.uv + jitter * _B.w);

                half4 r = tex2D(_MainTex, uvR);
                half4 g = tex2D(_MainTex, uvG);
                half4 b = tex2D(_MainTex, uvB);

                half3 col = 0;
                col += r.rgb * _R.rgb;
                col += g.rgb * _G.rgb;
                col += b.rgb * _B.rgb;

                half alpha = (r.a + g.a + b.a) / 3.0;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}