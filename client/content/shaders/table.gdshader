// The wooden table under the board (spec 2026-10-19-tabletop-look-design.md → Boards): planks at a 24" repeat, fading into the
// clear colour past the board's edge so the room has no visible end. World x and z are inches.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform sampler2D wood : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D wood_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec2 board_min;
uniform vec2 board_max;
uniform vec3 far_colour : source_color;
uniform float fade_from = 3.0;
uniform float fade_to = 30.0;

varying vec2 inches;

void vertex() {
	inches = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	vec2 uv = inches / 24.0;
	vec2 outside = max(max(board_min - inches, inches - board_max), vec2(0.0));
	float fade = smoothstep(fade_from, fade_to, length(outside));
	ALBEDO = texture(wood, uv).rgb * (1.0 - fade);
	NORMAL_MAP = texture(wood_normal, uv).xyz;
	ROUGHNESS = 0.55;
	SPECULAR = 0.4 * (1.0 - fade);
	EMISSION = far_colour * fade;
}
