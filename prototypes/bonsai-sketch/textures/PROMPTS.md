# Nano Banana prompts (PA-109)

The owner generates textures in Nano Banana from these prompts (square 1:1, highest resolution, 2–3 variants each). Originals live in `textures/juniper/`; never overwrite an original — new versions get a `_v2`, `_v3` suffix. The sketch embeds downscaled WebP derivatives (512², custom mips) as data URIs.

Status (2026-10-17): **wood textures are used** (`bark`, `shari`, `shari_macro` desaturated to ~30 % at ~50 % strength, `moss`, `moss_patch_atlas`). **Foliage atlases were rejected** by the owner — juniper foliage stays procedural (atlases read as "broccoli" / "paper ferns" up close). They are kept for a possible LowEnd fallback.

## bark.png — live juniper bark
```
Seamless tileable texture, square 1:1, orthographic flat scan viewed straight on. Bark of an old Shimpaku juniper bonsai (Juniperus chinensis 'Shimpaku') at macro scale: a strip of bark about 3 cm wide fills the whole frame. Long, thin, parallel fibrous strips run strictly vertically from the top edge to the bottom edge, slightly wavy, varying width 1–4 mm; some strips peel up at their edges, with narrow dark crevices between strips. Colour: muted greyish red-brown (#6A4636 to #7A5040) with irregular patches of weathered silver-grey (#8A8178) and a few dark umber crevices. Fine longitudinal fibre detail. Even, soft, diffuse, shadowless lighting like a flatbed scan; no specular highlights, no directional shadows, no vignette, no depth-of-field blur, everything in sharp focus. No moss, no lichen, no knots, no round holes or eye-shaped marks, no text, no watermark. The texture must tile seamlessly on all four edges.
```

## shari.png — dead wood (shari / jin)
```
Seamless tileable texture, square 1:1, orthographic flat scan viewed straight on. Weathered dead wood (shari / jin) of an ancient juniper bonsai, sun-bleached and treated with lime sulphur: smooth matte silver-grey wood (#B9B6AE to #D2CFC7) with very fine longitudinal grain running strictly vertically, two or three long, smooth, deep flowing grooves and shallow twisted ridges following the grain, a few hairline cracks along the grain, subtle warm ochre stains in places. Slightly polished, weathered surface. Even, soft, diffuse, shadowless lighting; no specular highlights, no cast shadows, no vignette, everything in sharp focus. No bark, no knots, no holes, no fungus, no moss, no text, no watermark. The texture must tile seamlessly on all four edges.
```

## shari_macro.png — large-scale tonal variation of dead wood
```
Seamless tileable texture, square 1:1, orthographic flat scan viewed straight on, covering a large area: about 25 cm of the dead-wood surface of an ancient juniper bonsai trunk (shari) seen from a distance, so the fine grain is barely visible and the LARGE-SCALE tonal variation dominates. Weathered, sun-bleached silver-grey wood with broad, soft, irregular patches: warm ochre and light tan areas, cool blue-grey areas, a few darker grey-brown streaks running vertically like old rain-water stains, slightly dirtier and darker toward the bottom edge, faint pale-green lichen speckles in two or three small areas. Low contrast overall, no sharp black lines, no strong grain lines, matte surface. Even, soft, diffuse, shadowless lighting; no specular highlights, no cast shadows, no vignette. No bark, no knots, no holes, no text, no watermark. The texture must tile seamlessly on all four edges.
```
Note: the result came out too saturated (blue/orange) — the sketch median-filters, blurs and desaturates it.

## moss.png — soil moss
```
Seamless tileable texture, square 1:1, orthographic top-down view straight down, covering about 10 cm × 10 cm. Dense, low, cushion moss of the kind grown on the soil surface of Japanese bonsai pots (fine Bryum / Leucobryum-like moss): very small tightly packed green tufts forming a soft velvety carpet, natural colour variation from fresh light green (#8DAA4E) to deeper green (#4F6E34), a few slightly yellow-brown dry patches, two or three tiny gaps showing dark brown soil grains, a few tiny sporophyte stalks. Even, soft, diffuse overcast lighting from above, gentle self-shadowing between tufts only; no cast shadows, no specular highlights, no vignette, everything in sharp focus. No leaves, no stones, no twigs, no pot rim, no text, no watermark. The texture must tile seamlessly on all four edges.
```

## moss_patch_atlas.png — moss patches on roots
```
Game sprite sheet, square 1:1, a 2×2 grid containing four separate isolated irregular patches of the same fine cushion moss as it grows on the exposed roots and the base of a bonsai trunk, viewed straight on, each patch about 3–5 cm, organic irregular outline with soft feathered edges, thin at the edges and thicker in the centre, on a perfectly flat uniform pure magenta background (#FF00FF); no patch touches its cell border, generous margin. Natural colour variation from light green to deep green with a few yellow-brown dry tips. Even soft diffuse lighting, no cast shadow on the background, sharp focus. No bark, no soil, no text, no watermark, no magenta colour spill on the edges.
```

## foliage_atlas.png (v1, rejected) — scale-leaf tufts
```
Game sprite sheet, square 1:1, a 2×2 grid containing four separate isolated objects on a perfectly flat, uniform pure magenta background (#FF00FF); no object touches its cell border, generous empty margin around each. Each object is one small tuft of mature Shimpaku juniper (Juniperus chinensis 'Shimpaku') bonsai foliage of the SCALE-LEAF type, NOT needles: short, thick, cord-like segmented green "fingers" about 2–3 mm thick that branch densely and pack into a compact rounded tuft; granular, bead-like surface made of tiny overlapping scales; slightly lighter yellow-green growing tips (#8DB255), main body deep green (#4F7A3A), darker green toward the base. Viewed from slightly above (30–45°), soft even diffuse daylight from above, gentle self-shadowing inside each tuft, no cast shadow on the background. Four variations in shape and size. Photorealistic botanical macro detail, sharp focus. No twigs or branches visible, no pot, no text, no watermark, no magenta colour spill on the foliage edges.
```

## foliage_atlas_v2.png (rejected) — flat scale-leaf sprays
```
Game sprite sheet, square 1:1, a 3×3 grid containing nine separate isolated objects on a perfectly flat, uniform pure magenta background (#FF00FF); each object is centred in its cell and fills only about 65% of the cell, with a generous empty margin, and no object touches its cell border. Each object is one small, loose spray of mature Shimpaku juniper (Juniperus chinensis 'Shimpaku') bonsai foliage of the SCALE-LEAF type, NOT needles: fine, thin, cord-like segmented "fingers" about 1–1.5 mm thick, branching in a flattened, fan-shaped, slightly domed spray (wider than tall, roughly 3:2), relatively open, with small gaps showing between the fingers and individual fingertips clearly visible along the outer edge — NOT a dense round ball, NOT broccoli-like. Surface of fine overlapping scales, delicate granular texture. Colour: muted, slightly bluish sage green (main body #5E7F4A, cooler areas #6E8C5A), soft light yellow-green growing tips (#A3BC72), only slightly darker toward the base; low saturation, natural, not vivid. Viewed from about 40° above, soft, even, diffuse overcast daylight, very gentle self-shadowing (no deep black shadows inside the spray), no cast shadow on the background. Nine clearly different variations: different outlines (fan, oval, crescent, irregular), sizes and densities, some rotated. Photorealistic botanical macro detail, sharp focus across the whole object. No twigs or branches visible, no pot, no text, no watermark, no magenta colour spill on the foliage edges.
```

## juvenile_atlas.png (rejected) — juvenile needle foliage
```
Game sprite sheet, square 1:1, a 2×2 grid containing four separate isolated objects on a perfectly flat, uniform pure magenta background (#FF00FF); no object touches its cell border, generous empty margin around each. Each object is one small sprig of JUVENILE juniper foliage (Juniperus chinensis): sharp, prickly, awl-shaped needles 5–8 mm long arranged in whorls of three along a thin green stem, needles spreading outward at 30–45°, glaucous blue-green colour (#5E8A7A) with a faint whitish stripe on the upper side of each needle. Viewed from slightly above (30–45°), soft even diffuse daylight, no cast shadow on the background. Four variations in length and density. Photorealistic botanical macro detail, sharp focus. No scale-like leaves, no pot, no text, no watermark, no magenta colour spill on the edges.
```
