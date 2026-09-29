Shader "HorrorSuika/BlackFog"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.004, 0.0, 0.006, 1)
        _WispLight ("Wisp Edge Light", Color) = (0.07, 0.045, 0.055, 1)
        _WispLightStrength ("Wisp Edge Strength", Range(0, 3)) = 1
        _MaxAlpha ("Max Opacity", Range(0, 1)) = 0.92
        _BaseDensity ("Base Density", Range(0, 1)) = 0.18
        _Threshold ("Wisp Threshold", Range(0, 1)) = 0.38
        _Contrast ("Wisp Contrast", Range(0.5, 6)) = 2.6
        _Scale ("Scale", Float) = 0.55
        _Speed ("Drift Speed", Float) = 0.06
        _EdgeDensity ("Edge Density", Range(0, 1)) = 0.45
        _EyeParting ("Eye Parting", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent-200"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BlackFog"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HorrorBackgroundCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float4 _WispLight;
                float _WispLightStrength;
                float _MaxAlpha;
                float _BaseDensity;
                float _Threshold;
                float _Contrast;
                float _Scale;
                float _Speed;
                float _EdgeDensity;
                float _EyeParting;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 world = input.positionWS.xy;
                float halfHeight = max(_HorrorView.z, 0.001);
                float2 view = (world - _HorrorView.xy) / halfHeight;
                float t = _HorrorTime * _Speed;

                // Two drifting smoke layers moving against each other, both bent by a shared slow warp.
                float2 p = view / max(_Scale, 0.01);
                float2 warp = float2(
                    HorrorFbm2(p * 0.7 + float2(t * 0.6, 0.0)),
                    HorrorFbm2(p * 0.7 + float2(3.1, -t * 0.5)));
                float near = HorrorFbm2(p * 1.1 + warp * 2.4 + float2(t, t * 0.35));
                float far = HorrorFbm2(p * 2.2 - warp * 1.6 + float2(-t * 1.4, t * 0.8));
                float wisps = saturate((near * 0.65 + far * 0.35 - _Threshold) * _Contrast);

                // Rolling banks: the fog thickens and thins across the whole screen over time.
                float bank = 0.5 + 0.5 * sin(_HorrorTime * 0.13 + view.x * 0.8 + view.y * 0.5);
                float density = lerp(_BaseDensity, 1.0, wisps) * lerp(0.75, 1.0, bank);

                // Heavier toward the frame edge so the scene closes in.
                float aspect = _HorrorView.w / halfHeight;
                float edge = saturate(length(view / float2(max(aspect, 0.01), 1.0)) - 0.35);
                density = saturate(density + edge * _EdgeDensity);

                // Smoke peels away from an eye once it is fully out, so the stare stays readable.
                for (int e = 0; e < HORROR_MAX_EYES; e++)
                {
                    if (e < (int)_HorrorEyeCount)
                    {
                        float4 eye = _HorrorEyes[e];
                        float dist = length(world - eye.xy) / max(eye.z, 0.001);
                        float presence = smoothstep(0.55, 1.0, eye.w);
                        density *= 1.0 - exp(-dist * dist * 0.35) * presence * _EyeParting;
                    }
                }

                density = saturate(density + _HorrorFlicker * 0.5 - _HorrorSurge * 0.2);

                // Black smoke on a black scene is invisible, so the curling edges of each wisp catch a
                // trace of dim light. That is what lets the fog read as rolling volume.
                float edgeNear = smoothstep(0.08, 0.45, wisps) * (1.0 - smoothstep(0.45, 0.95, wisps));
                float edgeFar = smoothstep(0.35, 0.55, far) * (1.0 - smoothstep(0.55, 0.8, far));
                float edgeLight = saturate(edgeNear + edgeFar * 0.5) * _WispLightStrength * (1.0 - _HorrorFlicker);
                float3 color = lerp(_FogColor.rgb, _WispLight.rgb, saturate(edgeLight));
                return float4(color, saturate(density * _MaxAlpha + edgeLight * 0.25));
            }
            ENDHLSL
        }
    }
}
