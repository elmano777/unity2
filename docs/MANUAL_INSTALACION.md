# Mini Deadlock VR — Manual de instalación y despliegue

**CS2H01 Interacción Humano-Computador · UTEC 2026-2 · Proyecto final**
Rolando David Diaz Jara · Jordinn Martin Reyes Medina · Segundo Fernando Aguilar Delgado
Versión del 21 de noviembre de 2026

---

## Contenido

1. [Qué es y qué contiene el repositorio](#1-qué-es-y-qué-contiene-el-repositorio)
2. [Requisitos](#2-requisitos)
3. [Obtener y abrir el proyecto](#3-obtener-y-abrir-el-proyecto)
4. [Preparar el visor](#4-preparar-el-visor)
5. [Ejecutar en el editor con Quest Link](#5-ejecutar-en-el-editor-con-quest-link)
6. [Compilar e instalar el APK](#6-compilar-e-instalar-el-apk)
7. [Partida online (Unity Relay y Lobby)](#7-partida-online-unity-relay-y-lobby)
8. [Reconstruir las escenas (menú Deadlock/Setup)](#8-reconstruir-las-escenas-menú-deadlocksetup)
9. [Registros para la evaluación (HEART)](#9-registros-para-la-evaluación-heart)
10. [Controles del juego](#10-controles-del-juego)
11. [Solución de problemas](#11-solución-de-problemas)
12. [Recursos de la entrega](#12-recursos-de-la-entrega)

## 1. Qué es y qué contiene el repositorio

**Mini Deadlock VR** es un duelo de realidad virtual 1 contra 1 para Meta Quest, inspirado en *Deadlock* (Valve). Dos jugadores crean o eligen una sala, eligen un héroe (Seven o Vindicta), farmean minions, recogen almas, compran mejoras, destruyen una cadena de objetivos y derrotan al patrón del rival. El juego incluye además una práctica guiada sin conexión ("Mi misión").

- Repositorio: <https://github.com/elmano777/unity2>
- Motor: Unity **2022.3.62f3 LTS** con Universal Render Pipeline.
- Escenas, en este orden en *Build Settings*: `Intro` → `TitleScreen` (el garage) → `HeroSelect` → `Lane` (el mapa).

| Carpeta | Contenido |
|---|---|
| `Assets/Scripts` | Código C# en doce módulos: `Core`, `Combat`, `Economy`, `Experience`, `Lobby`, `Minions`, `Network`, `Neutrals`, `Player`, `Structures`, `UI` y `Visual` |
| `Assets/Editor` | Menú `Deadlock/Setup/1..36`, que reconstruye las escenas, y herramientas de depuración |
| `Assets/Data` | Héroes, habilidades, minions y objetos de la tienda como *ScriptableObjects* (el balance se ajusta sin tocar código) |
| `Assets/Scenes` | Las cuatro escenas del juego |
| `Tools` | Scripts de Blender y archivos `.blend` de esqueletos, animaciones y props |
| `Capturas` | Capturas de cada avance usadas en los informes |
| `docs` | Este manual e imágenes del README |
| `README.md` · `CLAUDE.md` | Presentación del proyecto · notas técnicas detalladas (orden de los pasos de Setup, decisiones y problemas) |

## 2. Requisitos

**Hardware**

- Meta Quest 2, 3 o Pro con sus dos controladores (el juego no usa seguimiento de manos).
- Para desarrollar desde el editor: PC con Windows 10/11, una GPU compatible con Quest Link y un cable USB-C de datos (o Air Link en una red Wi-Fi de 5 GHz).
- Para el modo online: conexión a internet en los dos visores (o en las dos máquinas, si se juega desde el editor).

**Software**

- Unity Hub y **Unity 2022.3.62f3** con el módulo **Android Build Support** (incluye OpenJDK, Android SDK y NDK).
- **Meta Quest Link** (Meta Horizon Link) en el PC, solo para jugar desde el editor.
- **Meta Quest Developer Hub** (MQDH), para instalar el APK, transmitir la vista del visor y copiar archivos.
- Git.
- Una cuenta de desarrollador de Meta, para activar el modo desarrollador del visor.

Al abrir el proyecto, Unity descarga los paquetes de `Packages/manifest.json`: Meta XR SDK 201.0.0, Oculus XR Plugin 4.5.5, Netcode for GameObjects 1.12.0, Relay 1.2.0, Lobby 1.3.0, glTFast 6.16.1 y URP 14.0.12.

## 3. Obtener y abrir el proyecto

1. Clonar el repositorio (unos 275 MB de historial):

   ```bash
   git clone https://github.com/elmano777/unity2.git
   ```

2. En Unity Hub: **Add → Add project from disk** y elegir la carpeta `unity2`.
3. Abrirlo con **Unity 2022.3.62f3**. La primera importación tarda varios minutos.

## 4. Preparar el visor

1. En la app Meta Horizon del celular: **Dispositivos → (tu visor) → Ajustes del visor → Modo desarrollador → activar**.
2. Conectar el visor al PC con el cable y aceptar en el visor **Permitir depuración USB**.
3. Comprobar en MQDH que el visor aparece como conectado.

## 5. Ejecutar en el editor con Quest Link

Es el modo de desarrollo: permite probar cada cambio sin compilar. Con él se grabó el video de demostración.

1. Abrir Meta Quest Link en el PC y activar **Link** desde el menú rápido del visor.
2. En Unity, pulsar **Play**. El juego siempre arranca en `Intro`. Para jugar directamente la escena abierta, desactivar **Deadlock → Play From Title Screen**.
3. Ponerse el visor y jugar con los controles del Meta Quest 2, igual que en el APK ([sección 10](#10-controles-del-juego)).

**Depuración sin visor (opcional).** El juego se juega con los controles del Meta Quest 2. Solo para el desarrollo, sin el visor conectado, el editor acepta estos atajos de teclado, que no se usan en el APK:

| Acción | Tecla |
|---|---|
| Caminar / girar | W A S D / Q E |
| Habilidad A / X | 1 / 2 |
| Abrir el panel de mejoras | U |
| Recargar | R |
| Tirolesa (mirando a lo largo de la cuerda) | Z (mantener) |
| Pantalla final: votar / cambiar de página / jugar otra vez / volver al garage | 1, 2 / ← → / Enter / Escape |
| Atajos de demostración (solo en el editor) | F5 +300 almas · F6 objetivo rival al 10 % · F7 destruirlo · F8 recargar habilidades |

## 6. Compilar e instalar el APK

Es el modo de despliegue: el juego corre en el propio visor, sin PC. Así se jugó la evaluación con usuarios del 17 de noviembre de 2026. El cable solo mantenía cargado el visor, y su vista se transmitía al monitor con MQDH.

1. **File → Build Settings → Android → Switch Platform**.
2. Verificar que estén las cuatro escenas en este orden: `Intro`, `TitleScreen`, `HeroSelect`, `Lane`.
3. **Edit → Project Settings → XR Plug-in Management → pestaña Android**: verificar que **Oculus** esté activado.
4. En **Player Settings** (Android) el proyecto ya trae Minimum API Level 32 y Target API Level 34. El identificador del paquete es `com.UnityTechnologies.com.unity.template.urpblank`; si se cambia, también cambia la carpeta de los registros (sección 9).
5. Pulsar **Build** y guardar el `.apk`.
6. Instalarlo con MQDH (arrastrar el APK a la ventana del visor) o por línea de comandos:

   ```bash
   adb install -r MiniDeadlock.apk
   ```

7. En el visor: **Biblioteca → Orígenes desconocidos → deadlock**.
8. Opcional: en MQDH, **Cast** transmite la vista del visor al PC, para grabar u observar una sesión.

> **Importante para jugar online:** los dos visores deben tener **el mismo APK, compilado desde el mismo commit**. Las salas llevan una huella de la versión; si no coincide, el juego avisa "Esa sala es de otra versión del juego: ¿tienen el mismo APK?".

## 7. Partida online (Unity Relay y Lobby)

El modo online usa Netcode for GameObjects con los servicios **Relay** y **Lobby** de Unity Gaming Services. El proyecto ya está vinculado a un proyecto de Unity Cloud (su Project ID está en `ProjectSettings`), así que funciona sin configurar nada.

Para usar una organización propia:

1. **Edit → Project Settings → Services**: vincular el proyecto a una organización y a un proyecto de Unity Cloud.
2. En el panel de Unity Cloud, activar **Relay** y **Lobby** para ese proyecto.
3. Compilar de nuevo el APK para los dos jugadores.

Cómo se juega online:

1. En el garage, caminar hasta la vidente, golpear la bola de cristal con el gatillo y elegir **Partida online**.
2. Un jugador elige **CREAR SALA** y el otro, **ELEGIR SALA**. La lista muestra hasta cuatro salas y tiene un botón **ACTUALIZAR**.
3. Los dos eligen héroe. Con los dos dentro suena la bienvenida, y el ritual empieza a la vez en ambos visores. Quien crea la sala es el anfitrión: decide el estado del mundo y aplica todo el daño.

**Probar el duelo sin dos visores:** **Deadlock → Debug → Build Windows Test Client** genera un cliente de Windows en `Build/TestClient`. Con *Play From Title Screen* desactivado, dar Play en el editor (que hace de anfitrión) y abrir:

```text
DeadlockClient.exe -selftest -logFile cliente.log
```

`DuelDiagnostics` escribe cada 4 s lo que ve cada lado.

## 8. Reconstruir las escenas (menú Deadlock/Setup)

Las escenas no se arman a mano. El menú **Deadlock → Setup → 1 … 36** las reconstruye paso a paso: capas, prefabs, mapa, red, misión, animaciones, tienda, atmósfera, rendimiento, etc. Así cada cambio de escena queda en código, que se puede revisar y fusionar en Git sin conflictos.

El proyecto ya trae las escenas armadas: solo hay que volver a correr un paso si se modifica lo que ese paso genera. El orden, y qué pasos hay que repetir después de cuál, está en `CLAUDE.md`. Por ejemplo:

- El **36 (rendimiento)** se repite después de los pasos 26, 28, 30, 33 o 35.
- El **27 (objetivos, habilidades y tienda)** se repite después de los pasos 2, 6, 18 o 26.

## 9. Registros para la evaluación (HEART)

Cada partida, incluso una abandonada, escribe dos archivos de texto con una línea JSON por registro:

- `matches.jsonl`: una línea por partida. Guarda el héroe, desde qué botón empezó, el resultado, las torres destruidas y perdidas, si cayó el patrón y en cuánto tiempo, la duración, las almas, los minions, las habilidades, los objetos, los disparos, los segundos en cobertura, las bajas, las muertes, el nivel, las mejoras, el voto y si pulsó "Jugar otra vez".
- `events.jsonl`: eventos sueltos (`match_start`, `match_end`, `vote`, `play_again`, `item_purchased`, `level_up`, `ability_upgrade`).

Dónde quedan:

- **Editor (Windows):**

  ```text
  %USERPROFILE%\AppData\LocalLow\DefaultCompany\deadlock\
  ```

- **APK en el Meta Quest** (carpeta del paquete en el visor):

  ```text
  /sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/
  ```

Para copiarlos desde el visor, con el visor conectado por cable:

```bash
P=/sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files
adb pull $P/matches.jsonl
adb pull $P/events.jsonl
```

Los archivos se acumulan entre partidas: conviene copiarlos al terminar cada sesión de prueba.

## 10. Controles del juego

| Acción | Control |
|---|---|
| Moverse | Stick izquierdo (hacia donde se mira) |
| Girar | Stick derecho (pasos de 45°) |
| Disparar, recoger almas y comprar | Gatillo derecho (para comprar: dos disparos al objeto) |
| Menús del garage | Láser desde el control y cualquiera de los dos gatillos |
| Habilidades | A (control derecho) y X (control izquierdo) |
| Panel de mejoras de habilidad | Clic del stick izquierdo (un "!" dorado avisa que hay puntos) |
| Recargar | Clic del stick derecho (también recarga sola al vaciar el cargador) |
| Tirolesa | Grip izquierdo cerca de la cuerda; soltar para bajar |
| Volver al garage | Mantener B o Y durante 1,2 s (cierra la sala online) |
| Pantalla final | Rayo desde la mano derecha y gatillo; stick a los lados para cambiar de página; A/X jugar otra vez, B/Y volver al garage |

## 11. Solución de problemas

| Problema | Solución |
|---|---|
| "¿Tienen el mismo APK?" al entrar a una sala | Compilar e instalar el mismo APK (mismo commit) en los dos visores. |
| "No hay salas abiertas" | Que un jugador elija CREAR SALA y el otro pulse ACTUALIZAR. Verificar la conexión a internet y que Relay y Lobby estén activos. |
| El visor no aparece en Unity o en Link | Reiniciar Meta Quest Link, reconectar el cable y aceptar la depuración USB en el visor. |
| La imagen se arrastra con la cabeza o hay pocos FPS | Comprobar que no haya cámaras `CaptureCam` activas en la escena `Lane` y volver a correr el paso 36. |
| Mareo al moverse | La viñeta de confort está activa por defecto; no desactivarla en jugadores nuevos. |
| La tirolesa no avanza | La cuerda solo llega hasta donde tu bando controla el carril, y el juego muestra el motivo. Avanzar con los minions. |
| El visor se descarga durante una sesión larga | Mantenerlo conectado al cargador o al PC por cable: el APK sigue corriendo en el visor. |

## 12. Recursos de la entrega

- Código fuente: <https://github.com/elmano777/unity2>
- Video de demostración: <https://drive.google.com/drive/folders/16UVDVmIcmz5RZQKy1x8Up_Fg00LaUKAt?usp=sharing>
- Videos de las pruebas con usuarios: <https://drive.google.com/drive/folders/1zg0Zm3IIX3Rw4XsEQZRlUHqb7naBvKhA?usp=sharing>
- Este manual en PDF: <https://drive.google.com/drive/folders/1jH5tQB7KM3BYZJqmgYAxHuJw7nP5mjb6?usp=sharing>
- Informe final (LaTeX/PDF): entregado por Canvas.
- Notas técnicas completas: `CLAUDE.md` en el repositorio.

---

*Los modelos, voces y música de los héroes, minions y objetivos provienen de* Deadlock *(Valve Corporation) y se usan solo con fines académicos. Las texturas del mapa son CC0 (Poly Haven).*
