#ifndef SK_COMMON_INCLUDED
#define SK_COMMON_INCLUDED
// PA-102X-S spike: 1:1 HLSL port of sketch_v11 FS/LFS helpers (noise, bark, soil, pot, lighting, tonemap).
// All positions/normals are in SKETCH space (x right, y up, z toward the viewer).

#define SK_SE 0.28
#define SK_CE 0.96

float4 _SkL, _SkSunC, _SkAmb, _SkPot, _SkPotC;
float _SkSun, _SkSI, _SkKr, _SkNR, _SkNG, _SkNP, _SkFD, _SkFol, _SkPotT, _SkSpB, _SkAge, _SkRb, _SkBump, _SkMoss, _SkTr;

float skH13(float3 p) { p = frac(p * .1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
float3 skH33(float3 p) { p = frac(p * float3(.1031, .103, .0973)); p += dot(p, p.yxz + 33.33); return frac((p.xxy + p.yxx) * p.zyx); }

float skVn(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3. - 2. * f);
    return lerp(lerp(lerp(skH13(i), skH13(i + float3(1, 0, 0)), f.x), lerp(skH13(i + float3(0, 1, 0)), skH13(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(skH13(i + float3(0, 0, 1)), skH13(i + float3(1, 0, 1)), f.x), lerp(skH13(i + float3(0, 1, 1)), skH13(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float skVor(float3 p)
{
    float3 i = floor(p), f = frac(p);
    float d1 = 8., d2 = 8.;
    [unroll] for (int z = -1; z <= 1; z++)
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float3 g = float3(x, y, z), r = g + skH33(i + g) - f;
        float d = dot(r, r);
        if (d < d1) { d2 = d1; d1 = d; }
        else if (d < d2) d2 = d;
    }
    return sqrt(d2) - sqrt(d1);
}

float skBarkH(float3 p, float mat)
{
    if (_SkSpB > 1.5)
    {
        float e = skVor(p * float3(.09, .034, .09)), pl = smoothstep(0., .2, e) * (.8 + .2 * skVn(p * .4));
        return lerp(.3 * skVn(p * float3(.3, .1, .3)), pl, mat);
    }
    if (_SkSpB > 0.5) return .5 - .35 * mat * smoothstep(.8, .97, skVn(p * float3(.1, 1., .1))) + .08 * skVn(p * .25);
    return .15 * skVn(p * float3(.6, .06, .6)) + .1 * skVn(p * float3(1.6, .15, 1.6));
}

float3 skBarkC(float3 p, float mat)
{
    if (_SkSpB > 1.5)
    {
        float e = skVor(p * float3(.09, .034, .09));
        float3 pl = float3(.068, .064, .06) * (.7 + .55 * skVn(p * .25)), fi = float3(.05, .02, .013), old = lerp(fi, pl, smoothstep(.02, .16, e));
        return lerp(float3(.07, .058, .044), old, mat);
    }
    if (_SkSpB > 0.5)
    {
        float3 b = float3(.105, .043, .028) * (.85 + .3 * skVn(p * float3(.2, .6, .2)));
        b = lerp(b, float3(.19, .16, .12), .8 * mat * smoothstep(.74, .8, skVn(p * float3(.14, 1.3, .14) + 3.)));
        b *= 1. - .45 * mat * smoothstep(.93, .98, skVn(p * float3(.05, .8, .05) + 7.));
        return lerp(float3(.1, .05, .035), b, mat);
    }
    float3 b0 = float3(.075, .07, .06) * (.88 + .24 * skVn(p * float3(.7, .07, .7)));
    b0 = lerp(b0, float3(.068, .075, .045), .6 * smoothstep(.55, .75, skVn(p * .09 + 11.)));
    return lerp(float3(.07, .075, .04), b0, mat);
}

float3 skSoilC(float3 p)
{
    float3 c = float3(.05, .032, .018) * (.8 + .4 * skVn(p * .9));
    c = lerp(c, float3(.1, .088, .075), .55 * step(.84, skH13(floor(p * 2.))));
    float mo = smoothstep(.52, .68, skVn(float3(p.x, 0., p.z) * .06 + 5.) + .15 * skVn(p * .4));
    return lerp(c, float3(.035, .07, .018) * (.75 + .5 * skVn(p * 1.4)), mo * _SkMoss);
}

float3 skPotC(float3 p)
{
    float3 c = _SkPotC.rgb;
    if (_SkPotT < 0.5) { c *= .9 + .2 * skVn(p * .08); c *= 1. - .35 * step(.9, skH13(floor(p * 1.4))); }
    else if (_SkPotT < 1.5) { c *= .85 + .3 * skVn(p * float3(.05, .12, .05)); }
    else { c *= .94 + .12 * skVn(p * .06); c *= 1. - .4 * (1. - smoothstep(.0, .04, skVor(p * .11))); }
    return c;
}

// ---------------------------------------------------------------- C6: LowEnd bark (SK_BARK_LOW)
// The full bark builds the relief normal from 4 skBarkH calls (forward differences, step .35): for Kuromatsu that is
// 4 Voronoi (27 cells each) + 1 more for the colour. The LowEnd variant evaluates every noise term ONCE with its
// analytic gradient; Kuromatsu's colour and relief share one Voronoi. Optional LOD for thin branches: where the trunk
// weight lerp(.25, 1, tr) = mat / age is below _SkBarkLodMat the plate pattern is replaced by its mean (no Voronoi),
// faded in over [thr, 1.5 thr]. 0 = off (default).
float _SkBarkLodMat;

// skVn with its analytic gradient (identical value).
float skVnD(float3 p, out float3 d)
{
    float3 i = floor(p), f = frac(p);
    float3 u = f * f * (3. - 2. * f), du = 6. * f * (1. - f);
    float a = skH13(i), b = skH13(i + float3(1, 0, 0)), c = skH13(i + float3(0, 1, 0)), e = skH13(i + float3(1, 1, 0));
    float a1 = skH13(i + float3(0, 0, 1)), b1 = skH13(i + float3(1, 0, 1)), c1 = skH13(i + float3(0, 1, 1)), e1 = skH13(i + float3(1, 1, 1));
    float k1 = b - a, k2 = c - a, k3 = a1 - a, k4 = a - b - c + e, k5 = a - c - a1 + c1, k6 = a - b - a1 + b1;
    float k7 = -a + b + c - e + a1 - b1 - c1 + e1;
    d = du * float3(k1 + k4 * u.y + k6 * u.z + k7 * u.y * u.z,
                    k2 + k5 * u.z + k4 * u.x + k7 * u.z * u.x,
                    k3 + k6 * u.x + k5 * u.y + k7 * u.x * u.y);
    return a + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z;
}

// skVor (F2 - F1) with its analytic gradient from the same 27-cell search: d|r|/dp = -r/|r|, r = feature - p.
float skVorG(float3 p, out float3 g)
{
    float3 i = floor(p), f = frac(p);
    float d1 = 8., d2 = 8.;
    float3 r1 = 0, r2 = 0;
    [unroll] for (int z = -1; z <= 1; z++)
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float3 gg = float3(x, y, z), r = gg + skH33(i + gg) - f;
        float d = dot(r, r);
        if (d < d1) { d2 = d1; r2 = r1; d1 = d; r1 = r; }
        else if (d < d2) { d2 = d; r2 = r; }
    }
    float s1 = sqrt(d1), s2 = sqrt(d2);
    g = r1 / max(s1, 1e-4) - r2 / max(s2, 1e-4);
    return s2 - s1;
}

// Bark colour (= skBarkC) and the gradient of skBarkH (sketch space) for the relief normal.
float3 skBarkLow(float3 p, float mat, out float3 gr)
{
    if (_SkSpB > 1.5)
    {
        const float3 kv = float3(.09, .034, .09);
        float e = .35;
        float3 ge = 0;
        float thr = _SkBarkLodMat * _SkAge;
        [branch] if (mat >= thr)
        {
            e = skVorG(p * kv, ge);
            ge *= kv;
            float w = thr > 0. ? saturate((mat - thr) / (.5 * thr)) : 1.;
            e = lerp(.35, e, w);
            ge *= w;
        }
        float3 d4, d1;
        float v4 = skVnD(p * .4, d4);
        skVnD(p * float3(.3, .1, .3), d1);
        float t = saturate(e * 5.), ss = t * t * (3. - 2. * t), dss = 30. * t * (1. - t);
        float md = .8 + .2 * v4;
        gr = (1. - mat) * .3 * float3(.3, .1, .3) * d1 + mat * (dss * md * ge + ss * .08 * d4);
        float3 pl = float3(.068, .064, .06) * (.7 + .55 * skVn(p * .25)), fi = float3(.05, .02, .013), old = lerp(fi, pl, smoothstep(.02, .16, e));
        return lerp(float3(.07, .058, .044), old, mat);
    }
    if (_SkSpB > 0.5)
    {
        float3 d1, d2;
        float v1 = skVnD(p * float3(.1, 1., .1), d1);
        skVnD(p * .25, d2);
        float t = saturate((v1 - .8) / .17), dss = 6. * t * (1. - t) / .17;
        gr = -.35 * mat * dss * float3(.1, 1., .1) * d1 + .02 * d2;
        float3 b = float3(.105, .043, .028) * (.85 + .3 * skVn(p * float3(.2, .6, .2)));
        b = lerp(b, float3(.19, .16, .12), .8 * mat * smoothstep(.74, .8, skVn(p * float3(.14, 1.3, .14) + 3.)));
        b *= 1. - .45 * mat * smoothstep(.93, .98, skVn(p * float3(.05, .8, .05) + 7.));
        return lerp(float3(.1, .05, .035), b, mat);
    }
    float3 da, db;
    skVnD(p * float3(.6, .06, .6), da);
    skVnD(p * float3(1.6, .15, 1.6), db);
    gr = .15 * float3(.6, .06, .6) * da + .1 * float3(1.6, .15, 1.6) * db;
    return skBarkC(p, mat);
}

// FS lighting with baked per-vertex terms bk = (shd, folT, ao, folIn). Returns colour before tonemap.
float3 skShade(float3 base, float3 n, float4 bk, out float sh)
{
    float3 L = _SkL.xyz;
    float dif = max(dot(n, L), 0.);
    sh = dif > 0. ? lerp(1., bk.x, smoothstep(0., .25, dif)) : 0.;
    float ft = dif > 0. ? bk.y : 1.;
    sh *= ft;
    float a = bk.z * (1. - .6 * _SkFol * bk.w);
    float sky = .55 + .45 * n.y, bnc = saturate(-n.y) * .25;
    return base * (3.6 * _SkSun * dif * sh * _SkSunC.rgb + (.7 * sky + bnc) * (1. + .7 * (1. - _SkSun)) * a * _SkAmb.rgb);
}

float3 skSrgbToLinear(float3 c)
{
    float3 lo = c / 12.92, hi = pow((c + .055) / 1.055, 2.4);
    return lerp(hi, lo, step(c, .04045));
}

// Sketch tonemap 1 - exp(-1.35c), gamma 1/2.2; written so that the sRGB target stores exactly the sketch's value.
float4 skOut(float3 col)
{
    col = 1. - exp(-col * 1.35);
    col = pow(saturate(col), 1. / 2.2);
#if !defined(UNITY_COLORSPACE_GAMMA)
    col = skSrgbToLinear(col);
#endif
    return float4(col, 1.);
}

#endif
