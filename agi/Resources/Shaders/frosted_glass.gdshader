shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float blur_amount : hint_range(0.0, 5.0) = 2.0;
uniform vec4 mix_color : source_color = vec4(0.05, 0.05, 0.08, 0.45);
uniform float panel_alpha : hint_range(0.0, 1.0) = 0.8;

void fragment() {
    vec2 uv = SCREEN_UV;
    
    // Sunflower Circular Blur (89 Taps)
    // El problema del "cuadrado" era por usar una cuadrícula cartesiana (grid).
    // Usando una distribución circular densa (Sunflower seed) garantizamos
    // una forma redonda perfecta y una suavidad extrema.
    
    vec4 blurred = vec4(0.0);
    float total_weight = 0.0;
    
    // Escalamos el radio. Un slider de 5.0 nos dará 25 píxeles de radio.
    float max_radius = blur_amount * 5.0; 
    
    for (float i = 0.0; i < 89.0; i++) {
        // Distribución circular dorada
        float r = sqrt(i + 0.5) / sqrt(89.0);
        float theta = i * 2.39996323; // Golden angle
        
        vec2 offset = vec2(cos(theta), sin(theta)) * (r * max_radius) * SCREEN_PIXEL_SIZE;
        
        // Caída cuadrática suave para que los bordes del círculo se difuminen bien
        float weight = 1.0 - (r * r); 
        
        blurred += texture(screen_texture, uv + offset) * weight;
        total_weight += weight;
    }
    
    blurred /= total_weight;

    // ¡Solucionado desde la raíz! En ThemeManager.cs corregimos que la máscara fuera blanca,
    // ahora inyecta el verdadero color nativo (0.12, 0.12, 0.15) directamente a COLOR.rgb.
    
    // Para que NUNCA se vea transparente al 100%, escalamos ligeramente la opacidad
    // por si el slider en UI tiene un tope menor a 1.0 (ej. 0.9).
    float opacidad_real = clamp(panel_alpha * 1.1, 0.0, 1.0);
    
    // Mezclamos con el color nativo. Si la opacidad es 100%, será un 95% de color sólido 
    // y 5% de blur, para que recupere el color desactivado pero sin apagar el shader.
    float tint_strength = min(opacidad_real, 0.95);
    vec3 final_rgb = mix(blurred.rgb, COLOR.rgb, tint_strength);
    
    // final_alpha controla cuánto escritorio de KWin dejamos pasar. Al 100% será 1.0 (sólido).
    float final_alpha = max(opacidad_real, blurred.a);

    // Conservamos bordes redondeados perfectos.
    COLOR = vec4(final_rgb, final_alpha * COLOR.a);
}
