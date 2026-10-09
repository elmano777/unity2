# Mini Deadlock VR — notas para Claude

Proyecto final UTEC CS2H01 (IHC, 2026-2). Duelo VR 1v1 online ("el ritual") con progresión MOBA, Meta Quest con controles (sin hand tracking). Unity **2022.3.62f3**, Meta XR SDK + Oculus XR + glTFast. Se prueba en Play Mode por Quest Link.

## Estado (2026-10-09)

**Entregable 1 (sem. 10)** — menú, selector de héroe, carril con coberturas, Guardian, minions: hecho.
- `TitleScreen` → botones "PARTIDA ONLINE" / "MI MISIÓN" (PlayerPrefs `MissionRequested`, `MissionCompleted`).
- `HeroSelect` → Seven y Vindicta (`Assets/Data/Heroes/*.asset`, `HeroDefinitionSO`): línea al elegir, preview 3D, intro con líneas del informe, línea de entrada.
- `Lane` → coberturas, `LanePath` por waypoints, `WaveSpawner` por bando, torre Guardian, almas, arma por héroe (`HeroWeaponSwapper`).
- Recorrido completo verificado en Play sin visor (sin errores). **Falta probar con el Quest**: clics con el puntero láser y encuadre de HeroSelect a altura real.

**Pendiente:** la línea de victoria (`winClip`) no se reproduce aún (no hay pantalla final). Próximo: Entregable 2 (sem. 11) — viñeta de confort / locomoción + base online (decidir NGO+Relay vs Photon Fusion; es el mayor riesgo). Luego E3 online completo, disparo, almas, Mi misión; E4 habilidades, tienda, torres en secuencia, patrón; E5 pantalla final + voto + stats en `persistentDataPath`.

## Reglas del proyecto
- Sin bots ni IA enemiga: el duelo es contra otra persona.
- Código de gameplay sin NavMesh y sin XRI (Input System directo); solo `HapticFeedback.cs` depende de XR.
- La escena se arma con scripts de editor (menú `Deadlock/Setup/1..5`), no a mano.
- Los `.glb` extraídos de Deadlock vienen con Draco + WebP: convertir con `npx @gltf-transform/cli@3` (`copy` → `png --formats "*"` → `resize`) antes de importarlos. Revisar la escala de cada modelo.
- No commitear el ruido que Unity reescribe solo: `OculusProjectConfig.asset`, `ProjectSettings.asset`, `ShaderGraphSettings.asset`, `.vsconfig`.
- En Play por MCP con Unity sin foco el juego se congela (`runInBackground` = false): poner `Application.runInBackground = true` en runtime.
