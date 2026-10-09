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
