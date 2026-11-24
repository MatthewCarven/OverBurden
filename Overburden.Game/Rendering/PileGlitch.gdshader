shader_type spatial;

// THE PILE'S GLITCH (IDEAL-MACHINES-PLAN decision 30, IM-5's decision 16, IM-6's decision 14): a block the dump writes
// back into the world glitches cyan as it appears, then fades over about a second — "cyan is entanglement at work".
// CollapseGlitch's look on a voxel, drawn as one MultiMesh, so the seed and the fade ride each instance's custom data:
// a MultiMeshInstance3D's instance uniforms are one per node, shared by every instance in it. Every number is a look.
render_mode blend_mix, cull_back, unshaded, depth_draw_never, fog_disabled;

uniform vec3 glitch_colour : source_color = vec3(0.35, 1.0, 0.85);
uniform float glitch_rate = 12.0;
uniform float glitch_alpha : hint_range(0.0, 1.0) = 0.55;

varying float v_seed;
varying float v_fade;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void vertex() {
	// r: the instance's seed, from its position; g: what is left of its second, 1 down to 0.
	v_seed = INSTANCE_CUSTOM.r;
	v_fade = INSTANCE_CUSTOM.g;
	float tick = floor(TIME * glitch_rate + v_seed * 17.0);
	float jolt = hash(vec2(tick, v_seed));
	// A hair outside the block's faces, and now and then a sideways tear, a voxel's size of the collapse's.
	VERTEX += NORMAL * 0.004;
	VERTEX.x += step(0.85, jolt) * (jolt - 0.925) * 0.15;
}

void fragment() {
	float tick = floor(TIME * glitch_rate + v_seed * 17.0);
	float n = hash(floor(UV * 4.0) + vec2(tick, v_seed * 31.0));
	ALBEDO = glitch_colour * (0.6 + 0.4 * n);
	ALPHA = glitch_alpha * v_fade * step(0.45, n);
}
