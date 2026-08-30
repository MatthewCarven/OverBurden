shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

// Texture ARRAY, never an atlas. Greedy meshing tiles UVs beyond 0..1 to cover a
// merged quad, which an atlas cannot express. See docs/DESIGN.md non-negotiable #3.
uniform sampler2DArray albedo : source_color, filter_nearest, repeat_enable;

// UV2.x = texture array layer (which material this face shows). UV2.y is spare: it carried
// light × AO until M5a, which needed two independent channels and moved them to COLOR.
//
//   COLOR.r = skylight   × ambient occlusion
//   COLOR.g = blocklight × ambient occlusion
//
// Godot stores COLOR as RGBA8 unorm and applies no colour-space conversion to it under a
// ShaderMaterial — both checked against 4.7 rather than assumed. AO is baked into both
// channels because it is geometric darkening, which keeps it out of this shader and out of
// any later day/night uniform.
varying float layer;
varying vec2 shade;

void vertex() {
	layer = UV2.x;
	shade = COLOR.rg;
}

void fragment() {
	// M5c owns the real combination — sky and block tints, a skyBrightness uniform, and the
	// NORMAL-derived face tint that replaces the directional light this still runs under.
	// Until then nothing emits block light, so this is ambient occlusion over the sun. Summed
	// rather than taking the sky channel alone so that both channels are actually consumed:
	// a mesher that wrote one into the other would otherwise look identical.
	ALBEDO = texture(albedo, vec3(UV, layer)).rgb * (shade.x + shade.y);
}
