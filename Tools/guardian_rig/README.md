# Esqueleto y animaciones del Guardian

`Assets/Models/Structures/Guardian.glb` lleva un esqueleto de 17 huesos (`Hips`, `Chest`, `Head`, brazos y piernas, `Staff` y `StaffTip`). `Guardian_Anims.glb` trae solo el armature con tres clips. El Guardian mira a −Y en Blender.

- `joints.json`: cabeza, cola y padre de cada hueso. `Staff` nace en el puño izquierdo (el que sostiene el bastón, lado +X) y `StaffTip` está en la punta, de donde sale el rayo.
- `build.py`: importa el modelo estático, crea `Guardian_Rig`, pesa la malla y genera las acciones `Guardian_Idle` (4 s), `Guardian_Attack` (0,6 s, el bastón retrocede) y `Guardian_Die` (2,5 s, las rodillas se pliegan). Se corre **dentro de Blender** (el de la Store no arranca desde la terminal). El modelo estático es el `Guardian.glb` de antes del rig (historial de git, p. ej. `git show 9e4fe9d:Assets/Models/Structures/Guardian.glb > Assets/Models/Structures/Guardian_static.glb`); ese archivo no se commitea.
- Pesado rígido: el robot son piezas sueltas, así que cada isla va entera al hueso más cercano; las caras con el material del bastón van todas a `Staff`.
- Exportar: seleccionar solo `Guardian_Rig` (clips) y luego `Guardian_Rig` + la malla (modelo, sin animaciones), exportar GLB, y pasar `Tools/vindicta_rig/tidy_glb.py` a los dos (el exportador de Blender 5 vuelca todas las escenas y todas las acciones del archivo). Deseleccionar los objetos de las demás escenas antes de exportar.

## En Unity

`Deadlock/Setup/21 Animated Tower` cambia el modelo de `Guardian_Tower.prefab`, crea el rayo (LineRenderer en `StaffTip`), lo conecta a `Tower` (`beamOrigin`, `beam`) y agrega `TowerAnimator` con los tres clips. Re-correrlo después de los pasos 1 o 18, que reconstruyen el prefab.

- `Tower` apunta antes de disparar (`windUp`, 0,35 s) y expone `HasAimPoint` / `AimPoint`; el anfitrión manda el punto de mira a la copia por `NetworkTower`.
- `TowerAnimator` mezcla idle, ataque y caída con un PlayableGraph y, al final de cada cuadro, inclina el bastón para que la punta apunte al objetivo (máx. 24°) y gira un poco el torso y la cabeza. El nodo con `Hips` se renombra a `Guardian_Rig` porque Unity lo llama como el archivo y los clips buscan `Guardian_Rig/Hips/…`.
