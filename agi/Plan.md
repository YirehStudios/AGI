* *Para empezar, control propio sobre archivos*
- *Presets.json se maneje con un boton de actualizar para que el programa no lo descargue cada que abran y solo para cuando el usuario quiera actualizarlo y el boton se pone cuando el preset cambia de version e.j: "Presetsv1.json" y ahi es cuando aparece actualizar y el sistema cuando lo descargue lo guarde como preset pero en settings su version para que sea eso lo que compare y no el mismo json, permitiendo que el usuario ponga sus propios modelos compatibles*
- *Empezar el multisoporte para todas las herramientas para facilitar la exportacion y desarrollo a futuro*
* *Verificar si llama.cpp con TurboQuant o cualquier motor*
* *Volver nativo sherpa y habilitar modo medium y high, medium siendo kokoro pero con puente en python y high sea Fish Audio o IndecTTS 3 o 2*
* *Empezar con los MCP servers Locales*
* *Empezar a conectar API en la interfaz de cualquier IA, y que AGI en modo Server pueda dar una API Funcinal para AGI o cualquier servicio que lo pueda usar*
* *Iniciar la investigacion del IDE*
* *Iniciar la investigacion de IA de imagenes y videos*
* *Migrar sherpa o el puente de sherpa a env o simil en Linux y encerrarlo, Y en Windows Windows Embeddable Package de Python (la versión ZIP oficial). Para que las versiones de python no afecte a todo lo que dependa de ello, como sherpa, su puente con websocket, comfy, etc... a futuro y usar pytorch para detectar gpu dedicada o x cosa para que no tenga problema*

Fase 1:
🗺️ Plan Maestro de Re-infraestructura (Versión Final)

Fase 1: El Cerebro del Entorno (EnvironmentManager)

    Crear el gestor global que detecte el SO (Windows, Linux, Android) y defina las "Feature Flags" (ej. Android = Solo UI por ahora; Escritorio = Full Local). Él dictará las reglas del juego.

Fase 2: El Buscador Dinámico (FileResolver)

    Crear utilidades para buscar archivos por extensión o permisos sin depender de nombres exactos, erradicando el hardcodeo de rutas.

Fase 3: El Gestor de Paquetes Modular (PackageManager / Installer)

    Quitarle toda la lógica de URLs y descargas de Llama, Whisper y Sherpa al SetupWizard.

    Crear un módulo dedicado que pregunte al EnvironmentManager qué SO es, y en base a eso, decida qué binario descargar (ej. Llama Vulkan para Linux, Llama .exe para Windows, o preparar el terreno para arm64 en Android a futuro).

    Dejar este módulo listo y limpio para que el día de mañana solo sea agregar un "DownloadPythonEnv()" o "DownloadComfy()".

Fase 4: Limpieza y Refactor del SetupWizard

    Modificar el SetupWizard.cs para que sea puramente visual y de flujo. Solo llamará al PackageManager para descargar/verificar dependencias y actualizará la barra de progreso, sin saber qué hay debajo del capó.


🗺️ Plan Maestro 2: Conquista de Windows

Fase 1: Enrutamiento Dinámico de Descargas (El Gestor de URLs)

    Objetivo: El SetupWizard ya no debe tener una sola URL rígida por motor. Necesita un sistema de "espejos".

    Acción: Crearemos variables duales (ej. LlamaUrlLinux y LlamaUrlWindows). Al iniciar, le preguntaremos al EnvironmentManager qué sistema es, y basándonos en eso, el instalador sabrá exactamente qué enlace proporcionarte. Tú solo pegarás los links ahí y el código hará el enrutamiento.

Fase 2: Extracción Multiplataforma Nativa (DownloadManager)

    Objetivo: Evitar fallos de descompresión en Windows.

    Acción: Actualmente, para los .zip, el DownloadManager intenta usar el comando unzip de Linux. En Windows esto fallará porque no viene por defecto. Modificaremos esa parte específica para usar la librería nativa de C# (System.IO.Compression.ZipFile), que descomprime .zip a la velocidad de la luz y funciona nativamente en Windows, Linux y Android sin depender de comandos de consola.

Fase 3: Preparación del Entorno Portable (Python Windows)

    Objetivo: Sentar las bases para la voz de alta calidad (Kokoro) y ComfyUI.

    Acción: Habilitar el PackageManager para que, cuando esté en Windows, sepa cómo descargar el "Windows Embeddable Package" (el .zip oficial de Python), lo extraiga en user://env/python, y lo configure para que funcione de manera totalmente portable sin tocar el registro del sistema del usuario.

Plan Maestro de Migración a uv (Linux)
Fase 1: Auditoría e Instalación Base

Objetivo: Asegurar que uv esté instalado en el sistema anfitrión Linux antes de intentar cualquier operación, modificando la auditoría de dependencias.

    Instrucciones para la IA asignada (Script DependencyInstaller.cs):

        Qué hacer: Modificar el método AuditSystemDependenciesAsync. Eliminar la verificación de python3 y el módulo venv (CheckPythonVenv()). En su lugar, implementar un CheckCommandExists("uv"). Si uv no está presente, el script bash generado debe instalarlo usando el instalador oficial recomendado (curl -LsSf [https://astral.sh/uv/install.sh](https://astral.sh/uv/install.sh) | sh) en lugar de usar apt/dnf/pacman para dependencias de Python.  

        Qué NO hacer: No tocar bajo ninguna circunstancia la lógica de Windows o Android. No eliminar la instalación de aria2c, vulkan-tools o espeak-ng.  

Fase 2: Aprovisionamiento del Entorno (Caja de Arena)

Objetivo: Crear el entorno aislado, descargar la versión específica de Python e instalar las dependencias de Kokoro usando la velocidad de uv.

    Instrucciones para la IA asignada (Script PackageManager.cs):

        Qué hacer: Reescribir el bloque de Linux dentro de EnsurePythonEnvironmentAsync.  

            Definir el comando para descargar Python: uv python install 3.13.

            Crear el entorno virtual en la ruta definida (envPath) usando: uv venv --python 3.13 <ruta>.

            Instalar las dependencias exactas ejecutando: uv pip install websockets soundfile numpy kokoro-onnx onnxruntime-vulkan apuntando a ese entorno.

        Qué NO hacer: No modificar la lógica del archivo .zip embebido para Windows ni el script get-pip.py. No usar un archivo pyproject.toml todavía para mantener la simplicidad del código actual; usar uv pip install directamente sobre el entorno generado.  

Fase 3: Ejecución Hermética del Motor TTS

Objetivo: Levantar el servidor puente de Python utilizando el comando uv run, eliminando la necesidad de buscar el ejecutable exacto dentro de la carpeta bin del entorno.

    Instrucciones para la IA asignada (Script BackendLauncher.cs):

        Qué hacer: Modificar la sección donde se configura el puente de Python (sherpaInfo) dentro de ManageBackendLifecycle. En Linux, en lugar de resolver la ruta hacia python3 (pythonExe), configurar el ProcessStartInfo para que el FileName sea simplemente uv. Los Arguments deben ser: run --python 3.13 "{ttsScriptPath}" --port {SherpaPort} --models-dir "{modelsDir}".  

        Qué NO hacer: No alterar la inyección de variables de entorno (LD_LIBRARY_PATH, GGML_VK_VISIBLE_DEVICES). No tocar los subprocesos de _whisperProcess ni _llamaProcess.

        
🗺️ Plan Maestro 3: Soberanía+
Fase 1: El Ojo Omnisciente (Búsqueda Web Gratuita y Deep Research)
    
    El Puente (Python): 
        Levantaremos un servidor con FastAPI que actuará como nuestro microservicio.  
    
    La Búsqueda (Paso A): 
        Usaremos la librería duckduckgo_search para extraer títulos, fragmentos (snippets) y enlaces orgánicos. Esto consume mínima RAM y evita bloqueos al operar con un ritmo humano.  
    
    Deep Research (Paso B): 
        Integraremos librerías como Trafilatura o Crawl4AI. Cuando se requiera, el script entrará al enlace más relevante y extraerá el texto completo del artículo, dando la sensación de una investigación profunda real.  
    
    Inyección de Contexto: 
        Python empaquetará y limpiará los resultados en un formato estricto de Markdown (ej. # Fuente: ... Contenido: ...) antes de mandarlo a Godot, para que la IA no se pierda leyendo la información.  

Fase 2: Expansión Multiversal (APIs de Terceros)
    
    Modificaremos el núcleo de C# para almacenar de forma persistente las llaves de API (Gemini, Claude, GPT).
    
    Dotaremos al NetworkManager.cs de inteligencia para enrutar los prompts. Si el usuario elige "Modo Nube", la petición se va al exterior; si elige "Local", usamos nuestro Llama/Whisper de siempre.

Fase 3: Manos a la Obra (Protocolo MCP)

    Implementaremos la arquitectura del Model Context Protocol (MCP) en nuestros componentes.
    
    Esto permitirá conectar "Tools" dinámicas, convirtiendo a tu AGI en un agente capaz de ejecutar acciones en la computadora, listar directorios o ejecutar comandos, no solo charlar.

Fase 4: AGI como Centro de Mando (Modo Servidor / Reverse API)

    Transformaremos la instancia de Godot en un servidor anfitrión local.
    
    Abriremos un HttpListener para que otras aplicaciones (o tú mismo desde otros scripts) puedan mandar un POST a tu aplicación y recibir la respuesta procesada por tu AGI (Y tambien generar una API para que se pueda conectar servidores AGI con AGI IU para celular u otros dispositivos que no cuenten con mucho internet o quieran mejores modelos).
