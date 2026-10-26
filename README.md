# Mini Deadlock VR

Shooter de progresión en realidad virtual para Meta Quest, basado en *Deadlock* (Valve). Duelo online 1 contra 1 ("el ritual"): elegir héroe, farmear minions, recoger almas, comprar, destruir torres y derrotar al patrón del rival.

Proyecto final de **CS2H01 – Interacción Humano-Computador** (UTEC, ciclo 2026-2), profesor Teófilo Chambilla.

**Equipo:** Rolando David Diaz Jara · Jordinn Martin Reyes Medina · Segundo Fernando Aguilar Delgado

- Unity **2022.3.62f3** (URP), Meta XR SDK + Oculus XR, glTFast, Netcode for GameObjects + Relay + Lobby
- Meta Quest con controladores (sin hand tracking), probado por Quest Link
- Escenas: `Intro` → `TitleScreen` (el garage) → `HeroSelect` → `Lane`

## Avance 1 (semana 10, 15 %)

- **Pantalla de título (RF-01).** "PARTIDA ONLINE" y "MI MISIÓN". El juego guarda con PlayerPrefs si el jugador pidió o completó la misión (base de RF-04).
- **Selección de héroe (RF-02).** Seven y Vindicta, cada uno definido en un `HeroDefinitionSO` (`Assets/Data/Heroes`). Al elegir suena la línea del héroe y aparece su modelo 3D. Al confirmar se reproduce la intro con las frases del patrón (Hidden King / Archmother), con subtítulos (RNF-08), y luego la línea de entrada al ritual.
- **Interfaz espacial (RNF-04).** Menús en el mundo, manejados con un puntero láser desde el controlador.
- **Carril de combate (RF-15).** Calle bajo el puente con coberturas, camino de minions por waypoints (`LanePath`) y un `WaveSpawner` por bando.
- **Torre Guardian (RF-12)**, **minions y almas (base de RF-07)**, **arma por héroe** y **estadísticas** (`MatchStatsRecorder`, base de RF-16).

## Avance 2 (semana 11)

- **Base online 1v1 (RF-08).** `DuelMatchmaker` entra a una sala abierta o crea una de 2 jugadores (Lobby + Relay + NGO). El anfitrión juega del lado Amber y el que se une del lado Sapphire. `NetworkPlayer` sincroniza cabeza, manos, héroe y arma; el rival se ve como su héroe con manos enguantadas.
  - Solo se juntan jugadores con **el mismo build** (las salas llevan una huella de la configuración de red).
  - Si dos jugadores buscan a la vez y cada uno crea su sala, el de la sala más nueva se pasa a la más antigua.
  - Las salas abandonadas (anfitrión que cerró la app) se descartan a los 12 s y se busca otra.
  - Mientras se espera se muestra el código de sala.
- **Movimiento con confort (RNF).** `ComfortLocomotion` (stick izquierdo camina, derecho gira 45°) con viñeta de confort activada por defecto.

## Además (hasta el 24/10)

- **Intro como la de Deadlock.** Azotea sobre la ciudad al atardecer, el logo de la rueda aparece brillando con DEADLOCK debajo, música de título y "pulsa cualquier botón para empezar".
- **El garage** (pantalla de título 3D). Sala de la vidente con sonido ambiente y luz verdosa; se camina hasta la bola y se golpea para que aparezcan "Partida online", "Mi misión", y debajo volumen y subtítulos (RNF-08).
- **Selección de héroe 3D** ("visión espiritual"): los héroes aparecen como espíritus y se confirma con una runa.
- **Botón atrás.** Runa ATRÁS o B/Y en la selección de héroe; en el carril, mantener B/Y para volver al garage (cierra la sala online).
- **Textos brillantes** en todos los menús y clic del láser con cualquiera de los dos gatillos.
- **Arma y combate.** Fogonazo, bala visible, trazador, impacto y sonidos; barra de vida sobre cada minion y desvanecido al morir.
- **Manos del jugador.** Manos con guante en los controles; los dedos siguen al gatillo y al grip, y la derecha sostiene el arma.

Verificado en Play Mode sin visor. Pendiente: probar todo con el Quest y una partida real entre dos visores.

## Avance 3 (semana 12)

- **El anfitrión manda en el mundo (RF-08, RF-09).** Los minions, las torres y las almas los crea y los mueve el anfitrión; el que se une los ve como copias (`NetworkHealth`, `NetworkTransform`, `NetworkSoul`, `NetworkTower`). Nada aparece hasta que el rival entra a la sala (`MatchNet`).
- **Disparo y daño.** Los disparos del que se une viajan al anfitrión, que aplica el daño y lo publica: los dos ven las mismas barras de vida y las mismas muertes. Cada jugador tiene un cuerpo (cápsula bajo la cabeza, capa `Rival` para el otro) que los minions y las torres atacan y que el rival puede disparar; al caer reaparece a los 5 s en su lado. El rival ve el trazador de cada disparo.
- **Almas.** El anfitrión crea las almas; quien dispare primero se queda con ellas y el anfitrión le paga a su cartera.
- **Una torre por bando.** `StructureSpawner` levanta la torre Guardian de cada lado; cada jugador ataca la del otro.
- **Mi misión (RF-04, RF-13).** Práctica sin conexión en 6 pasos con panel flotante de pistas (`HintBoard`, `MissionGuide`): caminar, girar, disparar, eliminar un minion, recoger un alma y destruir la torre. Al terminar se marca como completada. En la práctica no se puede morir y solo juega el bando rival.
- **Arreglo de conexión.** La etiqueta de sala se calculaba con un hash de configuración que Netcode guardaba antes de registrar los prefabs; el anfitrión rechazaba al que se unía con "NetworkConfig mismatch".

Verificado con el editor de anfitrión y un cliente de Windows: ambos ven los mismos minions, torres y almas, y los golpes del cliente (a minions, torre, almas y al anfitrión) llegan al anfitrión. Pendiente: probarlo con dos Quest.

Pasos de editor nuevos: `Deadlock/Setup/18` (red de minions, almas, torre y cuerpo) y `19` (Mi misión). Para probar el duelo sin visores: `Deadlock/Debug/Build Windows Test Client` y abrir el cliente con `-selftest` mientras el editor hace de anfitrión; `DuelDiagnostics` escribe en el log lo que ve cada lado.

## Próximos avances

| Avance | Semana | Contenido |
|---|---|---|
| 4 | 13 | Habilidades, tienda, torres en secuencia y patrón final |
| 5 | 14 | Pantalla final con resumen y voto, estadísticas en archivo, pruebas con usuarios |

## Cómo abrirlo

1. Abrir la carpeta con Unity 2022.3.62f3.
2. Conectar el Quest por Quest Link.
3. Darle Play: siempre arranca en `Intro` (se desactiva con `Deadlock/Play From Title Screen` para jugar la escena abierta).

Las escenas se arman con los scripts del menú `Deadlock/Setup/1..19`, no a mano.

**Para jugar online entre dos visores:** los dos deben instalar **el mismo APK**, compilado desde el mismo commit. Un APK de otra versión no puede entrar a la sala.
