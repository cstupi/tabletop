// The gaming mat (spec 2026-10-19-tabletop-look-design.md → Boards). One print, sampled twice — at its repeat, and turned half
// a circle and shifted half a repeat, which keeps a print's grid of slabs or ripples aligned — and blended by a slow value
// noise through the print's own height, so the repeat does not show; a gentle brightness and warmth drift on top. World x and
// z are inches.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform sampler2D print : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D print_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tile_inches = 12.0;
uniform float normal_depth = 1.0;

varying vec2 inches;

void vertex() {
	inches = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float value_noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
	vec2 a_uv = inches / tile_inches;
	vec2 b_uv = vec2(0.5, 0.75) - a_uv;
	vec4 a = texture(print, a_uv);
	vec4 b = texture(print, b_uv);
	float m = value_noise(inches / 17.0) * 0.65 + value_noise(inches / 6.5 + 11.0) * 0.35;
	float ha = a.a + (1.0 - m) * 1.2, hb = b.a + m * 1.2;
	float top = max(ha, hb) - 0.18;
	float wa = max(ha - top, 0.0), wb = max(hb - top, 0.0);
	float wsum = wa + wb;
	wa /= wsum; wb /= wsum;
	vec3 colour = a.rgb * wa + b.rgb * wb;
	float height = a.a * wa + b.a * wb;

	vec3 na = texture(print_normal, a_uv).xyz * 2.0 - 1.0;
	vec3 nb = texture(print_normal, b_uv).xyz * 2.0 - 1.0;
	nb.xy = -nb.xy;
	vec3 n = normalize(vec3((na.xy * wa + nb.xy * wb) * normal_depth, na.z * wa + nb.z * wb));

	float drift = value_noise(inches / 31.0 + 5.0);
	float warmth = value_noise(inches / 47.0 + 23.0) - 0.5;
	colour *= mix(0.9, 1.08, drift);
	colour *= vec3(1.0 + warmth * 0.08, 1.0, 1.0 - warmth * 0.08);

	ALBEDO = colour;
	NORMAL_MAP = n * 0.5 + 0.5;
	ROUGHNESS = mix(0.8, 0.95, height);
	SPECULAR = 0.35;
}
