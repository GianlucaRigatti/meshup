import bpy,json
out='/Users/mattia/Documents/programming/meshup/art/generator_console/';s=bpy.context.scene;s.frame_set(1);col=bpy.data.collections['GeneratorConsole_Asset']
bpy.ops.object.select_all(action='DESELECT')
for o in col.objects:o.select_set(True)
bpy.ops.export_scene.gltf(filepath=out+'GeneratorConsole.glb',use_selection=True,use_active_scene=True,export_animation_mode='NLA_TRACKS',export_merge_animation='NLA_TRACK',export_extras=True,export_anim_slide_to_zero=True)
bpy.data.libraries.write(out+'GeneratorConsole.blend',{s},fake_user=True,compress=True)
dg=bpy.context.evaluated_depsgraph_get();meshes=[]
for o in col.objects:
 if o.type=='MESH':
  ev=o.evaluated_get(dg);me=ev.to_mesh();me.calc_loop_triangles();meshes.append({'name':o.name,'triangles':len(me.loop_triangles),'materials':sorted(set(me.materials[p.material_index].name for p in me.polygons))});ev.to_mesh_clear()
report=json.load(open(out+'validation.json'));report['triangles']=sum(m['triangles'] for m in meshes);report['meshes']=meshes;report['texture_resolution']=None
open(out+'validation.json','w').write(json.dumps(report,indent=2));result=report
