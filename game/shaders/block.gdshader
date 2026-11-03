// Blocks sample the original atlas directly (design doc §5.3, phase A): nearest filtering, no mipmaps.
// lightTextureAtlas.png is a white mask on the same grid, so it shares the UV and tints by the albedo.
shader_type spatial;
render_mode cull_back, specular_disabled;

uniform sampler2D albedo_atlas : source_color, filter_nearest, repeat_disable;
uniform sampler2D light_atlas : hint_default_transparent, filter_nearest, repeat_disable;
uniform float emission_energy = 1.6;
uniform bool cutout = false;

void fragment() {
	vec4 c = texture(albedo_atlas, UV);
	if (cutout && c.a < 0.5) {
		discard;
	}
	ALBEDO = c.rgb;
	ROUGHNESS = 0.9;
	EMISSION = c.rgb * texture(light_atlas, UV).a * emission_energy;
}
