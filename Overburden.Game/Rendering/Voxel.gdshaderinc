// The voxel shader's body, shared by Voxel.gdshader (rock) and VoxelGlass.gdshader (glass) so
// the two cannot drift: one lighting, one fog, one wet sheen. The including file owns
// shader_type and render_mode — the only lines that differ — and glass defines VOXEL_GLASS for
// its alpha. Split 2026-09-25 for docs/GLASS-PLAN.md; everything below was Voxel.gdshader.

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

// STRAIN, 0 or 1 per face, off UV2.y — M9b's warning sign. The mesher writes 1 on every face of
// a voxel the settled support field has at risk, in the merge key, so it costs quads only where
// it fires and a remesh only when the set of tinted faces changes. Constant across a quad, so the
// interpolation never lands between the two values.
varying float strain;

// WORLD SPACE, and carrying it is the whole fix for a bug that was cosmetic until M7c.
//
// `NORMAL` inside fragment() is in VIEW space in Godot 4. face_tint() below asks "is this face
// pointing up?", which in view space means "up relative to the camera" — so the fake directional
// shading rotated with the player and the top of a block dimmed as you looked down at it. Nobody
// filed it as a bug because it reads as a plausible lighting model right up until you turn.
//
// It stopped being cosmetic the moment the wet sheen arrived, because a sheen is *genuinely*
// view-dependent: with both terms sliding, there is no way to tell the one that is supposed to
// move from the one that is not.
//
// This costs nothing. A chunk instance's transform is a pure translation (Basis.Identity — see
// Main.OnMeshReady), so the model-space NORMAL vertex() sees IS the world normal, with no matrix
// multiply. If a chunk ever gains a rotation this becomes MODEL_NORMAL_MATRIX * NORMAL.
varying vec3 world_normal;

// A uniform with no diurnal cycle behind it yet, and it exists precisely so that one can be
// added without touching the light grid. The alternative — scaling the skylight seed and
// reflooding — relights the world every frame, and knowing that now is what stops someone
// implementing it that way later.
#ifdef VOXEL_GLASS
uniform float glass_alpha : hint_range(0.0, 1.0) = 0.15;
#endif

uniform float sky_brightness : hint_range(0.0, 2.0) = 1.0;

uniform vec3 sky_tint : source_color = vec3(1.0, 1.0, 1.0);

// Warm, so a lamp reads as a lamp against daylight rather than as a brighter patch of it.
uniform vec3 block_tint : source_color = vec3(1.0, 0.86, 0.66);

// Distance fade, moved out of the WorldEnvironment and into here at M5d. Same density and the
// same colour Godot's fog was configured with, so a daylit surface looks exactly as it did.
uniform float fog_density : hint_range(0.0, 0.05) = 0.004;
uniform vec3 fog_colour : source_color = vec3(0.65, 0.7, 0.78);

// HOW WET THE OPEN GROUND IS, 0..1, straight off Weather.WetnessAt — one global number, pushed
// once a frame, and this is where M5's bill gets collected.
//
// Wetness per fragment is `wetness * skylight`, and skylight is already COLOR.r per vertex. So a
// rainstorm changes ONE uniform: nothing enters the merge key, nothing is remeshed, and a storm
// costs exactly zero of the streaming budget. DESIGN's stated reason for keeping the two light
// channels separate rather than pre-multiplying them into one was this exact term — it is the
// first thing to spend what M5a paid for in vertex bytes.
//
// It also means rain stops at the mine entrance for free. A tunnel has no sky exposure, so the
// product is zero and the sheen ends exactly where daylight does, with nothing testing for
// "indoors".
uniform float wetness : hint_range(0.0, 1.0) = 0.0;

// Wet rock is darker rock. The larger half of the effect by a distance — the sheen is what the
// eye notices, but this is what makes the ground look soaked rather than varnished.
uniform float wet_darkening : hint_range(0.0, 1.0) = 0.35;

// And shinier. A cheap Fresnel rim rather than a specular lobe, because there is no light
// direction in this game to reflect: the sky is the source, so the sheen brightens at grazing
// angles and toward the fog colour, which is the sky's own colour.
uniform float wet_sheen : hint_range(0.0, 2.0) = 0.5;

// The A/B seam for the fix described on world_normal, and the reason it exists rather than the
// bug simply being fixed: the old look is the one that shipped through five milestones and was
// liked, so which of the two is preferred is a person's call and not a correctness argument.
// True restores the pre-M7c behaviour exactly. A single screenshot barely separates the arms —
// the artefact is that the tint MOVES — so the comparison wants the same wall at two yaws, which
// is what --yaw exists for.
uniform bool tint_in_view_space = false;

// WHAT AT-RISK ROCK LOOKS LIKE. A dusty red-brown the surface is pulled toward, and a darkening
// with it — attributable rather than pretty, because M9-PLAN decision 11's whole argument is that
// a purely random collapse cannot be learned from. Taste; a screenshot judges it, and the crack
// texture is the cosmetics pass's. --supporttint=0 is the control.
uniform vec3 strain_tint : source_color = vec3(0.78, 0.30, 0.22);
uniform float strain_amount : hint_range(0.0, 1.0) = 0.6;

void vertex() {
	layer = UV2.x;
	strain = UV2.y;
	shade = COLOR.rg;
	world_normal = NORMAL;
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

	// Rain only falls where the sky reaches, so wetness is gated on sky exposure rather than on
	// total light. A lamp-lit tunnel is bright and bone dry, which is the whole point.
	float wet = wetness * shade.x;

	// No ambient floor. An unlit cave is black, which is DESIGN's intent — M5 supplies the
	// darkness and M13 is what makes it a hazard. If M5d says it is unusable rather than dark,
	// the lever is one added constant here, not a change to the grid.
	//
	// face_tint takes the WORLD normal — see world_normal for why that is a fix and not a tidy-up.
	vec3 surface = texture(albedo, vec3(UV, layer)).rgb
		* face_tint(tint_in_view_space ? NORMAL : world_normal)
		* mix(1.0, 1.0 - wet_darkening, wet)
		* lit;

	// Strain multiplies the lit surface rather than replacing it, so an at-risk roof in a dark
	// chamber stays dark: the sign reads where there is light to read it by, which is where a
	// lamp is, which is where the player is.
	surface *= mix(vec3(1.0), strain_tint, strain * strain_amount);

	// The sheen, and this one genuinely belongs in view space: it is a reflection, so NORMAL and
	// the view vector are both wanted relative to the eye. VERTEX is the fragment in view space,
	// so -VERTEX normalised points back at the camera.
	//
	// Added rather than multiplied, and scaled by the sky the surface can actually see: a wet
	// floor deep in a lit chamber must not glow, and shade.x is already zero there.
	float grazing = 1.0 - clamp(dot(normalize(NORMAL), normalize(-VERTEX)), 0.0, 1.0);
	surface += fog_colour * sky_brightness * shade.x
		* (wet_sheen * wet * grazing * grazing * grazing * grazing);

	// VERTEX is view space in a fragment shader, so its length is the distance to the eye.
	ALBEDO = fog(surface, length(VERTEX), shade.x);
#ifdef VOXEL_GLASS
	// Glass (docs/GLASS-PLAN.md, decision 2): the same light, fog and sheen as rock, seen
	// through. One alpha for every pane, an estimate; a thicker look is a bigger number.
	ALPHA = glass_alpha;
#endif
}
