// The painter's hand on the ground (ToonPaint): what turns a flat sheet of one green into a painted meadow, with no
// texture to author or repeat. All of it is drawn in the pixel shader from the world position, so it never tiles:
// - dabs: flat patches a tone lighter and warmer or a tone darker and cooler, with brushy, wobbling edges, at three
//   sizes (a field's width, a garden's, a few steps), like blocked-in colour under a painting;
// - tufts: scattered three-blade brush ticks, dark or sun-bleached, at two sizes, each shown only while it is a few
//   pixels to a few dozen pixels big;
// - flowers: dots of white, yellow and pink in the meadows, close to;
// - rock: angular facets a tone apart with an inked crack between them;
// - snow: blue drift shadows;
// - hedges: a dark green line along every field's edge, and a track between two crops.
// Everything fades out before it gets smaller than a pixel, so nothing shimmers. _ToonPaintOff = 1 turns it off.
// Include after LandNoise.hlsl.
#ifndef OODA_TOON_PAINT_INCLUDED
#define OODA_TOON_PAINT_INCLUDED

float _ToonPaintOff;
half _ToonPaintOn() { return _ToonPaintOff > 0.5 ? 0.0 : 1.0; }

float2 PaintHash2(float2 c)
{
    return frac(sin(float2(dot(c, float2(127.1, 311.7)), dot(c, float2(269.5, 183.3)))) * 43758.5453);
}

// Flat dabs with brushy edges: +1 in a light dab, -1 in a dark one, 0 between.
float PaintDabs(float2 p, float size, float px)
{
    float2 q = p / size;
    q += (float2(LandNoise(q * 2.3 + 7.1), LandNoise(q * 2.3 + 31.7)) - 0.5) * 0.9;   // (warped: a brush's edge, not a blob's)
    float n = LandNoise(q) * 0.7 + LandNoise(q * 2.7 + 11.0) * 0.3;
    float aa = px / size * 1.5 + 0.008;
    return smoothstep(0.60 - aa, 0.60 + aa, n) - (1.0 - smoothstep(0.38 - aa, 0.38 + aa, n));
}

float PaintSegment(float2 d, float2 b)
{
    float h = saturate(dot(d, b) / dot(b, b));
    return length(d - b * h);
}

// Scattered tufts, one to a cell (in rather more than half the cells): three blades fanning from a foot.
// Returns how much of the pixel a blade covers; rnd is the tuft's own random number.
float PaintTufts(float2 p, float cell, float px, out float rnd)
{
    float2 g = p / cell;
    float w = px / cell;                              // cells per pixel
    float2 id = floor(g), f = frac(g) - 0.5;
    float2 h = PaintHash2(id);
    rnd = frac(h.x * 7.13 + h.y * 3.71);
    float2 d = (f - (h - 0.5) * 0.45) / lerp(0.55, 1.0, frac(h.x * 13.7));   // (no two the same size)
    float a = (rnd - 0.5) * 1.1 + 0.5;                // all lean roughly one way, as grass in a wind
    float s = sin(a), c = cos(a);
    d = float2(c * d.x + s * d.y, -s * d.x + c * d.y);
    float dist = min(PaintSegment(d, float2(0.0, 0.26)), min(PaintSegment(d, float2(-0.05 - 0.10 * h.x, 0.20)), PaintSegment(d, float2(0.05 + 0.10 * h.y, 0.17))));
    float thick = 0.03 * (1.0 - saturate(d.y / 0.27)) + 0.006;   // a blade tapers to its tip
    float cover = 1.0 - smoothstep(thick - w, thick + w, dist);
    // shown while a tuft is from a few pixels to a few dozen across
    float shown = (1.0 - smoothstep(0.03, 0.09, w)) * step(0.4, h.y);
    return cover * shown;
}

// The same, but never drawn close up (the big tufts would be the size of a house there).
float PaintTuftsFar(float2 p, float cell, float px, out float rnd)
{
    float w = px / cell;
    return PaintTufts(p, cell, px, rnd) * smoothstep(0.004, 0.012, w);
}

// Rock: facets a tone apart, a crack between. Returns the tone (x: -1..1) and the crack (y: 0..1).
float2 PaintFacets(float2 p, float cell, float px)
{
    float2 g = p / cell;
    float w = px / cell;
    float2 id = floor(g), f = frac(g);
    float f1 = 8.0, f2 = 8.0, tone = 0.0;
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            float2 o = float2(x, y);
            float2 h = PaintHash2(id + o);
            float2 r = o + h - f;
            float d = abs(r.x) + abs(r.y) * 0.8;      // (a diamond's distance: angular chips, not round cells)
            if (d < f1) { f2 = f1; f1 = d; tone = h.x * 2.0 - 1.0; }
            else if (d < f2) f2 = d;
        }
    float crack = 1.0 - smoothstep(0.03 - w, 0.05 + w, f2 - f1);
    return float2(tone, crack * (1.0 - smoothstep(0.05, 0.15, w)));
}

// A line along where a layer's weight crosses a half: halfWidth in metres.
float PaintEdge(float wgt, float3 ws, float halfWidth)
{
    float2 grad = float2(ddx(wgt), ddy(wgt));
    float px = max(length(ddx(ws)), length(ddy(ws)));             // metres per pixel
    float perM = length(grad) / max(px, 1e-4);
    if (perM < 1e-4) return 0.0;
    float distM = abs(wgt - 0.5) / perM;
    return (1.0 - smoothstep(halfWidth - px, halfWidth + px, distM)) * (1.0 - smoothstep(halfWidth * 0.6, halfWidth * 2.5, px));
}

// The ground's colour, painted. grass, forest, rock, snow, crops: how much of each the pixel is (0..1).
half3 PaintGround(half3 base, float3 ws, half grass, half forest, half rock, half snow, half crops)
{
    // (metres a pixel covers, taken once here: no gradients inside the branches below)
    float px = max(length(ddx(ws)), length(ddy(ws)));
    if (_ToonPaintOff > 0.5) return base;
    float2 p = ws.xz;
    half green = saturate(grass + forest * 0.7);

    // the dabs: two sizes, the big one always, the small one near and middling
    float big = PaintDabs(p, 150.0, px), small = PaintDabs(p + 91.3, 24.0, px);
    small *= 1.0 - smoothstep(4.0, 12.0, px);
    // and close to, small clumps a few steps across
    float tiny = PaintDabs(p + 37.9, 5.0, px) * (1.0 - smoothstep(0.8, 2.5, px));
    float tone = clamp(big * 0.75 + small * 0.6 + tiny * 0.45, -1.0, 1.0);
    // (the meadow's green a little deeper and less lime under it all)
    base *= lerp(half3(1, 1, 1), half3(0.90, 0.95, 0.84), grass);
    half amount = saturate(green + rock * 0.7 + snow * 0.8 + crops * 0.3);
    half3 light = lerp(half3(1.06, 1.05, 1.02), half3(1.13, 1.09, 0.84), green);      // grass bleaches yellow in the sun
    half3 dark = lerp(half3(0.84, 0.86, 0.94), half3(0.76, 0.90, 0.88), green);       // and goes blue-green in the lush hollows
    dark = lerp(dark, half3(0.86, 0.91, 1.0), snow);                                  // snow: only blue drift shadows
    light = lerp(light, half3(1, 1, 1), snow);
    base *= lerp(half3(1, 1, 1), tone > 0 ? light : dark, abs(tone) * amount);

    // the tufts on the grass
    if (grass > 0.05)
    {
        // (two sizes only: bigger ones, tried, read as footprints stamped on the meadow from the air)
        float r1, r2;
        float t = PaintTufts(p, 1.7, px, r1);
        float t2 = PaintTuftsFar(p + 13.7, 6.5, px, r2);
        float r = t > t2 ? r1 : r2;
        float cover = max(t, t2 * 0.7) * grass;
        half3 blade = r < 0.62 ? base * half3(0.70, 0.83, 0.80) : base * half3(1.18, 1.13, 0.82);
        base = lerp(base, blade, cover * 0.7);

        // flowers, close to: a dot in one cell in five where the big dabs are light
        float2 g = p / 3.1;
        float w = px / 3.1;
        if (w < 0.12)
        {
            float2 h = PaintHash2(floor(g) + 3.0);
            float d = length(frac(g) - 0.5 - (h - 0.5) * 0.6);
            float dot0 = (1.0 - smoothstep(0.07 - w, 0.07 + w, d)) * step(0.8, frac(h.x * 5.3 + h.y)) * saturate(big + 0.6) * (1.0 - smoothstep(0.04, 0.12, w));
            half3 petal = h.x < 0.4 ? half3(0.98, 0.97, 0.90) : h.x < 0.75 ? half3(0.98, 0.84, 0.30) : half3(0.95, 0.60, 0.72);
            base = lerp(base, petal, dot0 * grass);
        }
    }

    // the rock: chips and cracks
    if (rock > 0.05)
    {
        float2 f = PaintFacets(p, 13.0, px);
        base *= lerp(1.0, 1.0 + f.x * 0.09, rock);
        base = lerp(base, base * half3(0.55, 0.55, 0.70), f.y * rock * 0.8);
    }
    return base;
}

#endif
