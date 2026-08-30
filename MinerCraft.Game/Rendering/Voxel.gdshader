shader_type spatial;

// UNSHADED, and the directional light is gone from the scene with it — settled 2026-08-30.
// The light grid is the only light there is. What this replaces is Godot's sun plus a low
// ambient fill, which had two long-standing complaints against it: faces angled away went
// nearly black, and the sun picked out bright and dark slivers along the integer heightmap's
// risers. With no sun there are no slivers, and the terracing goes back to being geometry
// rather than a lighting artefact.
render_mode cull_back, unshaded;

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

// A uniform with no diurnal cycle behind it yet, and it exists precisely so that one can be
// added without touching the light grid. The alternative — scaling the skylight seed and
// reflooding — relights the world every frame, and knowing that now is what stops someone
// implementing it that way later.
uniform float sky_brightness : hint_range(0.0, 2.0) = 1.0;

uniform vec3 sky_tint : source_color = vec3(1.0, 1.0, 1.0);

// Warm, so a lamp reads as a lamp against daylight rather than as a brighter patch of it.
uniform vec3 block_tint : source_color = vec3(1.0, 0.86, 0.66);

void vertex() {
	layer = UV2.x;
	shade = COLOR.rg;
}

// The cheap fake for directional shading that every voxel game uses: top brightest, the two
// horizontal axes different from each other so a box reads as a box, bottom darkest.
//
// COMPUTED HERE RATHER THAN BAKED INTO THE VERTEX, and that is a decision. A quad has exactly
// one normal, so this costs nothing per fragment worth counting — while baking it would put a
// *look* constant into the merge key, where retuning it means remeshing the world. These four
// numbers are taste and M5d judges them; only their being in a shader is structural.
float face_tint(vec3 n) {
	if (n.y > 0.5) {
		return 1.0;
	}
	if (n.y < -0.5) {
		return 0.5;
	}
	return abs(n.x) > 0.5 ? 0.65 : 0.8;
}

void fragment() {
	vec3 lit = shade.x * sky_tint * sky_brightness + shade.y * block_tint;

	// No ambient floor. An unlit cave is black, which is DESIGN's intent — M5 supplies the
	// darkness and M13 is what makes it a hazard. If M5d says it is unusable rather than dark,
	// the lever is one added constant here, not a change to the grid.
	ALBEDO = texture(albedo, vec3(UV, layer)).rgb * face_tint(NORMAL) * lit;
}
