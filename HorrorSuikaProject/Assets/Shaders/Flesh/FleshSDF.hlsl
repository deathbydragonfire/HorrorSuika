#ifndef FLESH_SDF_INCLUDED
#define FLESH_SDF_INCLUDED

// Must match FleshRenderer.MaxInstances.
#define MAX_FLESH_INSTANCES 48

#define FLESH_FAR_DISTANCE 1.0e6
#define FLESH_MIN_BLEND 1.0e-4

// Per-instance data, uploaded as fixed-length global arrays (no StructuredBuffer on WebGL2).
// _FleshSphere : (worldCentre.xyz, worldRadius)
// _FleshColor  : (surfaceColour.rgb, blendRadius)
//
// The colour rides along in the same array as the blend radius to keep the upload to two vectors
// per instance. Colour is uploaded already converted to the active colour space.
//
// The underlying form is always a sphere, so the field is analytic: no baked volume, no 3D
// texture fetch, no rotation (a sphere is rotation invariant) and no voxel quantisation.
// Layered detail such as eyes or ears is conventional geometry drawn on top, not part of this field.
float4 _FleshSphere[MAX_FLESH_INSTANCES];
float4 _FleshColor[MAX_FLESH_INSTANCES];

// Cluster AABB in world space, used for the analytic ray entry/exit test.
float4 _FleshBoundsMin;
float4 _FleshBoundsMax;

int _FleshCount;
int _FleshMaxSteps;
int _FleshDebugMode;
float _FleshSurfaceEpsilon;

/// Exact signed distance in world units to a single instance.
float SphereSDF(float3 worldPos, int i)
{
    float4 sphere = _FleshSphere[i];
    return length(worldPos - sphere.xyz) - sphere.w;
}

/// Polynomial smooth minimum. k is the blend radius in world units. Reports the blend weight so
/// callers can carry a gradient through the same recurrence.
float SmoothMinWeighted(float a, float b, float k, out float h)
{
    k = max(k, FLESH_MIN_BLEND);
    h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

/// Polynomial smooth minimum. k is the blend radius in world units.
float SmoothMin(float a, float b, float k)
{
    float h;
    return SmoothMinWeighted(a, b, k, h);
}

/// Smooth union of every active instance. Exact everywhere, so sphere tracing takes full strides.
float SceneSDF(float3 worldPos)
{
    float result = FLESH_FAR_DISTANCE;

    [loop]
    for (int i = 0; i < MAX_FLESH_INSTANCES; i++)
    {
        if (i >= _FleshCount)
        {
            break;
        }

        result = SmoothMin(result, SphereSDF(worldPos, i), _FleshColor[i].w);
    }

    return result;
}

/// Smooth union plus the analytic gradient, the blended surface colour, and the index of the
/// closest contributing instance.
///
/// The gradient is exact, not an approximation. For the polynomial smooth minimum the chain-rule
/// term through h vanishes identically: its coefficient is (a - b - k(1 - 2h)), and h is defined so
/// that a - b = k(1 - 2h), so the coefficient is exactly zero. What remains is the h-weighted blend
/// of the operand gradients, and each sphere's gradient is an exact unit vector. This replaces the
/// six-tap central difference, which cost six full scene evaluations and quantised to the voxel grid.
///
/// Colour rides the same h weights, so a neck between two instances averages their tier colours over
/// exactly the region where their distances blend. Instances far from worldPos drive h to 1, which
/// keeps the accumulator untouched, so distant colours cannot leak across the pile.
float SceneSDFWithSurface(float3 worldPos, out float3 gradient, out float3 surfaceColor, out int nearestInstance)
{
    float result = FLESH_FAR_DISTANCE;
    float nearest = FLESH_FAR_DISTANCE;
    gradient = float3(0.0, 0.0, 0.0);
    surfaceColor = float3(0.0, 0.0, 0.0);
    nearestInstance = 0;

    [loop]
    for (int i = 0; i < MAX_FLESH_INSTANCES; i++)
    {
        if (i >= _FleshCount)
        {
            break;
        }

        float4 sphere = _FleshSphere[i];
        float4 colorBlend = _FleshColor[i];
        float3 offset = worldPos - sphere.xyz;
        float offsetLength = length(offset);
        float instanceDistance = offsetLength - sphere.w;
        float3 instanceGradient = offset / max(offsetLength, 1e-6);

        // First iteration: result is FLESH_FAR_DISTANCE, so h resolves to 0 and the distance, the
        // gradient and the colour are all taken wholly from this instance.
        float h;
        result = SmoothMinWeighted(result, instanceDistance, colorBlend.w, h);
        gradient = lerp(instanceGradient, gradient, h);
        surfaceColor = lerp(colorBlend.rgb, surfaceColor, h);

        if (instanceDistance < nearest)
        {
            nearest = instanceDistance;
            nearestInstance = i;
        }
    }

    return result;
}

/// Exact unit surface normal of the merged field.
float3 SceneNormal(float3 worldPos)
{
    float3 gradient;
    float3 surfaceColor;
    int nearestInstance;
    SceneSDFWithSurface(worldPos, gradient, surfaceColor, nearestInstance);
    return normalize(gradient + 1e-8);
}

/// Slab test against an axis-aligned box. Returns false when the ray misses.
bool RayBox(float3 rayOrigin, float3 rayDirection, float3 boxMin, float3 boxMax, out float tNear, out float tFar)
{
    float3 directionSign = rayDirection < 0.0 ? -1.0 : 1.0;
    float3 inverseDirection = 1.0 / (directionSign * max(abs(rayDirection), 1e-8));
    float3 t0 = (boxMin - rayOrigin) * inverseDirection;
    float3 t1 = (boxMax - rayOrigin) * inverseDirection;
    float3 tSmall = min(t0, t1);
    float3 tLarge = max(t0, t1);

    tNear = max(max(tSmall.x, tSmall.y), tSmall.z);
    tFar = min(min(tLarge.x, tLarge.y), tLarge.z);

    return tFar >= max(tNear, 0.0);
}

#endif
