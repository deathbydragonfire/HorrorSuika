#ifndef HORROR_BACKGROUND_COMMON_INCLUDED
#define HORROR_BACKGROUND_COMMON_INCLUDED

// Must match HorrorBackground.MaxEyes.
#define HORROR_MAX_EYES 8

// Globals uploaded every frame by HorrorBackground. Kept out of UnityPerMaterial so the backdrop,
// the fog layer, and every eye read one shared copy.
float4 _HorrorEyes[HORROR_MAX_EYES];   // xy world position, z world radius, w presence 0..1
float _HorrorEyeCount;
float _HorrorBeat;                     // heartbeat envelope 0..1 (lub-dub)
float _HorrorSurge;                    // surge envelope 0..1
float _HorrorFlicker;                  // 0 = normal, 1 = blackout
float _HorrorGlitch;                   // 0..1 VHS tearing, spikes with flicker
float _HorrorTime;                     // unscaled seconds, wrapped to keep precision on WebGL
float4 _HorrorView;                    // xy view centre, z half height, w half width
float4 _HorrorPlayfieldRect;           // xy min, zw max in world units; zero size disables
float4 _HorrorDepthFog;                // x start distance, y full distance, z far darkening, w near darkening
float _VhsSuppress;                    // set by VhsOverlay: 1 during regular gameplay, 0 on menus

float HorrorHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float2 HorrorHash22(float2 p)
{
    float n = HorrorHash21(p);
    return float2(n, HorrorHash21(p + n + 17.17));
}

float HorrorHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float HorrorNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = HorrorHash21(i);
    float b = HorrorHash21(i + float2(1.0, 0.0));
    float c = HorrorHash21(i + float2(0.0, 1.0));
    float d = HorrorHash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float HorrorNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float n000 = HorrorHash31(i);
    float n100 = HorrorHash31(i + float3(1.0, 0.0, 0.0));
    float n010 = HorrorHash31(i + float3(0.0, 1.0, 0.0));
    float n110 = HorrorHash31(i + float3(1.0, 1.0, 0.0));
    float n001 = HorrorHash31(i + float3(0.0, 0.0, 1.0));
    float n101 = HorrorHash31(i + float3(1.0, 0.0, 1.0));
    float n011 = HorrorHash31(i + float3(0.0, 1.0, 1.0));
    float n111 = HorrorHash31(i + float3(1.0, 1.0, 1.0));
    float x00 = lerp(n000, n100, u.x);
    float x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x);
    float x11 = lerp(n011, n111, u.x);
    return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
}

// Four rotated octaves. Rotation between octaves hides the value-noise grid.
float HorrorFbm2(float2 p)
{
    const float2x2 octave = float2x2(1.6, 1.2, -1.2, 1.6);
    float value = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 4; i++)
    {
        value += amplitude * HorrorNoise2(p);
        p = mul(octave, p);
        amplitude *= 0.5;
    }
    return value;
}

float HorrorFbm3(float3 p)
{
    float value = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 3; i++)
    {
        value += amplitude * HorrorNoise3(p);
        p = p * 2.03 + 17.1;
        amplitude *= 0.5;
    }
    return value;
}

// Cosine palette tuned for horror rather than a full rainbow: it cycles crimson, maroon,
// toxic teal, and bruise violet, so the psychedelic shifts never drift into cheerful yellows.
float3 HorrorPalette(float t)
{
    const float3 bias = float3(0.38, 0.08, 0.22);
    const float3 amplitude = float3(0.34, 0.13, 0.21);
    const float3 phase = float3(0.0, 0.5, 0.3);
    return saturate(bias + amplitude * cos(6.28318 * (t + phase))) * 1.6;
}

float2 HorrorRotate(float2 p, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

#endif
