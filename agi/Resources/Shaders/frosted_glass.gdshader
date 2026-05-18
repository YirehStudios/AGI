shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float blur_amount : hint_range(0.0, 5.0) = 2.0;
uniform vec4 mix_color : source_color = vec4(0.05, 0.05, 0.08, 0.45);

void fragment() {
	// Sample the screen behind this node at the requested mip level (controls blur radius).
	vec4 blurred = textureLod(screen_texture, SCREEN_UV, blur_amount);
	// Composite the tint over the blurred screen using correct alpha blending.
	// result.rgb = tint.rgb * tint.a + blurred.rgb * (1.0 - tint.a)
	COLOR = vec4(mix(blurred.rgb, mix_color.rgb, mix_color.a), 1.0);
}
