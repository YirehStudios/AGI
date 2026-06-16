shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float blur_amount : hint_range(0.0, 5.0) = 2.5;
uniform vec4 mix_color : source_color = vec4(0.05, 0.05, 0.08, 0.4);
uniform float corner_radius = 8.0;

// Rounded box SDF (Signed Distance Field)
float rounded_box_sdf(vec2 center_pos, vec2 size, float radius) {
    vec2 q = abs(center_pos) - size + vec2(radius);
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
}

void fragment() {
    // We get the size of the rect from SCREEN_PIXEL_SIZE and UV
    // But since it's a CanvasItem, TEXTURE_PIXEL_SIZE is 1.0/size
    // Wait, TEXTURE_PIXEL_SIZE is for TEXTURE, which might not be set for a Panel.
    // So we use UV to calculate SDF.
    vec2 size = vec2(1.0, 1.0); // UV goes from 0 to 1
    vec2 center_pos = UV - 0.5; // Center at 0,0
    
    // We need the aspect ratio to keep corners circular.
    // Instead of using uniforms, we can use dFdx and dFdy to get actual pixel coordinates!
    vec2 pixel_pos = vec2(0.0);
    // Actually Godot 4 CanvasItem has access to the NODE's size via passing a uniform or calculating from UV.
    // To make it easy, we will pass the rect_size as a uniform.
}
