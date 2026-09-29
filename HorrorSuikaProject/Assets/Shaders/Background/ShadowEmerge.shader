Shader "HorrorSuika/ShadowEmerge"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (0.88, 0.8, 0.76, 1)
        _AmbientColor ("Ambient", Color) = (0.06, 0.012, 0.02, 1)
        _DiffuseStrength ("Diffuse Strength", Range(0, 2)) = 0.8
        _SpecularStrength ("Wet Specular", Range(0, 4)) = 1.6
        _SpecularPower ("Specular Power", Range(4, 256)) = 90
        [HDR] _RimColor ("Rim Color", Color) = (1.2, 0.15, 0.2, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimHueMix ("Rim Psychedelic Mix", Range(0, 1)) = 0.6

        [Header(Shadow Emergence)]
        _Emerge ("Emerge", Range(0, 1)) = 1
        _MaxReveal ("Max Reveal", Range(0, 1)) = 0.5
        _SurgeReveal ("Surge Extra Reveal", Range(0, 1)) = 0.25
        _ShadowVeil ("Drifting Shadow Veil", Range(0, 1)) = 0.65
        _EmergeSoftness ("Emerge Softness", Range(0.05, 2)) = 0.6
        _WispScale ("Shadow Wisp Scale", Float) = 12
        _WispStrength ("Shadow Wisp Strength", Range(0, 2)) = 0.8
        _EyeCenter ("Eye Centre (xyz) Radius (w)", Vector) = (0, 0, 0, 1)
        _Hue ("Hue Offset", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ShadowEmerge"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HorrorBackgroundCommon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _AmbientColor;
                float _DiffuseStrength;
                float _SpecularStrength;
                float _SpecularPower;
                float4 _RimColor;
                float _RimPower;
                float _RimHueMix;
                float _Emerge;
                float _MaxReveal;
                float _SurgeReveal;
                float _ShadowVeil;
                float _EmergeSoftness;
                float _WispScale;
                float _WispStrength;
                float4 _EyeCenter;
                float _Hue;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 towardCamera = normalize(UNITY_MATRIX_V[2].xyz);

                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;

                Light mainLight = GetMainLight();
                float wrapped = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                float3 diffuse = albedo * (_AmbientColor.rgb + wrapped * wrapped * mainLight.color * _DiffuseStrength);

                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float specular = pow(saturate(dot(normalWS, halfDir)), _SpecularPower) * _SpecularStrength;

                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower);
                float3 rimTint = lerp(_RimColor.rgb, HorrorPalette(_HorrorTime * 0.12 + _Hue) * 1.4, _RimHueMix);
                float3 rim = rimTint * fresnel * (0.5 + _HorrorBeat * 0.8 + _HorrorSurge);

                // Shadow emergence: a smoky front sweeps from the camera-facing surface toward the back.
                // The cornea surfaces first and the rim stays swallowed until the eye is fully out.
                float eyeRadius = max(_EyeCenter.w, 0.0001);
                float frontness = dot(input.positionWS - _EyeCenter.xyz, towardCamera) / eyeRadius;
                float wisp = HorrorFbm3(input.positionOS * _WispScale / eyeRadius + float3(0.0, _HorrorTime * 0.35, _HorrorTime * 0.2)) - 0.5;
                // Even fully surfaced it never comes all the way out: Max Reveal stops the front short,
                // so only the most camera-facing part catches light and the edges stay in the dark.
                // A surge lets it lean further out.
                float reveal = saturate(_MaxReveal + _SurgeReveal * _HorrorSurge) * _Emerge;
                float threshold = lerp(1.25, -1.35, reveal);
                float lit = smoothstep(threshold, threshold + _EmergeSoftness, frontness + wisp * _WispStrength);
                lit *= smoothstep(0.0, 0.25, _Emerge);

                // Slow shadow drifting across the whole silhouette in world space, as if smoke were
                // passing in front of it.
                float2 veilP = input.positionWS.xy * 0.45 + float2(_HorrorTime * 0.05, -_HorrorTime * 0.03);
                float veil = smoothstep(0.35, 0.7, HorrorFbm2(veilP + HorrorFbm2(veilP * 1.7) * 1.5));
                lit *= 1.0 - veil * _ShadowVeil;

                float3 color = (diffuse + rim) * lit + specular * mainLight.color * lit * lit;

                // Deeper apparitions sink further into the black, so near and far read apart.
                float viewDistance = -TransformWorldToView(input.positionWS).z;
                float depthRange = max(_HorrorDepthFog.y - _HorrorDepthFog.x, 0.001);
                float depthFog = saturate((viewDistance - _HorrorDepthFog.x) / depthRange);
                color *= 1.0 - lerp(_HorrorDepthFog.w, _HorrorDepthFog.z, depthFog * depthFog);

                // In a blackout only the wet glint survives, like eyes catching the last light.
                color *= 1.0 - _HorrorFlicker * 0.85;
                color += specular * _HorrorFlicker * lit * 0.6;

                return float4(max(color, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
}
