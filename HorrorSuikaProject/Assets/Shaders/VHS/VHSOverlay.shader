Shader "HorrorSuika/VHSOverlay"
{
    // Transparent layer drawn on a top-most Screen Space Overlay canvas so the tape look and the
    // vignette also cover UI, which the full-screen pass cannot reach. It only darkens (scanlines,
    // vignette, a faint tracking shadow) so it never lifts the blacks; the one exception is a rare
    // dropout streak. Distortion and chroma smear come from VHSFullscreen.
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0, 1)) = 1
        _Scanlines ("Scanlines", Range(0, 1)) = 0.1
        _TrackingStrength ("Tracking Band Shadow", Range(0, 1)) = 0.15
        _TrackingSpeed ("Tracking Speed", Float) = 0.06
        _Dropouts ("Dropouts", Range(0, 1)) = 0.3
        _Vignette ("Vignette", Range(0, 1)) = 0.6
        _VignetteFalloff ("Vignette Falloff", Range(0.05, 1)) = 0.28
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Intensity;
            float _Scanlines;
            float _TrackingStrength;
            float _TrackingSpeed;
            float _Dropouts;
            float _Vignette;
            float _VignetteFalloff;
            float _HorrorGlitch;
            float _VhsSuppress;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            float VhsHash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float t = fmod(_Time.y, 1000.0);
                float glitch = saturate(_HorrorGlitch);

                // During regular gameplay only the vignette remains; the tape artefacts fade out.
                float tape = 1.0 - saturate(_VhsSuppress);

                float scan = 0.5 + 0.5 * sin(uv.y * 360.0 * 6.28318);
                float darkness = scan * _Scanlines * 0.35 * tape;

                // Rounded-rectangle vignette: it hugs the screen edges and corners instead of
                // drawing an oval, so the middle never looks like a lit disc.
                float edge = uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y) * 16.0;
                float vignette = 1.0 - pow(saturate(edge), _VignetteFalloff);
                darkness += vignette * _Vignette;

                float bandY = frac(t * _TrackingSpeed);
                float bandDist = abs(uv.y - bandY);
                bandDist = min(bandDist, 1.0 - bandDist);
                darkness += exp(-bandDist * bandDist * 1800.0) * _TrackingStrength * 0.3 * tape;

                float dropRow = floor(uv.y * 180.0);
                float dropSeed = VhsHash(float2(dropRow, floor(t * 24.0)));
                float dropX = VhsHash(float2(dropRow * 7.3, floor(t * 24.0)));
                float dropout = step(0.996 - glitch * 0.03, dropSeed) * smoothstep(0.04, 0.0, abs(uv.x - dropX)) * _Dropouts * tape;

                float alpha = saturate(dropout > 0.001 ? dropout : darkness) * _Intensity * input.color.a;
                float3 color = dropout > 0.001 ? float3(0.85, 0.83, 0.9) : float3(0.0, 0.0, 0.0);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
