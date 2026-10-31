# Esqueletos y animaciones de los minions

Los seis minions (Amber y Sapphire: Trooper, Melee y Medic) llevan un esqueleto de 13 a 16 huesos con piel rígida: son un barril con cabeza de vela u obelisco y miembros de piezas sueltas, así que cada isla de la malla va entera al hueso más cercano. Los Sapphire tienen la misma pose y proporciones que los Amber, por eso reusan los mismos huesos.

- `lib.py`, `anim_lib.py` y `clips.py`: funciones de Blender (`build_rig`, `reset_model`, `debug_render`; la clase `Kit` con la caminata de piernas; los clips de cada rol, `make_trooper` / `make_melee` / `make_medic`). Se cargan como bloques de texto `minion_lib`, `minion_anim_lib` y `minion_clips`.
- `build_amber.py` y `build_sapphire.py`: los huesos y las reglas del arma/torso de cada bando (los clips de Amber están en el propio `build_amber.py`; los de Sapphire usan `clips.py`, que es la misma lógica con un prefijo `S` en los nombres). Los huesos se midieron sobre los centroides de las islas y renders ortográficos. Se corre **dentro de Blender** (el de la Store no arranca desde la terminal) con los modelos estáticos importados (los `.glb` de antes del rig están en el historial de git).
- No se commitea el `.blend` de los seis (pesa ~30 MB con las texturas); se regenera corriendo los scripts sobre los modelos estáticos.
- Cada minion tiene `Idle`, `Walk` y `Attack`; el Medic además `Heal`. El arma (rifle, maza, bastón del estandarte) es un solo hueso `Weapon` con el brazo que la sostiene; el Medic lanza el orbe con la mano izquierda (`LeftHand`, sostiene la corneta).
- Exportar como la torre: solo el armature para los clips y armature + mallas para el modelo, sin animaciones; pasar `Tools/vindicta_rig/tidy_glb.py` a los dos con los prefijos de las demás acciones a omitir.

## En Unity

`Deadlock/Setup/22 Animated Minions` cambia el modelo de los prefabs, apaga el vaivén falso de `MinionAgent`, agrega `MinionAnimator` con los clips y `NetworkMinionActions` (muestra en la copia del que se une lo que hizo el anfitrión). Re-correrlo después de los pasos 1 o 18. Para otro minion, agregar una fila a la tabla del script.

## El Medic

El Medic pelea como el Melee (golpea con el estandarte; vida 90, daño 10, alcance 2,2 m) y además lanza un orbe de curación a los aliados heridos cada 2 s sin dejar de avanzar. Al morir suelta, aparte de las almas, un último orbe que vuela hasta el jugador que lo mató y le devuelve `healAmount` (25) de vida al llegar (`MinionAgent.DeathOrb`, `HealOrb`). En línea el anfitrión decide a quién cura y le avisa a la copia (`NetworkMinionActions`).

- `MinionAgent` anuncia lo que hace con `Acted` (ataque o curación). `MinionAnimator` mezcla idle y caminar según la velocidad real, y encima reproduce el gesto.
- Curación: el medic lanza un orbe verde que vuela 0,4 s hasta el aliado y la vida sube cuando llega (antes era instantánea).
- Los minions que mueren siguen desvaneciéndose (`Minion_DeathFade`, ahora también para mallas con piel).
