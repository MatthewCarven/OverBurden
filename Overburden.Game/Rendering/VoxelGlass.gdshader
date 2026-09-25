shader_type spatial;

// Glass (docs/GLASS-PLAN.md, decision 2): the rock shader's body, blended. The only material in
// the chunk mesh's transparent surface — the water draws its own meshes — so this changes no
// existing picture. Godot sorts transparent instances, not faces: water seen through glass can
// sort wrong at some angles, the pass's stated cost, with an aquarium as its trigger.
render_mode blend_mix, cull_back, unshaded, fog_disabled;

#define VOXEL_GLASS
#include "res://Rendering/Voxel.gdshaderinc"
