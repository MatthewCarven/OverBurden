shader_type spatial;

// The Tier A water surface: one flat plane per basin, at the level the accumulator solved.
//
// UNSHADED, like the terrain, because the light grid is the only light in this game. What the
// surface gets instead is `sky_brightness` — the same uniform the terrain and the fog take — so a
// pond goes leaden under a storm rather than staying bright blue while everything around it darkens.
//
// DEPTH IS TESTED BUT NOT WRITTEN. Tested, so terrain in front of the water occludes it and a pond
// seen over a ridge is hidden by the ridge. Not written, so two surfaces at different levels do not
// punch holes in each other and the terrain *under* the water still draws — which is what makes a
// shallow pond read as shallow. M8 owns the real transparent pass; this is one plane per basin and
// needs no sorting inside itself because a plane cannot overlap itself.
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;

uniform vec4 water_colour : source_color = vec4(0.16, 0.31, 0.42, 0.60);
uniform float sky_brightness : hint_range(0.0, 2.0) = 1.0;

// Water seen edge-on reflects almost everything and water seen straight down reflects almost
// nothing, which is most of what makes a flat plane read as a liquid rather than as a coloured
// sheet. Cheap Schlick, no environment to reflect - the sky colour stands in for one.
uniform vec3 sheen_colour : source_color = vec3(0.62, 0.70, 0.80);
uniform float sheen : hint_range(0.0, 1.0) = 0.45;

void fragment() {
	// NORMAL in view space is exactly right here, unlike the terrain's face tint: this is a
	// reflection, so it genuinely depends on where the eye is. See Voxel.gdshader's world_normal
	// for the case where it was not.
	float grazing = 1.0 - clamp(dot(normalize(NORMAL), normalize(-VERTEX)), 0.0, 1.0);
	float fresnel = grazing * grazing * grazing;

	ALBEDO = mix(water_colour.rgb, sheen_colour, fresnel * sheen) * sky_brightness;

	// More opaque edge-on, for the same reason. A pond you are standing beside is nearly a mirror
	// at its far edge and nearly clear at your feet.
	ALPHA = clamp(water_colour.a + fresnel * (1.0 - water_colour.a) * 0.8, 0.0, 1.0);
}
