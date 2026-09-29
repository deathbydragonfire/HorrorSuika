Shader "HorrorSuika/UIBloodDrip"
{
    // Procedural blood running down a UI button, built as one signed distance field so the seep,
    // drips, pool, and spill-over blend into each other like a single body of liquid.
    //
    // State comes from the Graphic's vertex colour, so no per-button material is needed:
    //   R = per-button seed, G = visibility, B = 1 when another button sits below (drips collide,
    //   pool on its top edge, then spill over), A = how far the blood has run (0..1).
    //
    // The rect spans the hovered button and hangs below it. Distances are in units of rect height,
    // measured down from the button's top edge.
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _BloodColor ("Blood", Color) = (0.42, 0.0, 0.02, 1)
        _DeepColor ("Thick Blood", Color) = (0.16, 0.0, 0.01, 1)
        _HighlightColor ("Wet Highlight", Color) = (0.95, 0.2, 0.18, 1)
        _Aspect ("Rect Width / Height", Float) = 2.26
        _SurfaceY ("Next Button Top (of rect height)", Range(0, 1)) = 0.556
        _Face ("Button Face (xMin, top, xMax, bottom)", Vector) = (0, 0, 2.26, 0.435)
        _CornerRadius ("Button Corner Radius", Float) = 0
        _Columns ("Drip Columns", Range(4, 40)) = 14
        _DripChance ("Drip Chance", Range(0, 1)) = 0.6
        _Thickness ("Drip Thickness", Range(0.005, 0.08)) = 0.034
        _RimDepth ("Top Seep Depth", Range(0, 0.3)) = 0.1
        _PoolAmount ("Pooling", Range(0, 3)) = 1
        _SpillThreshold ("Spill Threshold", Range(0.005, 0.1)) = 0.028
        _DropRate ("Falling Drops Per Second", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _BloodColor;
            float4 _DeepColor;
            float4 _HighlightColor;
            float _Aspect;
            float _SurfaceY;
            float4 _Face;
            float _CornerRadius;
            float _Columns;
            float _DripChance;
            float _Thickness;
            float _RimDepth;
            float _PoolAmount;
            float _SpillThreshold;
            float _DropRate;

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

            float Hash(float n)
            {
                return frac(sin(n * 127.1) * 43758.5453);
            }

            float Noise(float x)
            {
                float i = floor(x);
                float f = frac(x);
                return lerp(Hash(i), Hash(i + 1.0), f * f * (3.0 - 2.0 * f));
            }

            // Polynomial smooth minimum: joins shapes with a liquid fillet instead of a hard seam.
            float SmoothMin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            // Distance to a vertical drip from y = top to y = bottom that thickens toward its tip.
            float DripDistance(float2 p, float x, float top, float bottom, float rTop, float rBottom)
            {
                float y = clamp(p.y, top, bottom);
                float along = saturate((y - top) / max(bottom - top, 0.0001));
                return length(p - float2(x, y)) - lerp(rTop, rBottom, along);
            }

            struct Drip
            {
                bool exists;
                float x;
                float radius;
                float length;   // drawn length, stopped at the surface when there is one
                float volume;   // blood that reached the surface and is feeding the pool
                float speed;
            };

            Drip GetDrip(float column, float seed, float run, float hasSurface)
            {
                Drip d;
                float columnWidth = _Aspect / _Columns;
                d.exists = Hash(column + seed) < _DripChance;
                d.x = (column + 0.5 + (Hash(column * 5.3 + seed) - 0.5) * 0.5) * columnWidth;
                d.radius = _Thickness * lerp(0.75, 1.3, Hash(column * 3.17 + seed));
                // Only run from the flat part of the edge, never out of a rounded corner.
                float margin = _CornerRadius + d.radius * 1.2;
                d.exists = d.exists && d.x > _Face.x + margin && d.x < _Face.z - margin;
                d.speed = lerp(0.6, 1.4, Hash(column * 2.31 + seed));

                float lengthRoll = Hash(column * 1.73 + seed);
                float target = lerp(0.22, 1.05, lengthRoll * lengthRoll);
                float reach = saturate((run - 0.12) * d.speed / 0.55);
                float flowing = target * reach * reach * (3.0 - 2.0 * reach);

                if (hasSurface > 0.5)
                {
                    // Stops at the next button's edge. Whatever would have run further, plus a
                    // steady feed while the button stays hovered, becomes pool volume.
                    d.length = min(flowing, _SurfaceY);
                    float hit = step(_SurfaceY, target);
                    d.volume = (max(0.0, flowing - _SurfaceY) + saturate(run - 0.5) * 0.35 * hit) * _PoolAmount;
                }
                else
                {
                    d.length = flowing;
                    d.volume = 0.0;
                }

                if (!d.exists)
                {
                    d.volume = 0.0;
                }

                return d;
            }

            // Distance to an ellipse (approximate, good enough for soft liquid edges).
            float EllipseDistance(float2 p, float2 centre, float2 radii)
            {
                float2 q = (p - centre) / radii;
                return (length(q) - 1.0) * min(radii.x, radii.y);
            }

            // Every drip that lands on the next button spreads into a flat puddle that widens as it
            // delivers more blood. Neighbouring puddles melt into one uneven pool. Returns the pool's
            // distance field; depthAtX is how deep the pool is at x (drives the spill-over).
            float PoolField(float2 p, float column, float seed, float run, float x, out float depthAtX)
            {
                float field = 1.0;
                depthAtX = 0.0;
                for (int k = -3; k <= 3; k++)
                {
                    Drip d = GetDrip(column + k, seed, run, 1.0);
                    if (d.volume <= 0.0)
                    {
                        continue;
                    }

                    float halfWidth = d.radius * 1.2 + d.volume * 0.5;
                    float depth = min(d.radius * 0.5 + d.volume * 0.06, 0.05);
                    // Slightly off-centre so puddles are lopsided like real spatter.
                    float centreX = d.x + (Hash((column + k) * 8.3 + seed) - 0.5) * halfWidth * 0.4;
                    // Sits on the edge: a shallow dome above it, a film over the front below it.
                    float2 upper = float2(halfWidth, depth * 0.7);
                    float2 lower = float2(halfWidth * 0.92, depth);
                    float puddle = p.y < _SurfaceY
                        ? EllipseDistance(p, float2(centreX, _SurfaceY), upper)
                        : EllipseDistance(p, float2(centreX, _SurfaceY), lower);
                    field = SmoothMin(field, puddle, 0.018);

                    float dx = saturate(1.0 - abs(x - centreX) / halfWidth);
                    depthAtX = max(depthAtX, depth * sqrt(dx));
                }

                // Ragged rim so the pool edge is never a clean curve.
                field += (Noise(p.x * 40.0 + seed) - 0.5) * 0.004;
                return field;
            }

            float BloodField(float2 p, float seed, float run, float hasSurface, float t)
            {
                float columnWidth = _Aspect / _Columns;
                float column = floor(p.x / columnWidth);

                // Uneven wet band seeping along the hovered button's top edge.
                float seepShape = 0.35 + Noise(p.x * 7.0 + seed) * 0.8 + Noise(p.x * 23.0 + seed * 1.7) * 0.25;
                float seep = _RimDepth * seepShape * saturate(run * 3.0);
                float field = p.y - seep;

                // Drips from this column and its neighbours (a fat bead can cross a column edge).
                for (int k = -1; k <= 1; k++)
                {
                    Drip d = GetDrip(column + k, seed, run, hasSurface);
                    if (!d.exists || d.length < 0.005)
                    {
                        continue;
                    }

                    float taper = lerp(0.35, 0.55, Hash((column + k) * 11.3 + seed));
                    float drip = DripDistance(p, d.x, 0.0, d.length, d.radius * taper, d.radius * 0.75);
                    bool landed = hasSurface > 0.5 && d.length >= _SurfaceY - 0.001;
                    if (!landed)
                    {
                        // A heavy bead gathers at the hanging tip.
                        float bead = length((p - float2(d.x, d.length)) * float2(1.0, 0.9)) - d.radius * 1.15;
                        drip = SmoothMin(drip, bead, 0.012);

                        // With nothing below, a drop sometimes breaks off and falls.
                        if (hasSurface < 0.5 && run > 0.98 && _DropRate > 0.0)
                        {
                            float fall = frac(t * _DropRate * d.speed + Hash((column + k) * 7.7 + seed) * 10.0);
                            float dropY = d.length + fall * fall * 1.4;
                            float drop = length((p - float2(d.x, dropY)) * float2(1.0, 0.7)) - d.radius * 0.8;
                            drip = min(drip, fall < 0.95 ? drop : 1.0);
                        }
                    }

                    field = SmoothMin(field, drip, 0.016);
                }

                if (hasSurface > 0.5)
                {
                    float columnCentre = (column + 0.5) * columnWidth;
                    float depthHere;
                    float pool = PoolField(p, column, seed, run, columnCentre, depthHere);
                    field = SmoothMin(field, pool, 0.012);

                    // Where the pool is deep enough it spills over the front of the lower button.
                    float overflow = saturate((depthHere - _SpillThreshold) / _SpillThreshold);
                    if (overflow > 0.0)
                    {
                        float spillX = (column + 0.5 + (Hash(column * 9.1 + seed) - 0.5) * 0.6) * columnWidth;
                        float spillRadius = _Thickness * lerp(0.6, 1.0, Hash(column * 4.4 + seed));
                        float spillLength = overflow * saturate((run - 0.6) / 0.4) * lerp(0.12, 0.4, Hash(column * 6.2 + seed));
                        if (spillLength > 0.004)
                        {
                            float spillTop = _SurfaceY + depthHere * 0.6;
                            float spillEnd = spillTop + spillLength;
                            float spill = DripDistance(p, spillX, spillTop, spillEnd, spillRadius * 0.45, spillRadius * 0.7);
                            float bead = length((p - float2(spillX, spillEnd)) * float2(1.0, 0.9)) - spillRadius * 1.05;
                            field = SmoothMin(field, SmoothMin(spill, bead, 0.008), 0.012);
                        }
                    }
                }

                // Keep the blood on the button: inside its rounded face, or hanging below the flat
                // part of its bottom edge. The two regions overlap so there is no seam between them.
                float2 faceCentre = float2(_Face.x + _Face.z, _Face.y + _Face.w) * 0.5;
                float2 faceHalf = float2(_Face.z - _Face.x, _Face.w - _Face.y) * 0.5;
                float corner = min(_CornerRadius, min(faceHalf.x, faceHalf.y));
                float2 q = abs(p - faceCentre) - (faceHalf - corner);
                float face = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner;
                float hangSpan = max((_Face.x + corner) - p.x, p.x - (_Face.z - corner));
                float hang = max(hangSpan, (_Face.w - corner) - p.y);
                return max(field, min(face, hang));
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
                float visible = saturate(input.color.g);
                if (visible <= 0.001)
                {
                    return 0;
                }

                float seed = input.color.r * 91.7;
                float hasSurface = input.color.b;
                float run = saturate(input.color.a);
                float t = fmod(_Time.y, 1000.0);

                float2 p = float2(input.uv.x * _Aspect, 1.0 - input.uv.y);
                float field = BloodField(p, seed, run, hasSurface, t);

                float aa = max(fwidth(field), 0.0015);
                float alpha = 1.0 - smoothstep(-aa, aa, field);
                if (alpha <= 0.001)
                {
                    return 0;
                }

                // Blood is flat and dark: slightly translucent and brighter where the film is thin,
                // deep red where it is thick, with only small wet glints rather than tube shading.
                float depth = saturate(-field * 14.0);
                float3 color = lerp(_BloodColor.rgb, _DeepColor.rgb, depth);
                float2 offset = float2(0.003, -0.003);
                float slope = BloodField(p + offset, seed, run, hasSurface, t) - field;
                float glint = pow(saturate(slope * 220.0 - 0.35), 3.0) * saturate(depth * 2.0);
                color = lerp(color, _HighlightColor.rgb, glint * 0.45);
                alpha *= lerp(0.72, 1.0, saturate(depth * 3.0));

                return float4(color, alpha * visible * 0.96);
            }
            ENDHLSL
        }
    }
}
