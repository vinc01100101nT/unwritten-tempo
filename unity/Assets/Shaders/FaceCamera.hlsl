#ifndef FACE_CAMERA_INCLUDED
#define FACE_CAMERA_INCLUDED

// TURN A PLANT TO FACE THE CAMERA ABOUT ITS UPRIGHT AXIS, in its own space: its local +z ends up pointing at the camera
// (seen from above), so the picture standing on +z is always seen square-on and the plant's other crossed pictures keep
// their angles to it. Only the turn about the upright axis — a plant never leans or lies down. Used by the meadows'
// shader (MeadowFacing): from the top-down camera a meadow card's pictures are no longer seen edge-on (the user,
// 2026-10-28). Runs per corner on the graphics card; nothing is worked out per plant on the CPU.
void FaceCamera_float(float3 Position, out float3 Out)
{
    float3 cam = TransformWorldToObject(_WorldSpaceCameraPos);
    float2 d = cam.xz;
    float len = length(d);
    if (len < 1e-5) { Out = Position; return; }
    d /= len;
    // A turn whose sine is d.x and cosine d.y takes local +z onto (d.x, d.y).
    Out = float3(d.y * Position.x + d.x * Position.z, Position.y, -d.x * Position.x + d.y * Position.z);
}

void FaceCamera_half(half3 Position, out half3 Out)
{
    float3 o;
    FaceCamera_float(Position, o);
    Out = o;
}

#endif
