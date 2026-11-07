// Placement preview block: natural unshaded block colours when valid (as in the original), or tinted red when blocked.
// Writes depth so the 1.03x selector brackets, outward orientation arrow, and red attachment beam occlude cleanly
// against the solid part of the previewed block rather than bleeding through its interior.
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_always;

uniform sampler2D albedo_atlas : source_color, filter_nearest, repeat_disable;
uniform vec4 tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float tint_amount = 0.0;
uniform float opacity = 1.0;
uniform bool cutout = false;

void fragment() {
	vec4 c = texture(albedo_atlas, UV);
	if (cutout && c.a < 0.5) {
		discard;
	}
	ALBEDO = mix(c.rgb, tint.rgb, tint_amount);
}
