shader_type particles;

// The rain's process shader. Two jobs: keep a box of drops falling around the player wherever
// they walk, and kill any drop that has reached the ground.
//
// WHY A PROCESS SHADER RATHER THAN A ParticleProcessMaterial. The kill is a texture read against
// a field of sky heights, which the built-in material has no way to express — and the kill is the
// whole feature. Everything else here would have been free out of the box.

// The sky-height field: how high the ground is, in world metres, for each 0.5 m cell of a 128 m
// window around the player. See SkyHeightField for why 0.5 m and not 1 m.
uniform sampler2D sky_height : filter_nearest, repeat_disable;
uniform vec2 field_origin = vec2(0.0, 0.0);
uniform float field_span = 128.0;

// The box drops live in, centred on the camera. Smaller than the field on purpose, so this can be
// retuned without anybody having to think about the field's resolution.
uniform vec3 box_centre = vec3(0.0, 0.0, 0.0);
uniform float box_half_width = 24.0;
uniform float box_height = 32.0;

uniform float fall_speed = 22.0;
uniform float drift = 1.2;

// 0..1, straight off Weather. Drops above this fraction of the emitter's amount are parked out of
// sight rather than emitted, which is how one particle system covers drizzle and downpour without
// the amount — a buffer size — changing at runtime.
uniform float intensity : hint_range(0.0, 1.0) = 0.0;

// A hash rather than the built-in RANDOM_SEED stream, because a drop that respawns has to land
// somewhere unrelated to where it died, and doing that from its index alone keeps this a pure
// function of (index, respawn count) — which is what stops the whole box respawning in a plane.
float hash11(float n) {
	return fract(sin(n * 127.1) * 43758.5453123);
}

vec3 hash31(float n) {
	return vec3(hash11(n), hash11(n + 17.13), hash11(n + 41.71));
}

// Where in the box a drop starts. Spread over the whole height as well as the footprint, so a
// storm that has just started is not a visible sheet descending from the ceiling of the box.
vec3 spawn(float n) {
	vec3 r = hash31(n);
	return box_centre + vec3(
		(r.x - 0.5) * 2.0 * box_half_width,
		(r.y - 0.5) * box_height,
		(r.z - 0.5) * 2.0 * box_half_width);
}

// Ground height under a world position, or a value nothing can be below where the field has no
// answer. The unknown sentinel is what makes "no loaded column" mean "do not kill" without a
// second texture.
float ground_at(vec3 world) {
	vec2 uv = (world.xz - field_origin) / field_span;
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
		return -1e9;
	}
	return texture(sky_height, uv).r;
}

void start() {
	// INDEX is stable for a particle across its whole life, so it is the identity a respawn
	// hashes against. NUMBER counts emissions, which is what makes consecutive lives differ.
	float n = float(INDEX) + float(NUMBER) * 0.6180339887;

	TRANSFORM[3].xyz = spawn(n);
	VELOCITY = vec3(
		(hash11(n + 3.7) - 0.5) * 2.0 * drift,
		-fall_speed * (0.85 + 0.3 * hash11(n + 9.1)),
		(hash11(n + 5.3) - 0.5) * 2.0 * drift);

	// The intensity gate. A drop that is not raining today is marked inactive rather than moved
	// somewhere harmless, so it costs nothing to draw as well as nothing to look at.
	ACTIVE = hash11(float(INDEX) * 0.0173) < intensity;
	CUSTOM.x = n;
}

void process() {
	vec3 position = TRANSFORM[3].xyz + VELOCITY * DELTA;

	// The kill, and also the recycle: a drop that has hit the ground and a drop that has fallen
	// out of the bottom of a box the player has walked away from are the same event, and both
	// want the same answer, which is to start again at the top.
	bool hit = position.y <= ground_at(position);
	bool escaped =
		position.y < box_centre.y - box_height * 0.5
		|| abs(position.x - box_centre.x) > box_half_width
		|| abs(position.z - box_centre.z) > box_half_width;

	// A HIT AND AN ESCAPE ARE STILL TESTED SEPARATELY THOUGH THEY DO THE SAME THING HERE, because
	// only one of them is a landing. This is where splashes were emitted from until M7c-2 cut them
	// (see Rain.cs), and a splash on an escape would have drawn a ring of impacts on thin air at
	// the box's edge, following the player around. Whatever brings splashes back needs the
	// distinction, so it stays.
	if (hit || escaped) {
		float n = CUSTOM.x + 7.0;
		CUSTOM.x = n;

		// Respawned at the top of the box rather than anywhere in it: a recycled drop entering
		// mid-air is visible as a streak appearing out of nothing, while the top edge is off
		// screen for anyone looking at the ground.
		vec3 fresh = spawn(n);
		fresh.y = box_centre.y + box_height * 0.5;
		position = fresh;

		VELOCITY = vec3(
			(hash11(n + 3.7) - 0.5) * 2.0 * drift,
			-fall_speed * (0.85 + 0.3 * hash11(n + 9.1)),
			(hash11(n + 5.3) - 0.5) * 2.0 * drift);

		ACTIVE = hash11(float(INDEX) * 0.0173) < intensity;
	}

	TRANSFORM[3].xyz = position;
}
