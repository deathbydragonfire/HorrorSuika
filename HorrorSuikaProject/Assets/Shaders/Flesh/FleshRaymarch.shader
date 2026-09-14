Shader "Flesh/FleshRaymarch"
{
    Properties
    {
        _MacroColorVariation ("Macro Color Variation", Range(0, 1)) = 0.35
        _MottleColorVariation ("Mottle Color Variation", Range(0, 1)) = 0.2
        _MacroNoiseScale ("Macro Noise Scale", Float) = 1.5
        _MottleNoiseScale ("Mottle Noise Scale", Float) = 9.0
        _BaseRoughness ("Base Roughness", Range(0, 1)) = 0.35
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+100"
        }

        Pass
        {
            Name "FleshForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FleshSDF.hlsl"
            #include "FleshNoise.hlsl"

            #define FLESH_MAX_MARCH_STEPS 128

            #define FLESH_DEBUG_SHADED 0
            #define FLESH_DEBUG_DISTANCE 1
            #define FLESH_DEBUG_NORMAL 2
            #define FLESH_DEBUG_STEPCOUNT 3
            #define FLESH_DEBUG_LOCALPOSITION 4
            #define FLESH_DEBUG_INSTANCEID 5

            CBUFFER_START(UnityPerMaterial)
                float _MacroColorVariation;
                float _MottleColorVariation;
                float _MacroNoiseScale;
                float _MottleNoiseScale;
                float _BaseRoughness;
                float _AmbientStrength;
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

            float3 ShadeFlesh(float3 positionWS, float3 normalWS, float3 viewDirWS, float3 surfaceColor)
            {
                float macro = FleshFBM(positionWS * _MacroNoiseScale);
                float mottle = FleshFBM(positionWS * _MottleNoiseScale);

                // The tier colour is the albedo; the noise only modulates it so tiers stay readable.
                float3 albedo = surfaceColor;
                albedo *= lerp(1.0 - _MacroColorVariation, 1.0 + _MacroColorVariation, macro);

                // Bounded blend towards a bruised tint. A lerp factor outside [0,1] would extrapolate and blow out.
                float3 mottleTint = albedo * float3(1.12, 0.74, 0.78);
                albedo = lerp(albedo, mottleTint, saturate(mottle) * _MottleColorVariation);
                albedo = max(albedo, 0.0);

                Light mainLight = GetMainLight();
                float wrappedDiffuse = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                float3 diffuse = albedo * mainLight.color * wrappedDiffuse;

                // The analytic gradient is exact and continuous, so a tight lobe is safe here. The
                // previous broad sheen only existed to hide voxel-scale terracing from the baked field.
                float3 halfVector = normalize(mainLight.direction + viewDirWS);
                float smoothness = 1.0 - saturate(_BaseRoughness);
                float specularPower = exp2(smoothness * 9.0 + 1.0);
                float3 specular = mainLight.color * pow(saturate(dot(normalWS, halfVector)), specularPower) * smoothness;

                float3 ambient = albedo * SampleSH(normalWS) * _AmbientStrength;

                return diffuse + specular + ambient;
            }

            half4 frag(Varyings input, out float depthOut : SV_Depth) : SV_Target
            {
                depthOut = input.positionCS.z;

                float3 rayDirection;
                float3 rayOrigin;
                float backoff = length(_FleshBoundsMax.xyz - _FleshBoundsMin.xyz) + 1.0;

                if (unity_OrthoParams.w > 0.5)
                {
                    rayDirection = -normalize(float3(UNITY_MATRIX_V._m20, UNITY_MATRIX_V._m21, UNITY_MATRIX_V._m22));
                    rayOrigin = input.positionWS - rayDirection * backoff;
                }
                else
                {
                    rayDirection = normalize(input.positionWS - _WorldSpaceCameraPos);
                    rayOrigin = _WorldSpaceCameraPos;
                }

                float tNear;
                float tFar;
                if (_FleshCount <= 0 || !RayBox(rayOrigin, rayDirection, _FleshBoundsMin.xyz, _FleshBoundsMax.xyz, tNear, tFar))
                {
                    clip(-1);
                    return half4(0, 0, 0, 0);
                }

                float t = max(tNear, 0.0);
                float sdfDistance = FLESH_FAR_DISTANCE;
                int stepsTaken = 0;
                bool hit = false;

                [loop]
                for (int iteration = 0; iteration < FLESH_MAX_MARCH_STEPS; iteration++)
                {
                    if (iteration >= _FleshMaxSteps)
                    {
                        break;
                    }

                    sdfDistance = SceneSDF(rayOrigin + rayDirection * t);
                    stepsTaken++;

                    if (sdfDistance < _FleshSurfaceEpsilon)
                    {
                        hit = true;
                        break;
                    }

                    t += sdfDistance;

                    if (t > tFar)
                    {
                        break;
                    }
                }

                if (!hit)
                {
                    if (_FleshDebugMode == FLESH_DEBUG_STEPCOUNT)
                    {
                        float missLoad = stepsTaken / max((float)_FleshMaxSteps, 1.0);
                        return half4(missLoad * 0.5, 0, missLoad, 1);
                    }

                    clip(-1);
                    return half4(0, 0, 0, 0);
                }

                float3 hitPosition = rayOrigin + rayDirection * t;
                float4 hitClip = TransformWorldToHClip(hitPosition);
                depthOut = hitClip.z / hitClip.w;

                int nearestInstance;
                float3 gradient;
                float3 surfaceColor;
                SceneSDFWithSurface(hitPosition, gradient, surfaceColor, nearestInstance);
                float3 normalWS = normalize(gradient + 1e-8);

                if (_FleshDebugMode == FLESH_DEBUG_DISTANCE)
                {
                    float ramp = saturate(t / max(tFar, 1e-4));
                    return half4(ramp, 1.0 - ramp, 0, 1);
                }

                if (_FleshDebugMode == FLESH_DEBUG_NORMAL)
                {
                    return half4(normalWS * 0.5 + 0.5, 1);
                }

                if (_FleshDebugMode == FLESH_DEBUG_STEPCOUNT)
                {
                    float load = stepsTaken / max((float)_FleshMaxSteps, 1.0);
                    return half4(load, 1.0 - load, 0, 1);
                }

                if (_FleshDebugMode == FLESH_DEBUG_LOCALPOSITION)
                {
                    float4 sphere = _FleshSphere[nearestInstance];
                    float3 localPos = (hitPosition - sphere.xyz) / max(sphere.w, 1e-5);
                    return half4(saturate(localPos * 0.5 + 0.5), 1);
                }

                if (_FleshDebugMode == FLESH_DEBUG_INSTANCEID)
                {
                    float hue = frac(nearestInstance * 0.618034);
                    float3 idColor = saturate(abs(frac(hue + float3(0.0, 0.6666, 0.3333)) * 6.0 - 3.0) - 1.0);
                    return half4(idColor, 1);
                }

                float3 color = ShadeFlesh(hitPosition, normalWS, -rayDirection, surfaceColor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
