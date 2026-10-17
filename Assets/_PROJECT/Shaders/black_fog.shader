Shader "Kaleidos/unnamed pattern (5) 2D URP" {
    Properties {
        _Zoom ("Zoom", Float) = 1
        _Pan ("Pan", Vector) = (0,0,0,0)
        _Rotation ("Rotation", Float) = 0
        _Tint ("Tint", Color) = (1,1,1,1)
        _Speed ("Speed", Float) = 1
        _CellSize ("Pixel Size", Float) = 3.0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Zoom;
            float4 _Pan;
            float _Rotation;
            float4 _Tint;
            float _Speed;
            float _CellSize;

            #define uTime (_Time.y * _Speed)
            #define uResolution float2(640.0, 360.0)
            #define cellSize _CellSize

#define time uTime
static const float noiseScale = 6.0;
#define screenSize float2(640.0, 360.0)
#define sampleOffset (float2)(0.0)
#define renderSize float2(640.0, 360.0)

static const float octaves = 6.0;
static const float gain = 0.5;

#define morphPhase (uTime*0.16)
#define scrollSpeed float2(0.08, 0.08)
static const float2 loopD = float2(0.0, 0.0);
static const float loopZ = 0.0;
static const float warpStrength = 0.75;
static const float warpScale = 3.0;
static const float pinchStrength = 0.0;
#define warpPhase (uTime*0.08)

static const float4 PALETTE[110] = {
  float4(0.0,0.0,0.0,1.0),
  float4(0.00917431,0.00917431,0.00917431,1.0),
  float4(0.0183486,0.0183486,0.0183486,1.0),
  float4(0.0275229,0.0275229,0.0275229,1.0),
  float4(0.0366972,0.0366972,0.0366972,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.0,0.0,0.0,1.0),
  float4(0.000472811,0.000472811,0.000472811,1.0),
  float4(0.000945622,0.000945622,0.000945622,1.0),
  float4(0.00141843,0.00141843,0.00141843,1.0),
  float4(0.00189124,0.00189124,0.00189124,1.0),
  float4(0.00236406,0.00236406,0.00236406,1.0),
  float4(0.00283687,0.00283687,0.00283687,1.0),
  float4(0.00330968,0.00330968,0.00330968,1.0),
  float4(0.00378249,0.00378249,0.00378249,1.0),
  float4(0.0042553,0.0042553,0.0042553,1.0),
  float4(0.00472811,0.00472811,0.00472811,1.0),
  float4(0.00520092,0.00520092,0.00520092,1.0),
  float4(0.00567373,0.00567373,0.00567373,1.0),
  float4(0.00614655,0.00614655,0.00614655,1.0),
  float4(0.00661936,0.00661936,0.00661936,1.0),
  float4(0.00709217,0.00709217,0.00709217,1.0),
  float4(0.00756498,0.00756498,0.00756498,1.0),
  float4(0.00803779,0.00803779,0.00803779,1.0),
  float4(0.0085106,0.0085106,0.0085106,1.0),
  float4(0.00898341,0.00898341,0.00898341,1.0),
  float4(0.00945622,0.00945622,0.00945622,1.0),
  float4(0.00992904,0.00992904,0.00992904,1.0),
  float4(0.0104018,0.0104018,0.0104018,1.0),
  float4(0.0108747,0.0108747,0.0108747,1.0),
  float4(0.0113475,0.0113475,0.0113475,1.0),
  float4(0.0175302,0.0175302,0.0175302,1.0),
  float4(0.023713,0.023713,0.023713,1.0),
  float4(0.0298958,0.0298958,0.0298958,1.0),
  float4(0.0360786,0.0360786,0.0360786,1.0),
  float4(0.0422613,0.0422613,0.0422613,1.0),
  float4(0.0484441,0.0484441,0.0484441,1.0),
  float4(0.0546269,0.0546269,0.0546269,1.0),
  float4(0.0608097,0.0608097,0.0608097,1.0),
  float4(0.0669924,0.0669924,0.0669924,1.0),
  float4(0.0731752,0.0731752,0.0731752,1.0),
  float4(0.079358,0.079358,0.079358,1.0),
  float4(0.0855407,0.0855407,0.0855407,1.0),
  float4(0.0917235,0.0917235,0.0917235,1.0),
  float4(0.0979063,0.0979063,0.0979063,1.0),
  float4(0.104089,0.104089,0.104089,1.0),
  float4(0.110272,0.110272,0.110272,1.0),
  float4(0.116455,0.116455,0.116455,1.0),
  float4(0.122637,0.122637,0.122637,1.0),
  float4(0.12882,0.12882,0.12882,1.0),
  float4(0.135003,0.135003,0.135003,1.0),
  float4(0.141186,0.141186,0.141186,1.0),
  float4(0.147368,0.147368,0.147368,1.0),
  float4(0.153551,0.153551,0.153551,1.0),
  float4(0.159734,0.159734,0.159734,1.0),
  float4(0.165917,0.165917,0.165917,1.0),
  float4(0.1721,0.1721,0.1721,1.0),
  float4(0.178282,0.178282,0.178282,1.0),
  float4(0.184465,0.184465,0.184465,1.0),
  float4(0.190648,0.190648,0.190648,1.0),
  float4(0.196831,0.196831,0.196831,1.0),
  float4(0.203013,0.203013,0.203013,1.0),
  float4(0.209196,0.209196,0.209196,1.0),
  float4(0.215379,0.215379,0.215379,1.0),
  float4(0.221562,0.221562,0.221562,1.0),
  float4(0.227745,0.227745,0.227745,1.0),
  float4(0.233927,0.233927,0.233927,1.0),
  float4(0.24011,0.24011,0.24011,1.0),
  float4(0.246293,0.246293,0.246293,1.0),
  float4(0.252476,0.252476,0.252476,1.0),
  float4(0.258658,0.258658,0.258658,1.0),
  float4(0.264841,0.264841,0.264841,1.0),
  float4(0.271024,0.271024,0.271024,1.0),
  float4(0.277207,0.277207,0.277207,1.0),
  float4(0.283389,0.283389,0.283389,1.0),
  float4(0.289572,0.289572,0.289572,1.0),
  float4(0.295755,0.295755,0.295755,1.0),
  float4(0.301938,0.301938,0.301938,1.0),
  float4(0.308121,0.308121,0.308121,1.0),
  float4(0.314303,0.314303,0.314303,1.0),
  float4(0.320486,0.320486,0.320486,1.0),
  float4(0.326669,0.326669,0.326669,1.0),
  float4(0.332852,0.332852,0.332852,1.0),
  float4(0.339034,0.339034,0.339034,1.0),
  float4(0.345217,0.345217,0.345217,1.0),
  float4(0.3514,0.3514,0.3514,1.0),
  float4(0.357583,0.357583,0.357583,1.0),
  float4(0.363766,0.363766,0.363766,1.0),
  float4(0.369948,0.369948,0.369948,1.0),
  float4(0.376131,0.376131,0.376131,1.0),
  float4(0.382314,0.382314,0.382314,1.0),
  float4(0.390625,0.390625,0.390625,1.0)
};
float4 paletteLookup(float x){
  int i = clamp(int(clamp(x,0.0,1.0)*256.0),0,255);
  return PALETTE[clamp(int(float(i)/255.0*110.0),0,109)];
}

#define TAU 6.28318530718
#define MAX_OCTAVES 10
#define MORPH_DESYNC_AMP   1.6
#define MORPH_DESYNC_SCALE 2.5

float2 frameC(float2 uv) {
    float2 r = renderSize;
    return (uv - 0.5) * screenSize / r.y;
}
float2 frameUV(float2 c) {
    float2 r = renderSize;
    return 0.5 + c * r.y / screenSize;
}

float hash3(float3 p) {
    p = frac(p * float3(127.1, 311.7, 74.7));
    p += dot(p, p.yxz + 19.19);
    return frac((p.x + p.y) * p.z);
}

float vnoise3(float3 p) {
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(lerp(hash3(i + float3(0,0,0)), hash3(i + float3(1,0,0)), u.x),
            lerp(hash3(i + float3(0,1,0)), hash3(i + float3(1,1,0)), u.x), u.y),
        lerp(lerp(hash3(i + float3(0,0,1)), hash3(i + float3(1,0,1)), u.x),
            lerp(hash3(i + float3(0,1,1)), hash3(i + float3(1,1,1)), u.x), u.y),
        u.z
    );
}

float basisSample(float3 p, float2 d, float pz) {

    return vnoise3(p) * 2.0 - 1.0;
}

float fbm3(float3 p) {
    float v = 0.0, a = 0.5, norm = 0.0;
    float2  d  = loopD;
    float pz = loopZ;
    int oct = int(octaves + 0.5);
    for (int i = 0; i < MAX_OCTAVES; i++) {
        if (i >= oct) break;
        float sn = basisSample(p, d, pz);
        float t;
        {
            t = sn * 0.5 + 0.5;
        }
        v    += a * t;
        norm += a;
        p     = p * 2.0 + float3(5.3, 1.7, 3.1);
        d    *= 2.0;
        pz   *= 2.0;
        a    *= gain;
    }
    return v / norm;
}

#define DOMAIN_AMP  0.35
#define PINCH_R     0.8
#define PINCH_K     0.9
#define PINCH_PULSE 0.5

float whash2(float2 p) {
    p = frac(p * float2(127.1, 311.7));
    p += dot(p, p.yx + 19.19);
    return frac((p.x + p.y) * 43.32);
}
float wnoise2(float2 p) {
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(whash2(i + float2(0.0, 0.0)), whash2(i + float2(1.0, 0.0)), u.x),
               lerp(whash2(i + float2(0.0, 1.0)), whash2(i + float2(1.0, 1.0)), u.x), u.y);
}
float wfbm2(float2 p) {
    float v = 0.0, a = 0.5, norm = 0.0;
    for (int i = 0; i < 3; i++) {
        v += a * (wnoise2(p) * 2.0 - 1.0);
        norm += a; p = p * 2.0 + float2(5.3, 1.7); a *= 0.5;
    }
    return v / norm;
}

float2 warpField(float2 uv) {
    float2 fp = (float2)(warpPhase);
    
    return float2(wfbm2(uv * warpScale + fp), wfbm2(uv * warpScale + fp + float2(31.4, 17.7)));
}
float2 warpUV(float2 uv) {
    
    {
        float2 q = warpField(uv);
        return uv + warpStrength * DOMAIN_AMP * warpField(uv + warpStrength * q);
    }
    float2  c   = frameC(uv);
    float r   = length(c);
    if (r > 1e-5) {
        float k  = pinchStrength * (1.0 + PINCH_PULSE * sin(warpPhase * TAU));
        float rn = min(r / PINCH_R, 1.0);
        float factor;
        if (k >= 0.0) {
            factor = pow(rn, k * PINCH_K);
        } else {
            factor = 1.0 + (-k) * PINCH_K * (1.0 - rn * rn); 
        }
        c *= factor;
    }
    return frameUV(c);
}

float2 rotateUV(float2 uv) {
    return uv;
}

float fieldN(float2 uv, float mzExtra) {
    float mz = morphPhase + mzExtra;

    mz += MORPH_DESYNC_AMP * (wnoise2(uv * MORPH_DESYNC_SCALE) - 0.5);
    
    float2 nsv = (float2)(noiseScale);
    return fbm3(float3(uv * nsv + time * scrollSpeed, mz));
}

float4 shade(float2 screen_coords) {
    float2 block = floor((screen_coords + sampleOffset) / cellSize) * cellSize;
    float2 uv0   = warpUV(block / screenSize);
    float mzAdd = 0.0, valAdd = 0.0;
    float2 uvDisp = (float2)(0.0);

    float n = fieldN(rotateUV(uv0 + uvDisp), mzAdd);
    n = clamp(n + valAdd, 0.0, 1.0);
    return paletteLookup(n);
}


            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes IN){
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = IN.uv;
                return o;
            }

            float4 frag(Varyings i) : SV_Target {
                float2 px = 1.0 / fwidth(i.uv);
                float ar = px.x / px.y;
                float2 uv = i.uv - 0.5;
                if (ar > 1.77778) uv.x *= ar / 1.77778; else uv.y *= 1.77778 / ar;
                float s = sin(_Rotation), cs = cos(_Rotation);
                uv = mul(uv, float2x2(cs, -s, s, cs));
                uv = uv / _Zoom + _Pan.xy;
                float4 c = shade((uv + 0.5) * float2(640.0, 360.0));
                return float4(c.rgb * c.a, 1.0) * _Tint;
            }
            ENDHLSL
        }
    }
}