# Mini Deadlock VR — notas para Claude

Proyecto final UTEC CS2H01 (IHC, 2026-2). Duelo VR 1v1 online ("el ritual") con progresión MOBA, Meta Quest con controles (sin hand tracking). Unity **2022.3.62f3**, Meta XR SDK + Oculus XR + glTFast. Se prueba en Play Mode por Quest Link.

## Estado (2026-10-18)

**Entregable 1 (sem. 10)** — menú, selector de héroe, carril con coberturas, Guardian, minions: hecho.
- `TitleScreen` → botones "PARTIDA ONLINE" / "MI MISIÓN" (PlayerPrefs `MissionRequested`, `MissionCompleted`).
- `HeroSelect` → Seven y Vindicta (`Assets/Data/Heroes/*.asset`, `HeroDefinitionSO`): línea al elegir, preview 3D, intro con líneas del informe, línea de entrada.
- `Lane` → coberturas, `LanePath` por waypoints, `WaveSpawner` por bando, torre Guardian, almas, arma por héroe (`HeroWeaponSwapper`).
- `Lane` vestido como la calle bajo el puente de Deadlock (`Deadlock/Setup/6`): piso de piedra, edificios de arenisca + ladrillo, puente, cielo y niebla. Texturas CC0 de Poly Haven en `Assets/Art/Textures/Lane`. Si se re-corre el paso 2, correr el 6 después.
- Recorrido completo verificado en Play sin visor (sin errores). **Falta probar con el Quest**: clics con el puntero láser y encuadre de HeroSelect a altura real.

**Entregable 2 (sem. 11)** — base online + movimiento con viñeta: hecho (`Deadlock/Setup/8`, 2026-10-18).
- Online: NGO 1.12 + Relay + Lobby (como el lab de Building Blocks del profe). Proyecto Unity Cloud "deadlock"; el Project ID vive en `ProjectSettings.asset`. `DuelMatchmaker` (objeto `Network` del Lane) entra a una sala abierta o crea una de 2 jugadores. Es propio porque el bloque Auto Matchmaking de Meta 201 pide `com.unity.services.multiplayer` en vez de Lobby/Relay sueltos. "Mi misión" no se conecta.
- `NetworkPlayer` (prefab en `Assets/Prefabs/Network`): sincroniza cabeza, manos y héroe. El anfitrión juega del lado Amber y el que se une del lado Sapphire (`SpawnPoint_*`). Minions y daño siguen siendo locales a cada máquina (sincronizarlos es E3).
- `ComfortLocomotion` (stick izq. camina, der. giro de 45°; WASD/QE en el editor) + `ComfortVignette` (shader `Deadlock/ComfortVignette`, PlayerPrefs `ComfortVignette`, activada por defecto).
- Verificado: editor (anfitrión) + build de Windows (cliente) conectados por Relay, rival visible en z=23. Para dar Play directo en el Lane, desactivar `Deadlock/Play From Title Screen`. Falta probarlo con el Quest.

**Intro** (`Deadlock/Setup/11`, 2026-10-24): escena `Intro` (primera del build) como la de Deadlock: azotea sobre la ciudad al atardecer, el logo de la rueda aparece brillando, DEADLOCK debajo y "pulsa cualquier botón" → TitleScreen (`IntroScreen`, shader `Deadlock/IntroLogo`). Es una copia del TitleScreen sin el lobby: re-correr el paso 11 si cambia el rig del TitleScreen. Texturas del logo/título con `Tools/make_intro_tex.py`. Play arranca en Intro.

**Garage** (el lobby de la vidente, paso 9): audio ambiente `amb_garage_lp_l`, luz verdosa suave, y volumen/subtítulos aparecen bajo la bola al golpearla (el TitleCanvas vive dentro de `MenuRunes`). La intro suena con `music_title_155bpm`. Paso 12: todos los textos TMP con material brillante (`Assets/Art/Materials/Text`) y clic del láser con ambos gatillos; los pasos 9-11 lo aplican solos.
- Online: las salas se etiquetan con el hash de `NetworkConfig` (solo se juntan builds iguales: ambos jugadores deben tener el mismo APK) y un anfitrión que espera se pasa a una sala más antigua si existe (evita que dos que buscan a la vez queden cada uno en su sala). El código de sala se muestra mientras se espera.

**Atrás** (paso 13, `BackToGarage`): en HeroSelect runa ATRÁS (láser) o B/Y; en el Lane mantener B/Y 1,2 s (online o Mi misión) vuelve al garage, cierra la sala y destruye el NetworkManager. Va en su propia raíz: re-correr 13 después de 8 o 10 no hace falta, pero sí si se reconstruye el Lane (paso 2).

**Pendiente:** la línea de victoria (`winClip`) no se reproduce aún (no hay pantalla final). Próximo: E3 online completo, disparo, almas, Mi misión; E4 habilidades, tienda, torres en secuencia, patrón; E5 pantalla final + voto + stats en `persistentDataPath`.

## Reglas del proyecto
- Sin bots ni IA enemiga: el duelo es contra otra persona.
- Código de gameplay sin NavMesh y sin XRI (Input System directo); solo `HapticFeedback.cs` depende de XR.
- La escena se arma con scripts de editor (menú `Deadlock/Setup/1..7`), no a mano.
- Los `.glb` extraídos de Deadlock vienen con Draco + WebP: convertir con `npx @gltf-transform/cli@3` (`copy` → `png --formats "*"` → `resize`) antes de importarlos. Revisar la escala de cada modelo.
- No commitear el ruido que Unity reescribe solo: `OculusProjectConfig.asset`, `ShaderGraphSettings.asset`, `URPProjectSettings.asset`, `.vsconfig` (ya están en `.gitignore`). `ProjectSettings.asset` SÍ se versiona: guarda el Project ID de Unity Cloud ("deadlock") que usan Relay/Lobby; si Unity solo le cambia ruido, no commitear ese cambio.
- En Play por MCP con Unity sin foco el juego se congela (`runInBackground` = false): poner `Application.runInBackground = true` en runtime.
