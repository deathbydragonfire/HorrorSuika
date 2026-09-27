Shader "Flesh/FleshRaymarch"
{
    Properties
    {
        _MacroColorVariation ("Macro Color Variation", Range(0, 1)) = 0.12
        _MottleColorVariation ("Mottle Color Variation", Range(0, 1)) = 0.35
        _MacroNoiseScale ("Macro Noise Scale", Float) = 3.2
        _MottleNoiseScale ("Mottle Noise Scale", Float) = 6.5
        _BaseRoughness ("Base Roughness", Range(0, 1)) = 0.88
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.6
        _PoreNoiseScale ("Pore Noise Scale", Float) = 16
        _PoreColorVariation ("Pore Color Variation", Range(0, 1)) = 0.08
        _PoreNormalStrength ("Pore Normal Strength", Range(0, 3)) = 0.28
        _SpecularStrength ("Specular Strength", Range(0, 2)) = 0.7
        _SpecularBreakup ("Specular Breakup", Range(0, 3)) = 1.6
        _SubsurfaceStrength ("Subsurface Strength", Range(0, 2)) = 1.15
        _SubsurfaceWrap ("Subsurface Wrap", Range(0, 1)) = 0.85
        _Translucency ("Translucency", Range(0, 1)) = 0.42
        _ThicknessDensity ("Thickness Density", Float) = 1.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
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
            Blend SrcAlpha OneMinusSrcAlpha

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
                float _PoreNoiseScale;
                float _PoreColorVariation;
                float _PoreNormalStrength;
                float _SpecularStrength;
                float _SpecularBreakup;
                float _SubsurfaceStrength;
                float _SubsurfaceWrap;
                float _Translucency;
                float _ThicknessDensity;
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

            // Chord length through the nearest sphere along the view ray. Thin necks and silhouette
            // edges come out short, so scatter and translucency concentrate there.
            float EstimateViewThickness(float3 hitPosition, float3 rayDirection, int nearestInstance)
            {
                float4 sphere = _FleshSphere[nearestInstance];
                float3 toCenter = sphere.xyz - hitPosition;
                float axis = dot(toCenter, rayDirection);
                float3 offAxis = toCenter - rayDirection * axis;
                float halfChordSq = sphere.w * sphere.w - dot(offAxis, offAxis);
                return max(axis + sqrt(max(halfChordSq, 0.0)), 0.0);
            }

            // Gradient of one noise octave, used to tilt the shading normal so pores break up the highlight.
            float3 NoiseGradient(float3 p)
            {
                float h = 0.16;
                float n0 = FleshValueNoise(p);
                float dx = FleshValueNoise(p + float3(h, 0.0, 0.0)) - n0;
                float dy = FleshValueNoise(p + float3(0.0, h, 0.0)) - n0;
                float dz = FleshValueNoise(p + float3(0.0, 0.0, h)) - n0;
                return float3(dx, dy, dz);
            }

            float4 ShadeFlesh(float3 positionWS, float3 normalWS, float3 viewDirWS, float3 surfaceColor, float thickness)
            {
                float macro = FleshFBM(positionWS * _MacroNoiseScale);
                float blotch = FleshFBM(positionWS * _MottleNoiseScale + 4.2);
                float pores = FleshFBM(positionWS * _PoreNoiseScale);

                // The tier colour stays in charge. Blotches only nudge paler or slightly flushed,
                // and the pore grain is a small brightness ripple so the surface stays skin, not grit.
                float3 albedo = surfaceColor;
                albedo *= lerp(1.0 - _MacroColorVariation, 1.0 + _MacroColorVariation, macro);

                float3 pale = albedo * float3(1.05, 1.02, 0.99);
                float3 flushed = albedo * float3(0.96, 0.9, 0.88);
                albedo = lerp(albedo, lerp(flushed, pale, blotch), _MottleColorVariation);
                albedo *= lerp(1.0 - _PoreColorVariation, 1.0 + _PoreColorVariation, pores);
                albedo = max(albedo, 0.0);

                float3 poreGradient = NoiseGradient(positionWS * max(_PoreNoiseScale, 1.0));
                normalWS = normalize(normalWS - poreGradient * _PoreNormalStrength);

                Light mainLight = GetMainLight();
                float ndotl = dot(normalWS, mainLight.direction);
                float wrap = saturate(_SubsurfaceWrap);
                float wrappedDiffuse = saturate((ndotl + wrap) / (1.0 + wrap));
                float3 diffuse = albedo * mainLight.color * wrappedDiffuse;

                // Scatter stays in the tier's own hue, pulled a little deeper, so the red undertone
                // glows warm and the sallow tiers stay skin-coloured.
                float3 scatterTint = lerp(albedo, albedo * float3(1.15, 0.55, 0.42), 0.45);
                float thin = exp(-thickness * max(_ThicknessDensity, 0.0));
                float3 lightThrough = normalize(mainLight.direction + normalWS * 0.4);
                float transmission = pow(saturate(dot(viewDirWS, -lightThrough)), 2.2);
                float subsurfaceAmount = saturate(_SubsurfaceStrength);
                float3 subsurface = scatterTint * mainLight.color * subsurfaceAmount * (transmission * thin * 0.4 + wrappedDiffuse * 0.22 * thin);

                // The diffuse normal stays smooth. A finer tilt is used only for the reflection,
                // so the highlight shards into skin glints instead of one point on the sphere.
                float specScale = max(_PoreNoiseScale, 1.0) * 1.8;
                float3 specGradient = NoiseGradient(positionWS * specScale);
                float fleck = FleshValueNoise(positionWS * specScale * 0.73 + 11.0);
                float3 specNormal = normalize(normalWS - specGradient * _SpecularBreakup);

                float3 halfVector = normalize(mainLight.direction + viewDirWS);
                float roughness = saturate(_BaseRoughness + (fleck - 0.5) * 0.28);
                float smoothness = 1.0 - roughness;
                float specularPower = exp2(smoothness * 8.0 + 1.0);
                float lobe = pow(saturate(dot(specNormal, halfVector)), specularPower);
                float specMask = lerp(0.2, 1.0, fleck);
                float3 specTint = lerp(albedo, 1.0, 0.4);
                float3 specular = specTint * mainLight.color * lobe * smoothness * _SpecularStrength * specMask;

                float3 ambient = albedo * SampleSH(normalWS) * _AmbientStrength;
                float3 color = diffuse + specular + ambient + subsurface;

                // Slight see-through, stronger on thin edges and where light already passes through.
                float edge = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 2.0);
                float presence = saturate(thin * 0.7 + edge * 0.45);
                float alpha = lerp(1.0, lerp(0.9, 0.68, presence), saturate(_Translucency));

                return float4(color, alpha);
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

                float thickness = EstimateViewThickness(hitPosition, rayDirection, nearestInstance);
                float4 shaded = ShadeFlesh(hitPosition, normalWS, -rayDirection, surfaceColor, thickness);
                return half4(shaded);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
