import bpy,json,os
from mathutils import Vector
out='/Users/mattia/Documents/programming/meshup/art/generator_console'
s=bpy.context.scene;col=bpy.data.collections['GeneratorConsole_Asset'];n=Vector((0,-1,1)).normalized();offset=-n*.017
h=bpy.data.objects['Housing'];slots={i for i,m in enumerate(h.data.materials) if m.name in ['Console_Graphite','Console_Trim']};indices={v for p in h.data.polygons if p.material_index in slots for v in p.vertices}
for i in indices:h.data.vertices[i].co+=offset
for name in ['Small','Medium','ExtraLarge']:
 o=bpy.data.objects['Button_'+name]
 for tr in o.animation_data.nla_tracks:
  for st in tr.strips:
   for layer in st.action.layers:
    for strip in layer.strips:
     for bag in strip.channelbags:
      for fc in bag.fcurves:
       if fc.data_path=='location':
        for kp in fc.keyframe_points:
         d=offset[fc.array_index];kp.co.y+=d;kp.handle_left.y+=d;kp.handle_right.y+=d
s.frame_set(1);s.unit_settings.system='METRIC'
# Root metadata documents interaction hooks without engine-specific components.
r=bpy.data.objects['GeneratorConsole'];r['source_asset']='scifi_button.glb';r['press_travel_m']=.014;r['press_duration_seconds']=.5;r['size_options']='Small, Medium, ExtraLarge';r['forward']='-Y in Blender, +Z in Unity'
bpy.ops.object.select_all(action='DESELECT')
for o in col.objects:o.select_set(True)
bpy.ops.export_scene.gltf(filepath=out+'/GeneratorConsole.glb',use_selection=True,use_active_scene=True,export_animations=True,export_animation_mode='NLA_TRACKS',export_merge_animation='NLA_TRACK',export_extras=True,export_lights=False,export_cameras=False,export_anim_slide_to_zero=True)
# Write a new source file without saving over or changing the user's open file.
bpy.data.libraries.write(out+'/GeneratorConsole.blend',{s},fake_user=True,compress=True)
dg=bpy.context.evaluated_depsgraph_get();meshes=[]
for o in col.objects:
 if o.type=='MESH':
  ev=o.evaluated_get(dg);me=ev.to_mesh();me.calc_loop_triangles();meshes.append({'name':o.name,'triangles':len(me.loop_triangles),'vertices':len(me.vertices),'materials':[m.name for m in me.materials if m]});ev.to_mesh_clear()
report={'triangles':sum(m['triangles'] for m in meshes),'meshes':meshes,'size_m':[1.08,.56,.8],'press_travel_m':.014,'clips':['Small_Press','Medium_Press','ExtraLarge_Press','Generate_Press'],'clip_seconds':.5,'texture_resolution':512}
open(out+'/validation.json','w').write(json.dumps(report,indent=2));result=report
