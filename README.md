# Mini Deadlock VR

Shooter de progresión en realidad virtual para Meta Quest, basado en *Deadlock* (Valve). Duelo online 1 contra 1 ("el ritual"): elegir héroe, farmear minions, recoger almas, comprar, destruir torres y derrotar al patrón del rival.

Proyecto final de **CS2H01 – Interacción Humano-Computador** (UTEC, ciclo 2026-2), profesor Teófilo Chambilla.

**Equipo:** Rolando David Diaz Jara · Jordinn Martin Reyes Medina · Segundo Fernando Aguilar Delgado

- Unity **2022.3.62f3** (URP), Meta XR SDK + Oculus XR, glTFast
- Meta Quest con controladores (sin hand tracking), probado por Quest Link
- Escenas: `TitleScreen` → `HeroSelect` → `Lane`

## Avance 1 (semana 10, 15 %)

Lo que ya está implementado:

- **Pantalla de título (RF-01).** Botones "PARTIDA ONLINE" y "MI MISIÓN". El juego guarda con PlayerPrefs si el jugador pidió o completó la misión (base de RF-04).
- **Selección de héroe (RF-02).** Seven y Vindicta, cada uno definido en un `HeroDefinitionSO` (`Assets/Data/Heroes`). Al elegir una carta suena la línea del héroe y su modelo 3D aparece girando sobre un pedestal. Al confirmar se reproduce la intro con las frases del patrón (Hidden King / Archmother), con subtítulos (RNF-08), y luego la línea de entrada al ritual.
- **Interfaz espacial (RNF-04).** Menús en el mundo a ~2 m del jugador, manejados con un puntero láser desde el controlador. Las cartas y el pedestal están separados para que el modelo no tape la selección.
- **Carril de combate (RF-15).** Carril tipo *greybox* con coberturas para cubrirse, camino de minions por waypoints (`LanePath`) y un `WaveSpawner` por bando.
- **Torre Guardian (RF-12).** Modelo de 6,5 m que representa las torres que se destruyen en secuencia.
- **Minions y almas (base de RF-07).** Seis modelos (Amber y Sapphire, 1,86 m) que avanzan por el carril; las almas ya tienen su pickup.
- **Arma por héroe.** Cada héroe lleva su propia arma en la mano (55 cm, empuñadura en el origen del controlador).
- **Estadísticas (base de RF-16).** `MatchStatsRecorder` deja lista la estructura para guardar los datos de cada partida.

Recorrido completo título → héroe → carril verificado en Play Mode sin errores. Pendiente: probarlo con el visor (clics con el láser y altura real de la selección de héroe).

## Próximos avances

| Avance | Semana | Contenido |
|---|---|---|
| 2 | 11 | Movimiento libre con viñeta de confort y base de la partida online 1v1 (sala y conexión) |
| 3 | 12 | Partida online completa, disparo, almas y práctica guiada ("Mi misión") |
| 4 | 13 | Habilidades, tienda, torres en secuencia y patrón final |
| 5 | 14 | Pantalla final con resumen y voto, estadísticas en archivo, pruebas con usuarios |

## Cómo abrirlo

1. Abrir la carpeta con Unity 2022.3.62f3.
2. Conectar el Quest por Quest Link.
3. Darle Play: siempre arranca en `TitleScreen`.

Las escenas se arman con los scripts del menú `Deadlock/Setup/1..5`, no a mano.
