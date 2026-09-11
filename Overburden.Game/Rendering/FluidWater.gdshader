shader_type spatial;

// Tier B's water, M8d-2: the mesh FluidSurfaceMesher builds — merged tops, sides where water
// meets air, bottoms under falling cells — with depth in the colour, sides shaded by their
// normal, and a surface that moves between ticks instead of jumping at them.
//
// UNSHADED, like everything here: the light grid is the only light, and `sky_brightness` is the
// one uniform that dims the world. Tier A's plane (Water.gdshader) keeps its own shader and its
// own rules; the two differ in exactly the ways the M8d pass listed.
//
// DEPTH IS WRITTEN AND BACK FACES ARE CULLED (decision 4). One mesh with tops and sides needs its
// own faces to sort; writing depth does that for free, and culling the faces that look away
// leaves one front face per ray for a convex body, so the nearest face is what you see and the
// far side of a lake does not blend through its near side. Cost, stated in the plan: Tier B water
// seen through Tier A water shows the nearer only, and from inside the water you see none of it —
// the underwater tint is what says you are in it.
render_mode unshaded, cull_back, depth_draw_always, blend_mix;

// Where the surface stood at the previous tick is in UV.x (metres) and the water under the vertex
// in UV.y (metres) — FluidSurfaceMesher's two attributes, both constant across a merged quad
// because both are in its merge key.
//
// THE LERP IS THE PERCEPTUAL FLOOR ANSWERED THE WAY TIER A ANSWERS IT (decision 3). A cell's top
// jumps at 2 Hz; DESIGN's stated floor is that a surface two metres away jumping once a second is
// broken, not slow. `tick_phase` runs 0 → 1 over the half second after a tick lands, and the
// vertex rises from where it was to where it is. A cell that appeared rises from its floor; a cell
// that emptied has no vertex and vanishes at the tick — the one honest hole, stated in the plan.
uniform float tick_phase : hint_range(0.0, 1.0) = 1.0;

uniform float sky_brightness : hint_range(0.0, 2.0) = 1.0;

// Shallow and deep, and how fast one becomes the other (decision 2). Depth is the column's — wet
// cells under the vertex — not the optical path, so a puddle a few centimetres deep is nearly
// clear and a pit a metre deep is the deep colour. `depth_falloff` is per metre: at 0.9, a metre
// of water is 59% of the way to deep and three metres 93%. Look constants; a person picks them.
uniform vec4 shallow_colour : source_color = vec4(0.30, 0.50, 0.58, 0.30);
uniform vec4 deep_colour : source_color = vec4(0.06, 0.16, 0.30, 0.88);
uniform float depth_falloff : hint_range(0.1, 4.0) = 0.9;

// The Schlick sheen Water.gdshader has, unchanged: edge-on water is a mirror of the sky.
uniform vec3 sheen_colour : source_color = vec3(0.62, 0.70, 0.80);
uniform float sheen : hint_range(0.0, 1.0) = 0.45;

// The terrain's fake directional shade (Voxel.gdshader's face_tint) applied to the water's
// faces, gentler: a top is full, a side is darker, the underside of a falling drop darker still.
// So a lip reads as a lip and a fall has a front that is not the same blue as its top.
uniform float side_tint : hint_range(0.0, 1.0) = 0.78;
uniform float bottom_tint : hint_range(0.0, 1.0) = 0.6;

varying float water_depth;
varying vec3 world_normal;

void vertex() {
	VERTEX.y = mix(UV.x, VERTEX.y, tick_phase);
	water_depth = UV.y;
	// A chunk's transform is a pure translation, so the model normal is the world normal — the
	// terrain's world_normal argument, one shader over.
	world_normal = NORMAL;
}

float face_tint(vec3 n) {
	if (n.y > 0.5) {
		return 1.0;
	}
	if (n.y < -0.5) {
		return bottom_tint;
	}
	return side_tint;
}

void fragment() {
	float deep = 1.0 - exp(-depth_falloff * max(water_depth, 0.0));
	vec4 water = mix(shallow_colour, deep_colour, deep);

	// The reflection genuinely depends on where the eye is, so the view-space NORMAL is right
	// here — see Water.gdshader.
	float grazing = 1.0 - clamp(dot(normalize(NORMAL), normalize(-VERTEX)), 0.0, 1.0);
	float fresnel = grazing * grazing * grazing;

	ALBEDO = mix(water.rgb, sheen_colour, fresnel * sheen) * face_tint(world_normal) * sky_brightness;
	ALPHA = clamp(water.a + fresnel * (1.0 - water.a) * 0.8, 0.0, 1.0);
}
