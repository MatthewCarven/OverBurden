shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

// Texture ARRAY, never an atlas. Greedy meshing tiles UVs beyond 0..1 to cover a
// merged quad, which an atlas cannot express. See docs/DESIGN.md non-negotiable #3.
uniform sampler2DArray albedo : source_color, filter_nearest, repeat_enable;

// UV2 carries the two per-vertex channels the mesher owns:
//   UV2.x = texture array layer (which material this face shows)
//   UV2.y = light × ambient occlusion, 0..1
// Light and AO are constant 1.0 until M5, but the channel exists now so adding them
// later is a mesher change and not a vertex format change. Non-negotiable #4.
varying float layer;
varying float shade;

void vertex() {
	layer = UV2.x;
	shade = UV2.y;
}

void fragment() {
	ALBEDO = texture(albedo, vec3(UV, layer)).rgb * shade;
}
