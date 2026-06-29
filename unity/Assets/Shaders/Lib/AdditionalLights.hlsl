#ifndef UNWRITTEN_ADDITIONAL_LIGHTS_INCLUDED
#define UNWRITTEN_ADDITIONAL_LIGHTS_INCLUDED

// Single source of truth for sampling URP ADDITIONAL lights (lamps / torches / portals = point & spot lights)
// in our hand-written lit shaders. Every custom lit surface (GroundBlend, FoliageWind, …) calls this instead of
// re-implementing the loop — so the one correct implementation can't be copied wrong per-shader.
//
// ⚠️ THE TRAP THIS FILE EXISTS TO PREVENT (URP 17.x, learned the hard way 2026-06-29):
// the 2-arg GetAdditionalLight(i, positionWS) returns shadowAttenuation = 1 — it NEVER samples shadows. Only the
// 3-arg overload (…, half4 shadowMask) calls AdditionalLightShadow(). Using the 2-arg form makes the surface light
// up under a lamp but never receive its cast shadow. This file always uses the 3-arg overload. Don't "simplify" it.
//
// REQUIREMENTS of the including shader (can't live in an .hlsl, must be in the .shader pass):
//   1. Include URP Lighting BEFORE this file:  #include ".../ShaderLibrary/Lighting.hlsl"
//   2. Declare the variant pragmas in the ForwardLit pass:
//        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
//        #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
//      Without _ADDITIONAL_LIGHTS the loop compiles out (returns 0); without _ADDITIONAL_LIGHT_SHADOWS the lamp
//      lights but casts no shadow. (Also requires URP asset: Additional Lights = Per Pixel + Cast Shadows on.)

// Summed diffuse contribution of all additional lights at a world position, INCLUDING their realtime shadows.
// N = surface normal. doubleSided = light the back face too (foliage cards / thin sprites) so backs aren't black.
half3 ApplyAdditionalLightsDiffuse(float3 positionWS, half3 N, bool doubleSided)
{
    half3 sum = half3(0.0h, 0.0h, 0.0h);
#if defined(_ADDITIONAL_LIGHTS)
    uint count = GetAdditionalLightsCount();
    for (uint li = 0u; li < count; li++)
    {
        // 3-arg overload (shadowMask = 1,1,1,1 for fully dynamic / no baked lightmaps) — this is what samples shadows.
        Light l = GetAdditionalLight(li, positionWS, half4(1.0h, 1.0h, 1.0h, 1.0h));
        half ndl = saturate(dot(N, l.direction));
        if (doubleSided) ndl = max(ndl, saturate(dot(-N, l.direction)));
        sum += l.color * (ndl * l.distanceAttenuation * l.shadowAttenuation);
    }
#endif
    return sum;
}

#endif // UNWRITTEN_ADDITIONAL_LIGHTS_INCLUDED
