# Esqueleto de Seven

`Assets/Models/Heroes/Seven.glb` lleva un esqueleto humanoide de 21 huesos (`Hips` … `LeftToes`, nombres tipo Unity Humanoid) hecho con Blender 5.3 en modo headless. La pose de reposo es la pose original del modelo (no una pose T).

- `joints.json`: cabeza, cola y padre de cada hueso, en coordenadas de Blender (el personaje mira a −X y su derecha es +Y).
- `rig.py`: importa el glb **sin rig** (el de antes de este cambio está en el historial de git), crea la armadura y pesa las mallas: `headgear` rígido a `Head`, `gun` rígido a `RightHand` y el traje con bone heat sobre un proxy de remesh por vóxeles, resuelto por pieza suelta. Luego transfiere los pesos a los vértices del traje (6 vecinos, máx. 4 huesos).
- `export.py`: exporta el .blend a GLB (con skin, sin animaciones ni Draco).
- `posetest.py`: pone una pose de prueba y la renderiza con Workbench para revisar la deformación.
- `seven_anim.blend`: el rig con las acciones `Seven_Idle`, `Seven_Walk` y `Seven_Walk_Base` (el caminar original, solo piernas; fake user, no se exporta).
- `walk_upper_body.py`: reconstruye `Seven_Walk` desde `Seven_Walk_Base` y le da vida al torso sincronizado con las piernas: la cadera gira hacia la pierna que avanza y baja del lado de la pierna en el aire, el pecho gira al revés (~10°) y se inclina sobre la pierna de apoyo, inclinación hacia adelante que se acentúa en cada pisada, brazos que se balancean contra las piernas (izquierdo 16°, el del arma 6°), clavículas que acompañan, y cuello/cabeza que compensan para que la mirada quede fija. Las amplitudes están arriba del script; se corre dentro de Blender con el MCP (`exec(open(...).read())`).

Exportar `Assets/Models/Heroes/Seven_Anims.glb` (solo el armature con los clips): guardar el .blend, borrar `Seven_Walk_Base`, seleccionar solo `Seven_Rig` y exportar GLB con `use_selection=True`, `export_animations=True`, `export_animation_mode='ACTIONS'`; después reabrir el .blend sin guardar. En Unity, reimportar y correr `Deadlock/Setup/20 Hero Animations`.

```
blender --background --factory-startup --python rig.py -- Seven_sin_rig.glb joints.json seven_rig.blend 0.012
blender --background --factory-startup --python export.py -- seven_rig.blend Seven.glb
```

Al reimportar, glTFast cambia los fileID de la raíz del glb: `Assets/Prefabs/Heroes/Seven_Preview.prefab` apunta a la raíz por fileID y hay que reapuntarlo si cambia la jerarquía.
