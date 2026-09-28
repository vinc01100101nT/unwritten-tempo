// The map's standing water, shared by every screen pass that has to agree about it: the wet ground that draws it
// (WetGround.shader) and the ink outlines that fade under it (InkScreen.shader). All of it is set by WaterSurfaces /
// PlanarReflection / WetGround on the C# side; this file only reads it.
#ifndef UNWRITTEN_WATER_BODIES_INCLUDED
#define UNWRITTEN_WATER_BODIES_INCLUDED

// The map's bodies of water (WaterSurfaces): surface and floor of each, the first _WaterFilmCount of them sheets on
// level ground, the rest lakes; each square of the map carries its body's index + 1 in _WaterBodyTex.
float _WaterLevels[64];
float _WaterFloors[64];
float _WaterHeightCount, _WaterFilmCount, _WaterTolerance, _WaterRunoffPull, _WaterClarity, _WaterDrawn;
float4 _WaterRunoffRect, _WaterGridSize;
TEXTURE2D(_WaterBodyTex);

// One planar mirror per water height on screen (PlanarReflection); _MirrorsOn = 0 when none run.
float _MirrorHeights[8];
float _MirrorCount, _MirrorsOn;

// The body of water this pixel lies in — the body marked on its square, when its ground lies between that body's floor
// and surface — or -1.
int BodyIndex(float3 wp)
{
    int2 sq = (int2)floor((wp.xz - _WaterRunoffRect.xy) * _WaterRunoffRect.zw * _WaterGridSize.xy);
    if (any(sq < 0) || sq.x >= (int)_WaterGridSize.x || sq.y >= (int)_WaterGridSize.y) return -1;
    int id = (int)round(LOAD_TEXTURE2D(_WaterBodyTex, sq).r * 255.0) - 1;
    if (id < 0 || id >= (int)_WaterHeightCount) return -1;
    return (wp.y <= _WaterLevels[id] + _WaterTolerance && wp.y >= _WaterFloors[id]) ? id : -1;
}

// The mirror drawing this water height this frame, or -1.
int MirrorSlot(float h)
{
    [loop] for (int i = 0; i < 8; i++)
    {
        if (i >= (int)_MirrorCount) break;
        if (_MirrorHeights[i] == h) return i;
    }
    return -1;
}

// Is this body drawn as water this frame? Only while the wet ground is on screen at all, and — while mirrors run —
// only when its height has a mirror (a body without one is drawn dry, never with another height's reflection).
bool BodyShows(int body)
{
    if (body < 0 || _WaterDrawn < 0.5) return false;
    return _MirrorsOn < 0.5 || MirrorSlot(_WaterLevels[body]) >= 0;
}

bool IsLake(int body) { return body >= (int)_WaterFilmCount; }

// How much of what lies under the water at this pixel still shows through it, 0..1: water absorbs light the deeper it
// is, e^(-depth / clarity) (Beer–Lambert). A sheet on level ground is too thin to hide anything.
float UnderWaterShows(float3 wp, int body)
{
    if (!IsLake(body)) return 1.0;
    float depth = max(0.0, _WaterLevels[body] - wp.y);
    return exp(-depth / max(1e-4, _WaterClarity));
}

#endif
