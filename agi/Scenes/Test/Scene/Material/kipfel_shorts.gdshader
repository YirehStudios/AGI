shader_type spatial;

// Esto permite que la falda se vea por ambos lados y use texturas
render_mode cull_disabled, blend_mix, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform sampler2D textura_albedo : source_color; // Aquí arrastras tu imagen de la falda
uniform vec2 desplazamiento;                    // Este valor lo controla el script C#
uniform float elasticidad : hint_range(0.0, 2.0) = 1.0;

void vertex() {
    // MÁSCARA DE PESO:
    // UV.y suele ir de 0 (arriba) a 1 (abajo).
    // Usamos pow(UV.y, 2.0) para que la cintura NO se mueva nada 
    // y el borde inferior de la falda tenga todo el movimiento.
    float mascara = pow(UV.y, 2.0); 
    
    // Aplicamos el movimiento de inercia que viene del script
    // VERTEX.x y VERTEX.z son los ejes horizontales
    VERTEX.x += desplazamiento.x * mascara * elasticidad;
    VERTEX.z += desplazamiento.y * mascara * elasticidad;
	// Dentro del shader en la función vertex(), cambia la línea del VERTEX.x por:
	float balanceo = sin(TIME * 5.0) * 0.01; // Un pequeño vaivén constante
	VERTEX.x += (desplazamiento.x + balanceo) * mascara * elasticidad;
	VERTEX.z += (desplazamiento.y + balanceo) * mascara * elasticidad;

}

void fragment() {
    // Aplicamos la textura para que la falda no se vea gris/blanca
    vec4 color = texture(textura_albedo, UV);
    ALBEDO = color.rgb;
    ALPHA = color.a;
}
