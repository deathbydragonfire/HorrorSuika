Shader "HorrorSuika/VHSFullscreen"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 1)) = 1
        _Wobble ("Line Wobble", Range(0, 4)) = 1
        _TrackingStrength ("Tracking Band Strength", Range(0, 4)) = 1
        _TrackingSpeed ("Tracking Band Speed", Float) = 0.06
        _Chroma ("Chromatic Offset", Range(0, 0.01)) = 0.0028
        _ColorBleed ("Chroma Bleed", Range(0, 1)) = 0.55
        _Desaturate ("Desaturate", Range(0, 1)) = 0.18
        _Tint ("Tape Tint", Color) = (1.0, 0.94, 0.96, 1)
        _BlackLift ("Black Lift", Range(0, 0.1)) = 0
        _Scanlines ("Scanlines", Range(0, 1)) = 0.22
        _Noise ("Tape Noise", Range(0, 0.3)) = 0.06
        _Dropouts ("Dropouts", Range(0, 1)) = 0.5
        _Vignette ("Vignette", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "VHS"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _Wobble;
            float _TrackingStrength;
            float _TrackingSpeed;
            float _Chroma;
            float _ColorBleed;
            float _Desaturate;
            float4 _Tint;
            float _BlackLift;
            float _Scanlines;
            float _Noise;
            float _Dropouts;
            float _Vignette;

            // Set by HorrorBackground in the game scene; zero elsewhere.
            float _HorrorGlitch;

            // Set by VhsOverlay: 1 during regular gameplay (effect off), 0 on menus, pause, and results.
            float _VhsSuppress;

            float VhsHash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float VhsNoise(float x)
            {
                float i = floor(x);
                float f = frac(x);
                return lerp(VhsHash(float2(i, 0.0)), VhsHash(float2(i + 1.0, 0.0)), f * f * (3.0 - 2.0 * f));
            }

            float3 SampleScene(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float amount = _Intensity * (1.0 - saturate(_VhsSuppress));
                if (amount <= 0.001)
                {
                    return original;
                }

                float t = fmod(_Time.y, 1000.0);
                float glitch = saturate(_HorrorGlitch);

                // Per-line horizontal wobble, like a worn tape's timebase error.
                float scanRow = uv.y * 240.0;
                float wobble = (VhsNoise(scanRow * 0.35 + t * 9.0) - 0.5) * 0.0016 * _Wobble;

                // Tracking band: a noisy strip that rolls slowly up the frame, dragging the picture sideways.
                float bandY = frac(t * _TrackingSpeed);
                float bandDist = abs(uv.y - bandY);
                bandDist = min(bandDist, 1.0 - bandDist);
                float band = exp(-bandDist * bandDist * 1800.0) * _TrackingStrength;
                float bandShift = band * (VhsNoise(scanRow * 2.0 + t * 40.0) - 0.3) * 0.012;

                // Glitch tears from blackouts in the game scene.
                float tearRow = floor(uv.y * 30.0);
                float tearOn = step(0.7, VhsHash(float2(tearRow, floor(t * 18.0))));
                float tear = (VhsHash(float2(tearRow * 3.1, floor(t * 18.0))) - 0.5) * 0.05 * tearOn * glitch;

                float2 sampleUV = uv + float2(wobble + bandShift + tear, 0.0);

                // Chromatic offset grows toward the frame edges.
                float2 fromCentre = uv - 0.5;
                float chroma = _Chroma * (1.0 + dot(fromCentre, fromCentre) * 3.0 + glitch * 2.0 + band);
                float3 color;
                color.r = SampleScene(sampleUV + float2(chroma, 0.0)).r;
                color.g = SampleScene(sampleUV).g;
                color.b = SampleScene(sampleUV - float2(chroma, 0.0)).b;

                // VHS carries colour at a fraction of the luma bandwidth, so chroma smears sideways.
                float3 smear = (SampleScene(sampleUV + float2(0.004, 0.0)) + SampleScene(sampleUV + float2(0.008, 0.0))) * 0.5;
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                float smearLuma = dot(smear, float3(0.299, 0.587, 0.114));
                color = lerp(color, luma + (smear - smearLuma), _ColorBleed * 0.5);

                color = lerp(color, luma.xxx, _Desaturate);
                color = color * _Tint.rgb + _BlackLift;

                float scan = 0.5 + 0.5 * sin(uv.y * 360.0 * 6.28318);
                color *= 1.0 - _Scanlines * scan * 0.5;

                // Grain rides on brightness so the blacks stay black instead of turning to grey static.
                // Grain refreshes at a film-like 24 Hz instead of every frame, so it reads as tape texture, not buzz.
                float grain = VhsHash(uv * _ScreenParams.xy + frac(floor(t * 24.0) * 0.137) * 311.0) - 0.5;
                float grainLuma = dot(color, float3(0.299, 0.587, 0.114));
                color += grain * (_Noise * (0.15 + grainLuma * 1.5) + band * 0.05);

                // Dropouts: short bright streaks where the tape lost oxide.
                float dropRow = floor(uv.y * 180.0);
                float dropSeed = VhsHash(float2(dropRow, floor(t * 24.0)));
                float dropX = VhsHash(float2(dropRow * 7.3, floor(t * 24.0)));
                float drop = step(0.994 - glitch * 0.02, dropSeed) * smoothstep(0.05, 0.0, abs(uv.x - dropX));
                color += drop * _Dropouts * 0.6;

                color += band * 0.03 * VhsHash(float2(uv.x * 400.0, t));

                // Very wide and soft so it never reads as a bright disc in the middle.
                float vignette = 1.0 - smoothstep(0.45, 1.1, length(fromCentre * float2(1.1, 1.25))) * _Vignette;
                color *= vignette;

                return float4(lerp(original.rgb, max(color, 0.0), amount), original.a);
            }
            ENDHLSL
        }
    }
}
