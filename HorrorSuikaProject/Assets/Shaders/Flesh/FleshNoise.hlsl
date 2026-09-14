#ifndef FLESH_NOISE_INCLUDED
#define FLESH_NOISE_INCLUDED

#define FLESH_FBM_OCTAVES 3

/// Cheap deterministic hash of a lattice cell to [0, 1). GLES3-safe (no bit ops, no textures).
/// Multiplicative form rather than sin-based, which would show axis-aligned correlation artefacts.
float FleshHash(float3 cell)
{
    float3 p = frac(cell * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

/// Trilinearly interpolated 3D value noise in [0, 1].
float FleshValueNoise(float3 p)
{
    float3 cell = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float c000 = FleshHash(cell + float3(0.0, 0.0, 0.0));
    float c100 = FleshHash(cell + float3(1.0, 0.0, 0.0));
    float c010 = FleshHash(cell + float3(0.0, 1.0, 0.0));
    float c110 = FleshHash(cell + float3(1.0, 1.0, 0.0));
    float c001 = FleshHash(cell + float3(0.0, 0.0, 1.0));
    float c101 = FleshHash(cell + float3(1.0, 0.0, 1.0));
    float c011 = FleshHash(cell + float3(0.0, 1.0, 1.0));
    float c111 = FleshHash(cell + float3(1.0, 1.0, 1.0));

    float x00 = lerp(c000, c100, f.x);
    float x10 = lerp(c010, c110, f.x);
    float x01 = lerp(c001, c101, f.x);
    float x11 = lerp(c011, c111, f.x);

    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
}

/// Three-octave fractal Brownian motion in [0, 1].
float FleshFBM(float3 p)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float normalization = 0.0;

    [unroll]
    for (int i = 0; i < FLESH_FBM_OCTAVES; i++)
    {
        sum += FleshValueNoise(p) * amplitude;
        normalization += amplitude;
        p *= 2.03;
        amplitude *= 0.5;
    }

    return sum / max(normalization, 1e-5);
}

#endif
