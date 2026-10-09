#ifndef SK_BAKEFETCH_INCLUDED
#define SK_BAKEFETCH_INCLUDED
// Per-vertex bake results (SdfBaker), fetched by texel index in vertex shaders (vertex texture fetch).
TEXTURE2D(_SkBakeSnap);   // (normal from SDF gradient, displacement along the mesh normal)
TEXTURE2D(_SkBakeLight);  // (shd, folT, ao, folIn)
TEXTURE2D(_SkBakeMat);    // (trunk maturity tr, wood/soil blend h, pot flag, 0)

struct SkBaked
{
    float3 pos;
    float3 nrm;
    float4 light;
    float4 mat;
};

SkBaked skFetchBaked(float3 pos, float3 nrm, float2 texel)
{
    int2 c = int2(texel + 0.5);
    float4 s = LOAD_TEXTURE2D_LOD(_SkBakeSnap, c, 0);
    SkBaked o;
    o.light = LOAD_TEXTURE2D_LOD(_SkBakeLight, c, 0);
    o.mat = LOAD_TEXTURE2D_LOD(_SkBakeMat, c, 0);
    bool ok = dot(s.xyz, s.xyz) > 0.25;
    o.pos = ok ? pos + nrm * s.w : pos;
    o.nrm = ok ? normalize(s.xyz) : nrm;
    return o;
}
#endif
