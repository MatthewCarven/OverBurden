shader_type spatial;

// The glass in a falling piece: VoxelGlass's render mode and alpha, lit as a piece.
render_mode blend_mix, cull_back, unshaded, fog_disabled;

#define VOXEL_GLASS
#define VOXEL_PIECE
#include "res://Rendering/Voxel.gdshaderinc"
