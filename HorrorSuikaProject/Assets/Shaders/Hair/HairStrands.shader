Shader "HorrorSuika/HairStrands"
{
    Properties
    {
        _Hairiness ("Hairiness", Range(0, 1)) = 1
        _HairColor ("Hair Color", Color) = (0.055, 0.032, 0.018, 1)
        _HairHighlight ("Hair Highlight", Color) = (0.24, 0.13, 0.06, 1)
        _MinPixelWidth ("Min Pixel Width", Range(1, 3)) = 1.7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+110"
        }

        Pass
        {
            Name "HairForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Hairiness;
                float4 _HairColor;
                float4 _HairHighlight;
                float _MinPixelWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 growWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float along : TEXCOORD2;
                float shade : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float along = input.uv.y;
                float side = input.uv.x;
                float threshold = input.uv2.x;
                float halfWidth = input.uv2.y * 0.5;
                float coverage = saturate(_Hairiness);
                // Same stroke at every percent. Hairiness only decides which evenly ranked strands stay.
                float visible = step(threshold, coverage) * step(0.001, coverage);

                float taper = lerp(1.0, 0.0, smoothstep(0.72, 1.0, along));

                float3 growOS = input.tangentOS.xyz;
                float3 centerWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 growWS = TransformObjectToWorldDir(growOS);
                float growLen = length(growWS);
                growWS = growLen > 1e-5 ? growWS / growLen : float3(0.0, 1.0, 0.0);

                float3 viewWS = GetWorldSpaceNormalizeViewDir(centerWS);
                float3 sideWS = cross(growWS, viewWS);
                float sideLen = length(sideWS);
                sideWS = sideLen > 1e-4 ? sideWS / sideLen : float3(1.0, 0.0, 0.0);

                float worldScale = length(TransformObjectToWorldDir(float3(1.0, 0.0, 0.0)));
                float worldHalf = halfWidth * worldScale;

                // Small blobs would otherwise shrink hairs under a pixel. Large ones keep the authored width.
                float probe = 0.05;
                float4 clipCenter = TransformWorldToHClip(centerWS);
                float4 clipProbe = TransformWorldToHClip(centerWS + sideWS * probe);
                float2 pixelDelta = (clipProbe.xy / max(clipProbe.w, 1e-5) - clipCenter.xy / max(clipCenter.w, 1e-5));
                pixelDelta *= _ScreenParams.xy * 0.5;
                float pixelsPerUnit = length(pixelDelta) / probe;
                float minHalf = (_MinPixelWidth * 0.5) / max(pixelsPerUnit, 0.001);
                worldHalf = max(worldHalf, minHalf) * taper * visible;

                float3 positionWS = centerWS + sideWS * (side * worldHalf);
                // A bare blob collapses the coat so it cannot leave hairline fragments.
                float3 hiddenWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                positionWS = lerp(hiddenWS, positionWS, visible);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.growWS = growWS;
                output.positionWS = positionWS;
                output.along = along;
                output.shade = input.color.r;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Light mainLight = GetMainLight();
                float3 tangent = normalize(input.growWS);
                float ndotl = dot(tangent, mainLight.direction);
                float wrap = saturate(ndotl * 0.35 + 0.62);

                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 halfDir = normalize(mainLight.direction + viewDir);
                float sinTH = sqrt(saturate(1.0 - dot(tangent, halfDir) * dot(tangent, halfDir)));
                float spec = pow(sinTH, 28.0) * input.shade;

                float3 color = lerp(_HairColor.rgb, _HairHighlight.rgb, saturate(spec * 0.85 + wrap * 0.22));
                color *= lerp(0.78, 1.0, wrap);
                color *= lerp(0.82, 1.0, smoothstep(0.0, 0.2, input.along));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
