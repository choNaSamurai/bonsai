#ifndef SK_BAKEFETCH_INCLUDED
#define SK_BAKEFETCH_INCLUDED
// Per-vertex bake results (SdfBaker), fetched by texel index in vertex shaders (vertex texture fetch).
// Bound per renderer with a MaterialPropertyBlock (double-buffered bake targets); the bake passes use them as globals.
TEXTURE2D(_SkBakeSnap);   // (normal from SDF gradient, displacement along the mesh normal)
TEXTURE2D(_SkBakeLight);  // (shd, folT, ao, folIn)
TEXTURE2D(_SkBakeMat);    // (trunk maturity tr, wood/soil blend h, pot flag, 0)
float _SkNoBake;          // 1 = Chronicle / scrubbing: mesh as built, no bake (B0-like defaults)

struct SkBaked
{
    float3 pos;
    float3 nrm;
    float4 light;
    float4 mat;
};

// texel.xy = bake texel, texel.z = vertex kind (0 wood, 1 soil, 2 pot)
SkBaked skFetchBaked(float3 pos, float3 nrm, float3 texel)
{
    SkBaked o;
    if (_SkNoBake > 0.5)
    {
        o.pos = pos;
        o.nrm = nrm;
        o.light = float4(1., 1., .85, 0.);
        o.mat = float4(.6, texel.z > 0.5 ? 0. : 1., texel.z > 1.5 ? 1. : 0., 0.);
        return o;
    }
    int2 c = int2(texel.xy + 0.5);
    float4 s = LOAD_TEXTURE2D_LOD(_SkBakeSnap, c, 0);
    o.light = LOAD_TEXTURE2D_LOD(_SkBakeLight, c, 0);
    o.mat = LOAD_TEXTURE2D_LOD(_SkBakeMat, c, 0);
    bool ok = dot(s.xyz, s.xyz) > 0.25;
    o.pos = ok ? pos + nrm * s.w : pos;
    o.nrm = ok ? normalize(s.xyz) : nrm;
    return o;
}
#endif
