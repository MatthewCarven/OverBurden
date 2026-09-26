shader_type spatial;

// THE CORRUPTION (COLLAPSE-PLAN decision 2b, part c decision 7): the next second's pieces, still
// rock, drawn over with a glitch — his "like digital corruption spreading". A shell a hair outside
// the piece's faces, flickering in blocks and jumping sideways a few frames in each second, so it
// reads as a signal breaking up rather than as a tint. Animated here, by TIME, so the overlay is
// rebuilt only when the queue's head changes. Every number below is a look, and his to judge.
render_mode blend_mix, cull_back, unshaded, depth_draw_never, fog_disabled;

// Per piece, so neighbouring pieces do not flicker in step.
instance uniform float glitch_seed = 0.0;

uniform vec3 glitch_colour : source_color = vec3(0.35, 1.0, 0.85);
uniform float glitch_rate = 12.0;
uniform float glitch_alpha : hint_range(0.0, 1.0) = 0.45;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void vertex() {
	float tick = floor(TIME * glitch_rate + glitch_seed * 17.0);
	float jolt = hash(vec2(tick, glitch_seed));
	// A hair outside the rock's faces, and now and then a sideways tear.
	VERTEX += NORMAL * 0.004;
	VERTEX.x += step(0.85, jolt) * (jolt - 0.925) * 0.6;
}

void fragment() {
	float tick = floor(TIME * glitch_rate + glitch_seed * 17.0);
	// Blocks of a quarter voxel that come and go.
	float n = hash(floor(UV * 4.0) + vec2(tick, glitch_seed * 31.0));
	ALBEDO = glitch_colour * (0.6 + 0.4 * n);
	ALPHA = glitch_alpha * step(0.45, n);
}
