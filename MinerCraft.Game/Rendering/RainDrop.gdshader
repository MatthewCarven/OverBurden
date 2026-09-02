shader_type spatial;

// One raindrop: a quad, stretched along the direction it is falling and turned to face the camera
// about that direction. DESIGN calls for "velocity-stretched billboards", and this is that — the
// approximation that reads as volumetric rain for one draw call.
//
// CYLINDRICAL RATHER THAN SPHERICAL BILLBOARDING, and that is what makes it read as rain rather
// than as confetti. A spherical billboard turns the whole quad to face the eye, so a falling drop
// tips over as you look down at it and the streaks stop being vertical. Pinning the long axis to
// the velocity and spinning only about that axis keeps every streak parallel to its own fall,
// which is the thing the eye reads as "rain" before it reads any individual drop.
//
// Unshaded, like the terrain, because there is no light in this game but the grid — and a drop is
// too small and too brief to sample it. What it gets instead is `tint`, which the session drives
// from the same sky brightness the terrain uses, so rain goes grey under a storm rather than
// staying bright against darkened ground.
render_mode unshaded, cull_disabled, depth_draw_never, blend_add;

uniform vec3 tint : source_color = vec3(0.72, 0.78, 0.88);
uniform float drop_length = 0.9;
uniform float drop_width = 0.018;
uniform float alpha : hint_range(0.0, 1.0) = 0.5;

// A drop that spawns next to the eye is a metre-long quad a few centimetres away, and it covers a
// quarter of the screen as a bright wedge. FOUND IN THE FIRST SCREENSHOT, and it is unavoidable
// rather than a bug in the spawn: the box is centred on the camera, so some drop is always near
// it. The fix is the one every rain shader uses — fade a drop in over the first couple of metres,
// which is inside the distance at which a real drop would be out of focus anyway.
uniform float near_fade_start = 0.6;
uniform float near_fade_end = 3.0;

varying float near;

void vertex() {
	// INSTANCE_CUSTOM carries nothing here; the fall direction comes out of the particle
	// transform, whose third column the process shader leaves as identity — so velocity is
	// reconstructed from the only thing that does vary, which is where the drop is going.
	//
	// Godot hands a particle's transform in MODEL_MATRIX. The process shader writes only the
	// translation, so the basis is identity and "down" in model space is world down: the quad is
	// built directly in world space here and the model matrix contributes position alone.
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 eye = INV_VIEW_MATRIX[3].xyz;

	vec3 fall = vec3(0.0, -1.0, 0.0);
	vec3 to_eye = eye - origin;

	// The quad's width axis: perpendicular to both the fall and the view, which is what "turned
	// about the fall direction to face the camera" means. Degenerate exactly when you look
	// straight along the fall — down at your feet in still rain — where a drop is a point anyway
	// and any perpendicular will do.
	vec3 across = cross(fall, to_eye);
	across = length(across) > 1e-5 ? normalize(across) : vec3(1.0, 0.0, 0.0);

	vec3 world = origin
		+ across * (VERTEX.x * drop_width)
		+ fall * (VERTEX.y * drop_length);

	near = smoothstep(near_fade_start, near_fade_end, length(to_eye));

	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
}

void fragment() {
	// Softened along the length so a streak fades out at both ends instead of stopping dead,
	// which is most of what stops a quad reading as a quad.
	float along = 1.0 - abs(UV.y * 2.0 - 1.0);
	ALBEDO = tint * alpha * along * near;
}
