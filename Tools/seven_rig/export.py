import bpy, sys
args = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
for o in bpy.data.objects:
    o.select_set(True)
bpy.ops.export_scene.gltf(
    filepath=args[1], export_format='GLB', use_selection=False,
    export_apply=False, export_skins=True, export_animations=False,
    export_morph=False, export_draco_mesh_compression_enable=False,
    export_image_format='AUTO', export_yup=True)
