#ifndef CROWD_COMMON_INCLUDED
#define CROWD_COMMON_INCLUDED

// Keep in sync with CrowdTypes.cs
struct CrowdInstance
{
    float3 position;
    float  yaw;
    uint   variant;
    float  scale;
    uint   colorTop;
    uint   colorTop2;
    uint   colorBottom;
    uint   colorHair;
    uint   packed;      // pattern | skinTone<<8 | enthusiasm<<16 | team<<24
    float  standOffset;
};

struct CrowdAnimState
{
    uint  clipA; float startA; float speedA; float blendStart;
    uint  clipB; float startB; float speedB; float blendDuration;
    float nextDecision; uint epoch; float reactAt; uint flags;
};

struct CrowdClip
{
    uint frameOffset; uint frameCount; float fps; float duration;
    uint category; uint posture; float rootForward; float energy;
};

struct CrowdEmotion
{
    float standProbability; float speed; float intervalMin; float intervalMax;
    float reactDelayMin; float reactDelayMax; float blendDuration; float syncAmount;
    float4 sit0; float4 sit1; float4 stand0; float4 stand1;
};

#define CROWD_FLAG_PENDING 1u

float3 CrowdQRot(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

float3 CrowdSafeNormalize(float3 v, float3 fallback)
{
    float l2 = dot(v, v);
    return (l2 > 1e-12 && !isnan(l2) && !isinf(l2)) ? v * rsqrt(l2) : fallback;
}

float4 CrowdUnpackColor(uint c)
{
    return float4(c & 255u, (c >> 8) & 255u, (c >> 16) & 255u, (c >> 24) & 255u) / 255.0;
}

uint CrowdHash(uint x)
{
    // PCG-ish integer hash
    x ^= x >> 16; x *= 0x7feb352du;
    x ^= x >> 15; x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float CrowdRand(inout uint state)
{
    state = CrowdHash(state + 0x9e3779b9u);
    return (state & 0x00FFFFFFu) / 16777216.0;
}

#endif
