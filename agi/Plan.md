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
