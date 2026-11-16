// The air between the camera and the world (ToonAtmosphere): a painted aerial perspective for every toon shader, on top
// of the scene's own fog. Far things melt toward the sky's horizon colour, and toward a warm glow when you look at the
// sun; the air is thick in the valleys and thins with height (an exponential fall-off, integrated along the view ray),
// so the valley floor under you is hazy while a peak across the range stays crisp, and the layers of hills step back
// one behind another. Set by Scripts/Fx/ToonLook.cs as globals; when it has not run (or the look is off), _ToonAtmoParams
// is all zero and this returns the colour untouched.
// Include after Core.hlsl.
#ifndef OODA_TOON_ATMOSPHERE_INCLUDED
#define OODA_TOON_ATMOSPHERE_INCLUDED

float4 _ToonAtmoParams;   // x: density at the base height (1/m), y: fall-off with height (1/m), z: the most it hides (0..1), w: base height (m)
half4 _ToonAtmoColor;     // the haze away from the sun (the sky's horizon)
half4 _ToonAtmoSunColor;  // the haze toward the sun (rgb), a: how tight the glow is
float4 _ToonAtmoSunDir;   // toward the sun, world

// How much of the haze stands between the camera and a point (0 = none, up to _ToonAtmoParams.z).
half ToonAtmoAmount(float3 positionWS)
{
    float3 d = positionWS - GetCameraPositionWS();
    float dist = length(d);
    float b = _ToonAtmoParams.y;
    // the density integrated along the ray: thinner the higher the ray runs
    float k = clamp(d.y * b, -12.0, 12.0);
    float integ = abs(k) > 1e-3 ? (1.0 - exp(-k)) / k : 1.0 - 0.5 * k;
    float camH = clamp((GetCameraPositionWS().y - _ToonAtmoParams.w) * b, -2.0, 30.0);
    float optical = _ToonAtmoParams.x * exp(-camH) * dist * integ;
    return saturate(1.0 - exp(-optical)) * _ToonAtmoParams.z;
}

half3 ToonAtmoHaze(float3 positionWS)
{
    float3 dir = normalize(positionWS - GetCameraPositionWS());
    half toSun = pow(saturate(dot(dir, _ToonAtmoSunDir.xyz)), max(_ToonAtmoSunColor.a, 1.0));
    return lerp(_ToonAtmoColor.rgb, _ToonAtmoSunColor.rgb, toSun);
}

// The colour seen through the air. scale: 1 for the world, less for what must stay readable.
half3 ToonAtmosphere(half3 col, float3 positionWS, half scale)
{
    half amt = ToonAtmoAmount(positionWS) * scale;
    return lerp(col, ToonAtmoHaze(positionWS), amt);
}

#endif
