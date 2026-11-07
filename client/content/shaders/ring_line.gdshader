// A ring's outline at a constant width on the screen (readable rings plan, RingMesh.Line). Each segment is two triangles: every vertex
// is one end of the segment, UV2 the segment's other end (x, z; the ring is flat), UV.x the side (-1, +1), UV.y the width in pixels.
// The vertex moves on the screen across the segment by half the width, and back from the other end by half the width, so neighbouring
// segments overlap at their joins. COLOR is linear.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform float opacity = 1.0;

void vertex() {
	vec4 here = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	vec4 there = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(UV2.x, VERTEX.y, UV2.y, 1.0));
	vec2 a = here.xy / here.w * VIEWPORT_SIZE, b = there.xy / there.w * VIEWPORT_SIZE;
	vec2 along = b - a;
	along = length(along) > 1e-4 ? normalize(along) : vec2(1.0, 0.0);
	vec2 across = vec2(-along.y, along.x);
	vec2 shift = (across * UV.x - along) * UV.y * 0.5;
	here.xy += shift * 2.0 / VIEWPORT_SIZE * here.w;
	POSITION = here;
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ALPHA = COLOR.a * opacity;
}
