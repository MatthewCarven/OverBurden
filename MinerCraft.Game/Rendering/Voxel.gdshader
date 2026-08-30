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

// Distance fade, moved out of the WorldEnvironment and into here at M5d. Same density and the
// same colour Godot's fog was configured with, so a daylit surface looks exactly as it did.
uniform float fog_density : hint_range(0.0, 0.05) = 0.004;
uniform vec3 fog_colour : source_color = vec3(0.65, 0.7, 0.78);

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

// Fog is in-scattered light along the view ray, so in a dark place there is none to scatter.
//
// WHY THIS IS NOT THE ENVIRONMENT'S FOG. Godot's WorldEnvironment fog is one colour for the
// whole world, and it was still that colour a hundred metres underground: distant tunnel walls
// faded towards daylight blue, which lights the dark with the one thing M5 exists to remove. The
// fix is to scale the fog by the fragment's own sky exposure, which is already in COLOR.r. Then
// a lit hillside hazes exactly as before and a mine fades to black — and both fade, so the edge
// of the loaded world still has something to hide behind.
//
// Sky exposure, not total light: a lamp is a metre away and does not scatter across the room.
// Multiplying by sky_brightness as well keeps the fog on the diurnal uniform, so night fades to
// black without reflooding anything.
vec3 fog(vec3 colour, float depth, float sky) {
	float amount = 1.0 - exp(-fog_density * depth);
	return mix(colour, fog_colour * sky_brightness * sky, amount);
}

void fragment() {
	vec3 lit = shade.x * sky_tint * sky_brightness + shade.y * block_tint;

	// No ambient floor. An unlit cave is black, which is DESIGN's intent — M5 supplies the
	// darkness and M13 is what makes it a hazard. If M5d says it is unusable rather than dark,
	// the lever is one added constant here, not a change to the grid.
	vec3 surface = texture(albedo, vec3(UV, layer)).rgb * face_tint(NORMAL) * lit;

	// VERTEX is view space in a fragment shader, so its length is the distance to the eye.
	ALBEDO = fog(surface, length(VERTEX), shade.x);
}
