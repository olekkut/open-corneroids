// The placement ghost: the object's own textured model, see-through and tinted green (fits) or red (blocked), so
// the player sees which way its faces point before placing it. Unshaded, so the tint reads the same in any light.
// Depth is tested like any transparent material (terrain in front hides the ghost) but never written, so the
// footprint's wire box and the faces behind the model still show through the model itself.
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never, blend_mix;

uniform sampler2D albedo_atlas : source_color, filter_nearest, repeat_disable;
uniform vec4 tint : source_color = vec4(0.35, 1.0, 0.45, 1.0);
uniform float tint_amount = 0.35;
uniform float opacity = 0.7;
uniform bool cutout = false;

void fragment() {
	vec4 c = texture(albedo_atlas, UV);
	if (cutout && c.a < 0.5) {
		discard;
	}
	ALBEDO = mix(c.rgb, tint.rgb, tint_amount);
	ALPHA = opacity;
}
