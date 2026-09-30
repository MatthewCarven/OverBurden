shader_type spatial;

// A falling piece of a collapse (COLLAPSE-PLAN part c): the rock shader, lit by an instance
// uniform instead of baked corners — see VOXEL_PIECE in the include. Same render mode as rock.
render_mode cull_back, unshaded, fog_disabled;

#define VOXEL_PIECE
#include "res://Rendering/Voxel.gdshaderinc"
