# Esqueleto y animaciones de Vindicta

`Assets/Models/Heroes/Vindicta.glb` lleva un esqueleto de 21 huesos con los mismos nombres que el de Seven (`Hips` … `LeftToes`). Ella mira a −Y en Blender y su derecha es −X. La pose de reposo es la original (postura de francotiradora, piernas abiertas).

- `joints.json`: cabeza, cola y padre de cada hueso, medidos sobre renders ortográficos de la malla.
- `rig.py`: igual que `Tools/seven_rig/rig.py` pero con su malla `body` (cuerpo, pelo y vestido en una sola) y el arma `gun` rígida a `RightHand`. Pesos con bone heat sobre un proxy de vóxeles. **No corre en el Blender de la Microsoft Store** (acceso denegado desde la línea de comandos): se ejecutó por etapas dentro de una sesión de Blender con el MCP.
- `vindicta_anim.blend`: escena con el rig pesado y las acciones `Vindicta_Walk` (1 s) y `Vindicta_Idle` (4 s).
- `tidy_glb.py`: quita la escena vacía y las animaciones de otro personaje de un glb exportado desde una sesión con varios personajes.

El idle parte de la pose original. El caminar parte de una pose neutra (piernas juntas): cada pierna se gira hasta quedar vertical y sobre eso se balancea; la cadera baja para que el pie de apoyo toque el piso.

A Unity entran `Vindicta.glb` (malla con piel, sin animaciones) y `Vindicta_Anims.glb` (solo el armature con los clips). Al cambiar `Vindicta.glb`, `Vindicta_Preview.prefab` hay que retargetearlo: los fileID del Transform raíz, del GameObject (`m_Name`) y `m_SourcePrefab` dependen del glb (se leen con `AssetDatabase.TryGetGUIDAndLocalFileIdentifier`; `m_SourcePrefab` sale de instanciar el glb y guardar un prefab nuevo).
