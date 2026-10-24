# Esqueleto de Seven

`Assets/Models/Heroes/Seven.glb` lleva un esqueleto humanoide de 21 huesos (`Hips` … `LeftToes`, nombres tipo Unity Humanoid) hecho con Blender 5.3 en modo headless. La pose de reposo es la pose original del modelo (no una pose T).

- `joints.json`: cabeza, cola y padre de cada hueso, en coordenadas de Blender (el personaje mira a −X y su derecha es +Y).
- `rig.py`: importa el glb **sin rig** (el de antes de este cambio está en el historial de git), crea la armadura y pesa las mallas: `headgear` rígido a `Head`, `gun` rígido a `RightHand` y el traje con bone heat sobre un proxy de remesh por vóxeles, resuelto por pieza suelta. Luego transfiere los pesos a los vértices del traje (6 vecinos, máx. 4 huesos).
- `export.py`: exporta el .blend a GLB (con skin, sin animaciones ni Draco).
- `posetest.py`: pone una pose de prueba y la renderiza con Workbench para revisar la deformación.

```
blender --background --factory-startup --python rig.py -- Seven_sin_rig.glb joints.json seven_rig.blend 0.012
blender --background --factory-startup --python export.py -- seven_rig.blend Seven.glb
```

Al reimportar, glTFast cambia los fileID de la raíz del glb: `Assets/Prefabs/Heroes/Seven_Preview.prefab` apunta a la raíz por fileID y hay que reapuntarlo si cambia la jerarquía.
