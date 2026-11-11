// Value noise for the land and water shaders: cheap per pixel, for the large shapes it breaks up.
#ifndef OODA_LAND_NOISE_INCLUDED
#define OODA_LAND_NOISE_INCLUDED

// value noise, 0..1, smooth; cheap enough per pixel for the large shapes it breaks up
float LandHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float LandNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    float a = LandHash(i), b = LandHash(i + float2(1, 0)), c = LandHash(i + float2(0, 1)), d = LandHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// two octaves: the broad patches and the finer clumps inside them
float LandNoise2(float2 p) { return LandNoise(p) * 0.65 + LandNoise(p * 4.1 + 17.3) * 0.35; }

#endif
