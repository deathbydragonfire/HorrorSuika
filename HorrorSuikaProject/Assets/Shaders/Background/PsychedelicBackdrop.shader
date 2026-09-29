Shader "HorrorSuika/PsychedelicBackdrop"
{
    Properties
    {
        [Header(Flesh)]
        _DeepColor ("Deep Color", Color) = (0.012, 0.0, 0.006, 1)
        _FleshColor ("Flesh Color", Color) = (0.32, 0.015, 0.03, 1)
        _BruiseColor ("Bruise Color", Color) = (0.09, 0.01, 0.14, 1)
        _PatternScale ("Pattern Scale", Float) = 1.25
        _FlowSpeed ("Flow Speed", Float) = 1

        [Header(Psychedelia)]
        _Psychedelia ("Psychedelia", Range(0, 1)) = 0.55
        _HueSpeed ("Hue Speed", Float) = 0.045
        _Swirl ("Vortex Swirl", Range(0, 4)) = 1.2

        [Header(Veins)]
        [HDR] _VeinColor ("Vein Color", Color) = (0.9, 0.02, 0.05, 1)
        _VeinScale ("Vein Scale", Float) = 2.6
        _VeinWidth ("Vein Width", Range(0.01, 0.2)) = 0.06
        _VeinStrength ("Vein Strength", Range(0, 1)) = 0.85
        _BeatStrength ("Heartbeat Strength", Range(0, 2)) = 1

        [Header(Blood And Sigils)]
        _Mandala ("Kaleidoscope Mandala", Range(0, 2)) = 0.6
        _MandalaSegments ("Mandala Segments", Range(3, 16)) = 7
        _DripColor ("Blood Drip Color", Color) = (0.28, 0.0, 0.01, 1)
        _DripAmount ("Blood Drips", Range(0, 1)) = 0.7

        [Header(Black Fog)]
        _FogColor ("Fog Color", Color) = (0.0, 0.0, 0.0, 1)
        _FogDensity ("Fog Density", Range(0, 1)) = 0.72
        _FogScale ("Fog Scale", Float) = 0.8
        _FogSpeed ("Fog Speed", Float) = 0.05

        [Header(Eyes)]
        _SocketDarkness ("Socket Darkness", Range(0, 1)) = 0.9
        _EyeHalo ("Eye Halo", Range(0, 2)) = 0.5
        _EyeTwist ("Eye Twist", Range(0, 3)) = 1.1

        [Header(Finish)]
        _Brightness ("Brightness", Range(0, 3)) = 1.15
        _SporeAmount ("Spores", Range(0, 1)) = 0.35
        _Vignette ("Vignette", Range(0, 2)) = 1.05
        _Grain ("Film Grain", Range(0, 0.2)) = 0.035
        _PlayfieldDim ("Playfield Dim", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Backdrop"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HorrorBackgroundCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _FleshColor;
                float4 _BruiseColor;
                float _PatternScale;
                float _FlowSpeed;
                float _Psychedelia;
                float _HueSpeed;
                float _Swirl;
                float4 _VeinColor;
                float _VeinScale;
                float _VeinWidth;
                float _VeinStrength;
                float _BeatStrength;
                float _Mandala;
                float _MandalaSegments;
                float4 _DripColor;
                float _DripAmount;
                float4 _FogColor;
                float _FogDensity;
                float _FogScale;
                float _FogSpeed;
                float _SocketDarkness;
                float _EyeHalo;
                float _EyeTwist;
                float _Brightness;
                float _SporeAmount;
                float _Vignette;
                float _Grain;
                float _PlayfieldDim;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            // Distance to the nearest Voronoi cell border. Borders read as a branching vein network.
            float VeinEdge(float2 p, float t)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                float f1 = 8.0;
                float f2 = 8.0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 g = float2(x, y);
                        float2 o = HorrorHash22(cell + g);
                        o = 0.5 + 0.42 * sin(t * 0.35 + 6.28318 * o);
                        float d = length(g + o - local);
                        if (d < f1)
                        {
                            f2 = f1;
                            f1 = d;
                        }
                        else if (d < f2)
                        {
                            f2 = d;
                        }
                    }
                }
                return f2 - f1;
            }

            // Vertical blood runs. Each column has its own speed, phase, and streak length; the bead
            // at the leading edge is wider than the trail, like a drop dragging its tail down glass.
            float BloodDrips(float2 p, float t)
            {
                float columns = 7.0;
                float column = floor(p.x * columns);
                float local = frac(p.x * columns) - 0.5;
                float seed = HorrorHash21(float2(column, 3.7));
                if (seed < 0.35)
                {
                    return 0.0;
                }

                float speed = lerp(0.04, 0.13, HorrorHash21(float2(column, 9.1)));
                float span = 3.2;
                float head = 1.3 - frac(t * speed + seed * 7.0) * span;
                float trail = lerp(0.35, 1.4, HorrorHash21(float2(column, 1.3)));
                float offset = (HorrorHash21(float2(column, 5.5)) - 0.5) * 0.5;
                float x = abs(local - offset + sin(p.y * 6.0 + seed * 20.0) * 0.03);
                float along = p.y - head;
                float thin = lerp(0.03, 0.012, saturate(along / trail));
                float streak = smoothstep(thin, thin * 0.3, x) * step(0.0, along) * smoothstep(trail, 0.0, along);
                float bead = smoothstep(0.075, 0.02, length(float2(x, along * 0.8)));
                return saturate(streak + bead);
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 world = input.positionWS.xy;
                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 0.0001);
                float tape = 1.0 - saturate(_VhsSuppress);

                // VHS tearing: random horizontal bands slide sideways while the glitch envelope is up.
                float glitchStep = floor(_HorrorTime * 20.0);
                float band = floor(screenUV.y * 26.0);
                float tearOn = step(0.62, HorrorHash21(float2(band * 1.7, glitchStep + 3.0)));
                world.x += (HorrorHash21(float2(band, glitchStep)) - 0.5) * tearOn * _HorrorGlitch * 1.4 * tape;

                float halfHeight = max(_HorrorView.z, 0.001);
                float2 p = (world - _HorrorView.xy) / halfHeight;
                float t = _HorrorTime * _FlowSpeed;
                float beat = _HorrorBeat * _BeatStrength;
                float surge = _HorrorSurge;

                // The whole membrane breathes with the heartbeat.
                p *= 1.0 - beat * 0.018;

                // Slow vortex, strongest at the centre. Surges wind it up.
                float radius = length(p);
                float swirlAmount = (_Swirl + surge * 2.5) * sin(t * 0.11) / (1.0 + radius * 1.4);
                p = HorrorRotate(p, swirlAmount);

                // Flesh twists around each eye and a shadow pool opens beneath it.
                float socket = 0.0;
                float halo = 0.0;
                for (int e = 0; e < HORROR_MAX_EYES; e++)
                {
                    if (e < (int)_HorrorEyeCount)
                    {
                        float4 eye = _HorrorEyes[e];
                        float eyeRadius = max(eye.z, 0.001);
                        float dist = length(world - eye.xy) / eyeRadius;
                        float presence = eye.w;
                        float2 eyeP = (eye.xy - _HorrorView.xy) / halfHeight;
                        float falloff = exp(-dist * dist * 0.18) * presence;
                        p = eyeP + HorrorRotate(p - eyeP, falloff * _EyeTwist * sin(t * 0.6 + e * 1.7));
                        socket = max(socket, smoothstep(2.4, 0.6, dist) * presence);
                        float ring = (dist - 1.35) * 3.0;
                        halo += exp(-ring * ring) * presence;
                    }
                }

                float2 sp = p * _PatternScale;

                // Two levels of domain warping give the slow oily churn.
                float2 q = float2(
                    HorrorFbm2(sp + float2(0.0, t * 0.09)),
                    HorrorFbm2(sp + float2(5.2, 1.3) - t * 0.07));
                float2 w = float2(
                    HorrorFbm2(sp + 3.6 * q + float2(1.7, 9.2) + t * 0.11),
                    HorrorFbm2(sp + 3.6 * q + float2(8.3, 2.8) - t * 0.09));
                float f = HorrorFbm2(sp + 3.2 * w);

                float3 color = lerp(_DeepColor.rgb, _FleshColor.rgb, smoothstep(0.25, 0.95, f));
                color = lerp(color, _BruiseColor.rgb, smoothstep(0.35, 1.05, length(q)) * 0.7);

                // Psychedelic bleed rides the warp field, so colour oozes along the currents.
                float hue = f * 1.4 + length(w) * 0.9 + _HorrorTime * _HueSpeed;
                float3 trip = HorrorPalette(hue);
                float tripMask = smoothstep(0.55, 1.1, length(w) + f * 0.35);
                float psychedelia = saturate(_Psychedelia + surge * 0.45);
                color = lerp(color, trip * (0.18 + 0.6 * f), tripMask * psychedelia);

                // Veins: warped Voronoi borders plus thin ridged capillaries. Both throb with the beat.
                float2 vp = p * _VeinScale + (q - 0.5) * 1.9;
                float edge = VeinEdge(vp, t);
                float width = _VeinWidth * (1.0 + beat * 0.6 + surge * 0.5);
                float vein = 1.0 - smoothstep(0.0, width, edge);
                float ridge = saturate(1.0 - abs(HorrorNoise2(p * 7.5 + w * 2.5) * 2.0 - 1.0));
                float capillary = pow(ridge, 14.0) * 0.55;
                // Veins gather in patches instead of netting the whole screen evenly.
                float veinPatch = smoothstep(0.32, 0.68, HorrorFbm2(p * 0.55 + float2(11.0, 3.0) + t * 0.02));
                float veinMask = saturate(vein + capillary) * _VeinStrength * lerp(0.15, 1.0, veinPatch);
                float3 veinColor = lerp(_VeinColor.rgb, HorrorPalette(hue + 0.5) * 0.8, psychedelia * 0.4);
                veinColor *= 0.45 + 1.4 * beat + surge;
                color = lerp(color, veinColor, veinMask);
                color += veinColor * exp(-edge * 16.0) * 0.12 * (0.4 + beat);

                // Faint kaleidoscopic sigil turning behind everything. Surges burn it in.
                float2 mp = HorrorRotate(p, t * 0.05 + surge * 0.8);
                float mr = length(mp);
                float segment = 6.28318 / max(_MandalaSegments, 3.0);
                float ma = abs(fmod(atan2(mp.y, mp.x) + 6.28318, segment) - segment * 0.5);
                float2 kp = float2(cos(ma), sin(ma)) * mr;
                float kaleido = HorrorFbm2(kp * 3.0 + float2(t * 0.2, 0.0));
                float rings = abs(sin(mr * 13.0 - t * 1.4 + kaleido * 6.0));
                float spokes = abs(sin(ma * 9.0 + kaleido * 4.0 - t * 0.6));
                float sigil = max(smoothstep(0.12, 0.0, rings), smoothstep(0.06, 0.0, spokes) * 0.5) * smoothstep(1.7, 0.15, mr);
                color += HorrorPalette(mr * 0.8 - t * 0.1 + kaleido) * sigil * _Mandala * (0.12 + surge * 1.1 + beat * 0.08);

                // Blood runs down the membrane, glossy at the bead.
                float drip = BloodDrips(p, t) * _DripAmount;
                color = lerp(color, _DripColor.rgb * (0.7 + beat * 0.5), drip);
                color += _DripColor.rgb * drip * pow(saturate(1.0 - abs(frac(p.y * 3.0 + t * 0.3) - 0.5) * 2.0), 8.0) * 0.6;

                // Drifting spores: sparse motes rising through the murk.
                float2 sporeP = p * 5.0 + float2(sin(t * 0.2) * 0.4, -t * 0.22);
                float2 sporeCell = floor(sporeP);
                float2 sporeOffset = HorrorHash22(sporeCell) * 0.8 + 0.1;
                float sporeDist = length(frac(sporeP) - sporeOffset);
                float spore = smoothstep(0.07, 0.0, sporeDist) * step(0.86, HorrorHash21(sporeCell + 7.13));
                color += spore * _SporeAmount * (0.35 + 0.65 * HorrorPalette(hue + sporeCell.x * 0.13));

                // Black fog swallows most of the membrane; colour only glows through the gaps.
                float2 fogP = (world - _HorrorView.xy) / halfHeight * _FogScale;
                float fogTime = _HorrorTime * _FogSpeed;
                float2 fogWarp = float2(HorrorFbm2(fogP + float2(fogTime, 0.0)), HorrorFbm2(fogP + float2(4.1, -fogTime)));
                float fog = HorrorFbm2(fogP * 1.3 + fogWarp * 2.2 + float2(-fogTime * 1.6, fogTime * 0.7));
                fog = smoothstep(0.3, 0.6, fog);
                float fogAmount = saturate(lerp(_FogDensity * 0.4, _FogDensity + 0.12, fog) - surge * 0.25);
                // The fog itself has faint body: slow billows of smoke a shade above black.
                float billow = HorrorFbm2(fogP * 2.4 - fogWarp * 1.8 + float2(fogTime * 2.0, -fogTime));
                float3 smoke = _FogColor.rgb + float3(0.022, 0.014, 0.018) * smoothstep(0.35, 0.8, billow) * (1.0 - fog * 0.5);
                color = lerp(color, smoke, fogAmount);

                // Shadow pool under each eye, with a faint iridescent corona at its edge.
                color *= 1.0 - socket * _SocketDarkness;
                color += HorrorPalette(_HorrorTime * 0.15 + halo) * float3(1.0, 0.35, 0.45) * halo * _EyeHalo * 0.35 * (1.0 - fogAmount * 0.6);

                // Keep the play area calm so items stay readable.
                float2 rectMin = _HorrorPlayfieldRect.xy;
                float2 rectMax = _HorrorPlayfieldRect.zw;
                if (rectMax.x > rectMin.x)
                {
                    float2 rectCentre = (rectMin + rectMax) * 0.5;
                    float2 rectHalf = (rectMax - rectMin) * 0.5;
                    float2 dq = abs(world - rectCentre) - rectHalf;
                    float sd = length(max(dq, 0.0)) + min(max(dq.x, dq.y), 0.0);
                    float inside = 1.0 - smoothstep(-0.35, 0.8, sd);
                    color *= lerp(1.0, _PlayfieldDim, inside);
                }

                color *= _Brightness * (1.0 + beat * 0.12);

                float aspect = _HorrorView.w / halfHeight;
                float2 centred = (screenUV - 0.5) * float2(aspect, 1.0);
                float vignette = smoothstep(1.15, 0.2, length(centred) * _Vignette);
                color *= vignette;

                color *= 1.0 - _HorrorFlicker;

                // Glitch tints the torn bands with a sick chromatic cast.
                color *= lerp(float3(1.0, 1.0, 1.0), float3(1.5, 0.4, 1.3), tearOn * _HorrorGlitch * tape);

                float grain = HorrorHash21(screenUV * _ScreenParams.xy + frac(floor(_HorrorTime * 24.0) * 0.073) * 91.7) - 0.5;
                color += grain * _Grain * tape;

                return float4(max(color, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
}
