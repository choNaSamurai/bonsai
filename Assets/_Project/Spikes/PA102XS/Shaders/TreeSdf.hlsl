#ifndef SK_TREESDF_INCLUDED
#define SK_TREESDF_INCLUDED
// PA-102X-S spike: HLSL port of the sketch_v11 SDF (sdRC, sdTC, smin, wood, map, nrm, ao, shd, folT, folIn, potD, trunkD).
// Scene data comes from ONE RGBA32F texture (no uniform arrays): row 0 = DAT (1024 texels),
// row 1 = CB at 0..123, GM at 128..145, PE at 160..177. Requires Core.hlsl (TEXTURE2D / LOAD_TEXTURE2D).
#include "SketchCommon.hlsl"

#define SK_NTC 24
#define SK_RP 13
#define SK_MAXR 7
#define SK_RC 3
#define SK_NSG 6
#define SK_MAXG 9
#define SK_MAXCPG 14
#define SK_OB 97
#define SK_OR 155
#define SK_OBR 246
#define SK_MAXPAD 9

TEXTURE2D(_SkSdf);

float4 skG(int i) { return LOAD_TEXTURE2D(_SkSdf, int2(i, 0)); }
float4 skUB(int k) { return LOAD_TEXTURE2D(_SkSdf, int2(SK_OB + k, 0)); }
float4 skCB(int c) { return LOAD_TEXTURE2D(_SkSdf, int2(c, 1)); }
float4 skGM(int k) { return LOAD_TEXTURE2D(_SkSdf, int2(128 + k, 1)); }
float4 skPE(int k) { return LOAD_TEXTURE2D(_SkSdf, int2(160 + k, 1)); }

float skSdRC(float3 p, float3 a, float3 b, float r1, float r2)
{
    float3 ba = b - a;
    float l2 = max(dot(ba, ba), 1e-6), rr = r1 - r2, a2 = l2 - rr * rr, il2 = 1. / l2;
    float3 pa = p - a;
    float y = dot(pa, ba), z = y - l2;
    float3 xv = pa * l2 - ba * y;
    float x2 = dot(xv, xv), y2 = y * y * l2, z2 = z * z * l2, k = (float)sign(rr) * rr * rr * x2;
    if ((float)sign(z) * a2 * z2 > k) return sqrt(x2 + z2) * il2 - r2;
    if ((float)sign(y) * a2 * y2 < k) return sqrt(x2 + y2) * il2 - r1;
    return (sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
}

float skSdTC(float3 p, float3 a, float3 b, float r1, float r2)
{
    float3 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0., 1.);
    return length(pa - ba * h) - lerp(r1, r2, h);
}

float skSmin(float a, float b, float k) { float h = max(k - abs(a - b), 0.) / k; return min(a, b) - h * h * k * .25; }

float skEll(float3 p, float2 ab) { float2 q = p.xz / ab; float k0 = length(q), k1 = length(p.xz / (ab * ab)); return k0 * (k0 - 1.) / max(k1, 1e-4); }

float skWood(float3 p, float cap, bool lod)
{
    float dt = 1e5;
    [loop] for (int g = 0; g < SK_NSG; g++)
    {
        float4 sb = skUB(SK_NTC + SK_MAXR * SK_RC + g);
        if (length(p - sb.xyz) - sb.w > min(dt, cap)) continue;
        [loop] for (int c1 = g * 4; c1 < g * 4 + 4; c1++)
        {
            float4 b = skUB(c1);
            if (length(p - b.xyz) - b.w > dt) continue;
            [unroll] for (int j1 = 0; j1 < 4; j1++)
            {
                int i = c1 * 4 + j1;
                float4 A = skG(i), B = skG(i + 1);
                dt = min(dt, skSdRC(p, A.xyz, B.xyz, A.w, B.w));
            }
        }
    }
    float dr = 1e5;
    int nr = (int)(_SkNR + .5);
    [loop] for (int r = 0; r < SK_MAXR; r++)
    {
        if (r >= nr) break;
        float lim = min(min(dr, dt) + _SkKr, cap);
        float4 rb = skUB(SK_NTC + SK_MAXR * SK_RC + SK_NSG + r);
        if (length(p - rb.xyz) - rb.w > lim) continue;
        float d = 1e5;
        [loop] for (int c2 = 0; c2 < SK_RC; c2++)
        {
            float4 b2 = skUB(SK_NTC + r * SK_RC + c2);
            if (length(p - b2.xyz) - b2.w > min(lim, d)) continue;
            [unroll] for (int j2 = 0; j2 < 4; j2++)
            {
                int i2 = SK_OR + r * SK_RP + c2 * 4 + j2;
                float4 A2 = skG(i2), B2 = skG(i2 + 1);
                d = min(d, skSdRC(p, A2.xyz, B2.xyz, A2.w, B2.w));
            }
        }
        if (d < 1e4) dr = skSmin(dr, d, _SkKr * .6);
    }
    float w = skSmin(dt, dr, _SkKr);
    int ng = (int)(_SkNG + .5);
    [loop] for (int g2 = 0; g2 < SK_MAXG; g2++)
    {
        if (g2 >= ng) break;
        float4 gb = skGM(g2 * 2), gm = skGM(g2 * 2 + 1);
        float kg = gm.x, ks = gm.w;
        if (length(p - gb.xyz) - gb.w > min(w, cap) + kg) continue;
        int cs0 = (int)(gm.y + .5), cn = lod ? 2 : (int)(gm.z + .5);
        float dg = 1e5, lb = 1e5;
        [loop] for (int j3 = 0; j3 < SK_MAXCPG; j3++)
        {
            if (j3 >= cn) break;
            int c3 = cs0 + j3;
            float4 cb = skCB(c3);
            if (cb.w < 0. && j3 > 0) { dg = skSmin(dg, lb, ks); lb = 1e5; }
            float aw = abs(cb.w), sk = floor(aw / 1000.);
            if (length(p - cb.xyz) - (aw - sk * 1000.) > min(lb, dg) + ks) { j3 += (int)sk; continue; }
            int bi = SK_OBR + c3 * 5;
            [unroll] for (int s = 0; s < 4; s++)
            {
                float4 A3 = skG(bi + s), B3 = skG(bi + s + 1);
                lb = min(lb, skSdTC(p, A3.xyz, B3.xyz, A3.w, B3.w));
            }
        }
        dg = skSmin(dg, lb, ks);
        w = skSmin(w, dg, kg);
    }
    return w;
}

float3 skPel(int g, float3 p, out float3 r, out float3 hx)
{
    float4 e0 = skPE(g * 2), e1 = skPE(g * 2 + 1);
    hx = float3(e1.x, 0., e1.y);
    r = float3(e0.w, e1.z, e1.w);
    float3 o = p - e0.xyz;
    return float3(dot(o, hx), o.y, dot(o, float3(hx.z, 0., -hx.x)));
}

float skFolT(float3 p, float3 l)
{
    float tl = 0.;
    int np = (int)(_SkNP + .5);
    [loop] for (int g = 0; g < SK_MAXPAD; g++)
    {
        if (g >= np) break;
        float3 r, hx;
        float3 o = skPel(g, p, r, hx);
        float3 oo = o / r, dd = float3(dot(l, hx), l.y, dot(l, float3(hx.z, 0., -hx.x))) / r;
        float A = dot(dd, dd), B = 2. * dot(oo, dd), C = dot(oo, oo) - 1., D = B * B - 4. * A * C;
        if (D <= 0.) continue;
        D = sqrt(D);
        float t0 = (-B - D) / (2. * A), t1 = (-B + D) / (2. * A);
        tl += max(0., t1 - max(t0, 0.));
    }
    return exp(-_SkFD * tl);
}

float skFolIn(float3 p)
{
    float m = 0.;
    int np = (int)(_SkNP + .5);
    [loop] for (int g = 0; g < SK_MAXPAD; g++)
    {
        if (g >= np) break;
        float3 r, hx;
        float3 o = skPel(g, p, r, hx);
        m = max(m, saturate(1. - length(o / r)));
    }
    return m;
}

float skSdRR(float2 q, float2 b, float r) { float2 d = abs(q) - b + r; return length(max(d, 0.)) + min(max(d.x, d.y), 0.) - r; }
float skPShape(float3 p, float2 b) { return _SkPot.z > .5 ? skEll(p, b) : skSdRR(p.xz, b, 12.); }
float2 skPotB(float3 p) { return _SkPot.xy * (.9 + .1 * saturate((p.y + _SkPot.w) / (_SkPot.w + 4.))); }

float skPotD(float3 p)
{
    float dep = _SkPot.w;
    float2 b = skPotB(p);
    float body = max(skPShape(p, b), max(p.y - 2., -p.y - dep)) - 2.5;
    float lip = max(skPShape(p, _SkPot.xy + 3.5), abs(p.y - 1.5) - 1.6) - 1.4;
    float ft = 1e5;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float a = i < 2 ? .75 : 2.39;
        float sx = (i % 2 == 0) ? 1. : -1.;
        float2 c = _SkPot.z > .5 ? float2(sin(a) * sx * _SkPot.x * .8, cos(a) * _SkPot.y * .8)
                                 : float2(sx * (_SkPot.x - 30.), (i < 2 ? 1. : -1.) * (_SkPot.y - 15.));
        ft = min(ft, _SkPot.z > .5 ? length(p.xz - c) - 10. : skSdRR(p.xz - c, float2(17., 9.), 4.));
    }
    ft = max(ft, abs(p.y + dep + 3.) - 4.) - 1.2;
    return max(min(min(body, lip), ft), -max(skPShape(p, b - 8.), -p.y - (dep - 10.)));
}

float skTrunkD(float3 p)
{
    float dt = 1e5;
    [loop] for (int c = 0; c < SK_NTC; c++)
    {
        float4 b = skUB(c);
        if (length(p - b.xyz) - b.w > dt) continue;
        [unroll] for (int j = 0; j < 4; j++)
        {
            int i = c * 4 + j;
            float4 A = skG(i), B = skG(i + 1);
            dt = min(dt, skSdRC(p, A.xyz, B.xyz, A.w, B.w));
        }
    }
    return dt;
}

// (distance, isPot, wood/soil blend h)
float3 skMap(float3 p, bool lod)
{
    float inner = skPShape(p, skPotB(p) - 8.);
    float soil = max(max(inner, p.y), -p.y - (_SkPot.w - 10.));
    float ks = 2.;
    float pot = skPotD(p);
    float w = skWood(p, min(soil, pot) + 2. * _SkKr + 4., lod);
    float h = saturate(.5 + .5 * (soil - w) / ks);
    float ws = lerp(soil, w, h) - ks * h * (1. - h);
    return pot < ws ? float3(pot, 1., 0.) : float3(ws, 0., h);
}

float skMapD(float3 p, bool lod) { return skMap(p, lod).x; }

float3 skNrm(float3 p)
{
    float e = .15;
    float3 n = 0;
    [loop] for (int i = 0; i < 4; i++)
    {
        float3 k = i == 0 ? float3(1, -1, -1) : i == 1 ? float3(-1, -1, 1) : i == 2 ? float3(-1, 1, -1) : float3(1, 1, 1);
        n += k * skMapD(p + k * e, false);
    }
    return normalize(n);
}

float skAo(float3 p, float3 n)
{
    float s = 0., w = 1.;
    [loop] for (int i = 0; i < 3; i++)
    {
        float h = 1.5 + 7. * float(i);
        s += (h - skMapD(p + n * h, true)) * w;
        w *= .65;
    }
    return saturate(1. - s * .045);
}

float skShd(float3 p, float3 l)
{
    float r = 1., t = 1.;
    [loop] for (int i = 0; i < 12; i++)
    {
        float h = skMapD(p + l * t, true);
        r = min(r, 8. * h / t);
        t += clamp(h, 2.2, 34.);
        if (r < .005 || t > 380.) break;
    }
    return saturate(r);
}

float3 skPotNrm(float3 p)
{
    float e = .15;
    float3 n = 0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float3 k = i == 0 ? float3(1, -1, -1) : i == 1 ? float3(-1, -1, 1) : i == 2 ? float3(-1, 1, -1) : float3(1, 1, 1);
        n += k * skPotD(p + k * e);
    }
    return normalize(n);
}

#endif
