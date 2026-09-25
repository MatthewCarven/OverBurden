shader_type spatial;

// UNSHADED, and the directional light is gone from the scene with it — settled 2026-08-30.
// The light grid is the only light there is. What this replaces is Godot's sun plus a low
// ambient fill, which had two long-standing complaints against it: faces angled away went
// nearly black, and the sun picked out bright and dark slivers along the integer heightmap's
// risers. With no sun there are no slivers, and the terracing goes back to being geometry
// rather than a lighting artefact.
//
// FOG IS OURS, not the environment's — see fog() below for why one that ignores the light grid
// cannot be right in a game about going underground.
render_mode cull_back, unshaded, fog_disabled;

// The body is shared with the glass shader: see Voxel.gdshaderinc.
#include "res://Rendering/Voxel.gdshaderinc"
