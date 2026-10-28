# Better Audio

Selector de salidas de audio para Windows 11. WPF, C# y .NET 10 LTS; interfaz en español. Versión 1.2: HUD con mezclador integrado, control de Windows Sonic y transiciones continuas.

## Probarlo

Abrí **Better Audio.lnk** en esta carpeta, o **artifacts/BetterAudio/BetterAudio.exe**. La publicación incluye el runtime: no hace falta instalar .NET ni ejecutar como administrador. Conservá todos los archivos de la carpeta junto al ejecutable. También se genera un ZIP portable con `build.ps1 -Package`.

La primera apertura muestra el HUD y agrega un icono verde de tres barras a la bandeja, junto al reloj. Si Windows lo coloca en el menú de iconos ocultos (`^`), arrastralo a la zona visible de la bandeja. Windows decide qué iconos muestra; la app no modifica esa preferencia.

El HUD se centra sobre el rectángulo real del icono de bandeja, obtenido con `Shell_NotifyIconGetRect`. Si está junto al borde de un monitor, ajusta su posición para permanecer en pantalla y conserva el icono como origen de la animación. El fondo es translúcido con sombra y halo difuminados; no usa Acrylic del sistema. La navegación anima la posición de todos los iconos en un arco con escala, opacidad y rotación Z; conserva los iconos existentes.

| Acción | Control |
| --- | --- |
| Vista previa sin tomar el foco | Dejar el puntero sobre el icono 350 ms |
| Abrir y habilitar teclado | Clic izquierdo en el icono |
| Recorrer y activar salidas | ← / →, botones laterales, rueda sobre el arco o desplazamiento horizontal |
| Explorar sin cambiar el audio | Desmarcar **Activar al navegar** |
| Confirmar en modo manual | Clic en el icono del dispositivo o Enter |
| Volumen de la salida que estás viendo | ↑ / ↓, slider o rueda sobre el volumen |
| Ajuste fino / rápido | Pasos de 2%; Shift para pasos de 10% |
| Silenciar / reactivar esa salida | Botón de altavoz o M |
| Abrir el mezclador dentro del HUD | Botón **Mezclador** |
| Abrir el mezclador de Windows | Engranaje junto a **Mezclador** |
| Ajustar una aplicación | Su slider, rueda sobre su fila, o ↑ / ↓ al enfocar su control |
| Silenciar una aplicación | Su botón de altavoz, o M al enfocar su control |
| Regresar a dispositivos desde el mezclador | Flecha de regreso o Esc |
| Activar Windows Sonic / desactivar sonido espacial | Checkbox **Sonido espacial** |
| Elegir otros proveedores espaciales en Windows | Engranaje junto al checkbox |
| Cerrar el panel | Esc o clic fuera del panel anclado |
| Salir de la aplicación | Clic derecho en el icono → Salir |

**Activar al navegar** viene habilitado y se guarda en `%LOCALAPPDATA%\BetterAudio\settings.json`. Cada movimiento solicita activar la salida elegida. Si recorrés varias mientras Windows procesa un cambio, las solicitudes pendientes se reemplazan por la última elección; las llamadas COM nunca se solapan. Desmarcar el checkbox cancela solicitudes pendientes, aunque una llamada ya iniciada puede terminar. Volver a marcarlo habilita el cambio automático para los próximos movimientos.

El icono central y su nombre comercial indican el dispositivo seleccionado; los adyacentes descienden por el arco al 65% de tamaño y 40% de opacidad. El punto de luz inferior identifica la salida realmente activa. El nombre original completo está disponible en el tooltip. En modo manual podés explorar y ajustar el volumen propio de cada dispositivo antes de confirmar. En modo automático, los cambios externos de salida también actualizan el centro del arco. La activación establece la salida para multimedia, consola y comunicaciones. Las aplicaciones que tengan una salida fijada en su propio mezclador pueden mantener esa asignación.

El modo hover se oculta al alejar el puntero. La rueda funciona al colocar el cursor sobre la zona correspondiente, sujeto a la opción de Windows de desplazar ventanas inactivas. Un clic dentro del panel lo vuelve interactivo. La ventana se reutiliza al ocultarla y volverla a abrir.

**Mezclador** reemplaza el contenido del HUD por las sesiones de audio de la salida seleccionada. Incluye volumen principal, volumen/silencio por sesión y actualización por eventos al abrir/cerrar aplicaciones o ajustar niveles desde otro programa. Algunas aplicaciones, como navegadores o WhatsApp, pueden crear varias sesiones independientes. Solo aparecen las sesiones de esa salida; no cambia asignaciones de salida por aplicación. El engranaje abre el mezclador completo de Windows.

**Sonido espacial** activa **Windows Sonic** al marcarlo y desactiva el procesamiento espacial al desmarcarlo. El estado se consulta con la API nativa `ISpatialAudioClient`; después de cambiarlo se vuelve a consultar para confirmar el resultado. Si Windows no informa el estado o falta el componente requerido, el checkbox se deshabilita y su tooltip explica el motivo. El engranaje abre las propiedades de la salida para configurar Dolby/DTS u otros proveedores y sus licencias. Al volver a marcar el checkbox se selecciona Sonic, incluso si antes se había utilizado otro proveedor. Algunas salidas no admiten Sonic y Windows puede rechazar su activación; el HUD informa el fallo y conserva el estado realmente observado.

## Decisiones técnicas

- .NET 10 LTS en lugar de .NET 8: soporte hasta noviembre de 2028; .NET 8 finaliza su soporte el 10 de noviembre de 2026. [Política oficial](https://dotnet.microsoft.com/en-us/platform/support/policy).
- WPF para una ventana pequeña, sin WebView ni servicios web; HUD oscuro translúcido de 408 × 378 DIPs (482 DIPs de alto en el mezclador), con borde verde tenue, burbujas circulares y halo central. Los errores agregan 28 DIPs de altura. Las transiciones del arco, hover lateral, volumen y apertura respetan la preferencia de animación de Windows; los controles tienen nombres accesibles.
- Icono Win32 identificado por GUID, con `Shell_NotifyIconW` y [posición real vía `Shell_NotifyIconGetRect`](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect). No depende de campos privados de `NotifyIcon`. El protocolo v4 reemplaza el tooltip nativo por el HUD y permite recuperar el icono al reiniciar Explorer. El menú contextual usa Windows Forms.
- `NAudio.Wasapi` **3.3.0**, en lugar del metapaquete NAudio. Su API actual incluye `CreateNotificationClient()` para escuchar conexiones, desconexiones, renombrados y cambios de salida. También escuchamos eventos de volumen/silencio. [NAudio](https://github.com/naudio/NAudio).
- Objetos COM confinados a un hilo de audio; la UI recibe snapshots sin objetos COM. Las actualizaciones se agrupan durante 100 ms y las escrituras de volumen durante 45 ms, conservando dispositivo e identificador de instancia de sesión aunque navegues a otro. El mezclador conserva controles durante actualizaciones, usa orden estable y libera sus notificaciones al cerrarse.
- Cambio de salida con `IPolicyConfig`, aislado en `ComPolicyConfig.cs`. Es una interfaz **no documentada de Windows** y puede requerir ajustes con futuras versiones. Los fallos se muestran en pantalla y se registran localmente.
- Lectura espacial con [`ISpatialAudioClient::GetMaxDynamicObjectCount`](https://learn.microsoft.com/en-us/windows/win32/api/spatialaudioclient/nf-spatialaudioclient-ispatialaudioclient-getmaxdynamicobjectcount); cambios mediante **SoundVolumeCommandLine 1.28** de NirSoft, ejecutado sin ventana y con timeout. Se verifica el estado nativo después de cada escritura. El componente se obtuvo del sitio oficial, se conserva sin cambios con sus archivos originales y su SHA-256 se verifica al compilar. El engranaje usa la [URI oficial de propiedades por endpoint](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings). La app no escribe directamente el Registro.
- La apertura prepara el estado de audio mientras el HUD está oculto y configura tamaño y opacidad del contenido antes del primer frame. La ventana transparente mantiene opacidad 1; solamente HudSurface hace un fundido de 170 ms, sin escala ni desplazamiento. Los clics que llegan durante una apertura por hover promueven esa misma apertura. Cancelarla invalida las continuaciones pendientes; no hay un segundo reinicio de la animación después de leer audio.
- Mutex de sesión e IPC local: abrir un segundo ejecutable muestra la instancia existente.
- Posicionamiento según el monitor del icono, área de trabajo y DPI; cursor como respaldo si Windows todavía no informa un rectángulo visible. Sin hooks globales de teclado/ratón ni sondeo continuo de audio.
- Publicación `win-x64` independiente con ReadyToRun, sin trimming de WPF ni extracción de un bundle en cada arranque.

## Desarrollo y compilación

Requiere Windows y **SDK de .NET 10**. El SDK usado en esta máquina está instalado por usuario en `%LOCALAPPDATA%\BetterAudio\sdk`; el script también admite una instalación normal de `dotnet`.

```powershell
# Compilar y publicar
.\build.ps1

# Pruebas de comportamiento, integración de lectura real y ZIP portable
.\build.ps1 -Test -Live -Package
```

El script detiene la publicación si Better Audio está abierto: cerralo desde su menú de bandeja. No termina procesos automáticamente. `packages.lock.json` fija las dependencias NuGet. El workflow de GitHub Actions ejecuta la misma compilación y las pruebas de comportamiento en Windows, y adjunta una publicación portable.

```text
AudioFlyoutApp/
  Core/Audio/       Core Audio, hilo COM e IPolicyConfig
  Core/Models/      Snapshots y estado de tarjetas
  UI/Flyout/       Ventana, carrusel y entradas de teclado/rueda
  UI/ViewModels/   Estado, navegación y escrituras agrupadas
  Services/        Bandeja, posicionamiento Win32 y log
AudioFlyoutApp.Tests/
  Program.cs       Runner de regresión sin framework adicional
```

Las pruebas cubren selección, agrupación de escrituras, desconexiones simuladas, recuperación, preferencias, geometría y primera carga. Incluyen mezclador por sesión/endpoint, cambios externos, estados espaciales desconocidos/fallidos, apertura pendiente/cancelada y valores intermedios del hover. `--live` enumera las salidas/sesiones reales, consulta el estado espacial, escribe los mismos niveles de volumen/silencio de una sesión sin cambiarlos y verifica brevemente un icono de prueba. `--spatial-roundtrip` es una prueba explícita que activa Sonic y restaura el estado desactivado; omite la operación si ya existe un proveedor activo. `--ui` usa dispositivos reales; `--ui-demo` y `--ui-mixer-demo` usan datos simulados. `--render` exporta las dos vistas a PNG en artifacts/ sin mostrar ventanas.

## Diagnóstico y alcance

Logs locales rotativos en `%LOCALAPPDATA%\BetterAudio\app.log`. No hay telemetría propia, cuentas, servicios remotos ni inicio automático añadido por la app.

Esta entrega portable está lista para probar localmente. No está firmada con un certificado de distribución ni incluye instalador/actualizador. La verificación automática simula desconexiones; la reconexión física Bluetooth/USB y el posicionamiento con combinaciones de monitores y DPI requieren pruebas en esos equipos. Las salidas de pantallas usan el nombre proporcionado por su controlador; la clasificación visual de iconos es orientativa. No incluye control de micrófonos ni cambio de salida por aplicación.

**Distribución del componente espacial:** SoundVolumeCommandLine es freeware de Nir Sofer; su licencia permite redistribuir todos sus archivos originales sin modificaciones, pero prohíbe venderlo o incluirlo en un producto comercial. Este paquete personal/gratuito conserva `ThirdParty/SoundVolumeCommandLine/readme.txt` y el resto de los archivos originales. Para una futura distribución comercial hay que sustituir ese backend u obtener otra autorización del titular. [Licencia y documentación del componente](https://www.nirsoft.net/utils/sound_volume_command_line.html).

## Ajustes 1.2.2

Encabezado verde compartido Better Audio UX con fijar/desfijar y cierre explícito. Fijar mantiene el panel abierto al usar otras aplicaciones; desfijar restablece el cierre al perder foco. La lista del mezclador usa la rueda exclusivamente para desplazarse, tiene una zona de scrollbar más amplia e iconos genéricos cuando falta el icono de una aplicación. Icono de aplicación y silencio comparten el mismo eje vertical. El volumen se ajusta arrastrando el slider o con teclado al enfocarlo. Las dimensiones son 408 × 410 DIPs en la vista principal y 408 × 482 en el mezclador.

La altura del HUD se interpola durante 300 ms al cambiar de vista. SizeChanged solamente reposiciona junto a la bandeja, sin imponer el tamaño final en cada frame. Las interrupciones parten de la altura visible actual; el contenido entra con fundido de 260 ms y desplazamiento de 300 ms. Las pruebas incluyen tamaños intermedios, inversión durante expansión y estados finales de ambas vistas.
