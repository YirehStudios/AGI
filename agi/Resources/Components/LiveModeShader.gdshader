shader_type canvas_item;

// Aseguramos que el nombre coincida exactamente con lo que envía C#
uniform float voice_level : hint_range(0.0, 1.0) = 0.0;

void fragment() {
   // Centramos las coordenadas UV (de -1 a 1)
   vec2 uv = UV * 2.0 - 1.0;
   
   // Obtenemos el ángulo (a) y el radio (r) actual del pixel
   float a = atan(uv.y, uv.x);
   float r = length(uv);

   float time = TIME * 2.0; // Multiplicamos para que la onda se mueva un poco más rápido

   // Calculamos la agresividad de la deformación basada en el volumen
   // Si voice_level es 0, casi no hay deformación. Si es 1, se deforma mucho.
   float intensity = 0.01 + (voice_level * 0.20);

   // Sumamos capas de ruido basadas en senos y cosenos (magia trigonométrica)
   float noise = sin(a * 3.0 + time) * intensity;
   noise += cos(a * 2.0 - time * 0.8) * (intensity * 0.8);
   noise += sin(a * 4.0 + time * 1.2) * (intensity * 0.5);

   // El radio base también "palpita" o crece cuando el volumen sube
   float base_radius = 0.4 + (voice_level * 0.15);
   float final_radius = base_radius + noise;

   // Dibujamos el borde de forma suave (anti-aliasing)
   float alpha = 1.0 - smoothstep(final_radius - 0.02, final_radius + 0.02, r);

   // Asignamos el color final (Blanco puro, usando alpha para la forma)
   COLOR = vec4(0.5, 0.5, 0.5, alpha);
}