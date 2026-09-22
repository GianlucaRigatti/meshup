import bpy, bmesh, math, json, os
from mathutils import Vector, Quaternion
OUT='/Users/mattia/Documents/programming/meshup/art/generator_console'
source=bpy.data.objects['Object_9']
scene=bpy.data.scenes.new('Integrated Generator Console')
bpy.context.window.scene=scene
col=bpy.data.collections.new('GeneratorConsole_Asset');scene.collection.children.link(col)
root=bpy.data.objects.new('GeneratorConsole',None);col.objects.link(root)
# Extract evaluated original geometry, retaining the original UVs and textures.
dg=bpy.context.evaluated_depsgraph_get()
# Source belongs to the previous scene; evaluate there before returning.
bpy.context.window.scene=bpy.data.scenes['Scene'];dg=bpy.context.evaluated_depsgraph_get()
ev=source.evaluated_get(dg);base=bpy.data.meshes.new_from_object(ev);base.transform(ev.matrix_world)
bpy.context.window.scene=scene
bm=bmesh.new();bm.from_mesh(base);bm.verts.ensure_lookup_table();unseen=set(bm.verts);sets={'Housing':set(),'GenerateBezel':set(),'Button_Generate':set()}
while unseen:
 todo=[unseen.pop()];comp=[]
 while todo:
  v=todo.pop();comp.append(v)
  for e in v.link_edges:
   w=e.other_vert(v)
   if w in unseen: unseen.remove(w);todo.append(w)
 inds={v.index for v in comp};moving=any(any(g.group==3 for g in base.vertices[i].groups) for i in inds)
 is_control=min(v.co.z for v in comp)>1.4 and max(v.co.x for v in comp)-min(v.co.x for v in comp)<.8
 sets['Button_Generate' if moving else 'GenerateBezel' if is_control else 'Housing'].update(inds)
bm.free()
def activate(o):
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
for name,inds in sets.items():
 me=base.copy();b=bmesh.new();b.from_mesh(me);b.verts.ensure_lookup_table();bmesh.ops.delete(b,geom=[v for v in b.verts if v.index not in inds],context='VERTS');b.to_mesh(me);b.free()
 o=bpy.data.objects.new(name,me);col.objects.link(o);o.parent=root
 for v in me.vertices:
  if name=='Housing':v.co.x=(v.co.x-.0029)*2.7
  else:v.co.x+=.82
 activate(o)
 mod=o.modifiers.new('Planar reduction','DECIMATE');mod.decimate_type='DISSOLVE';mod.angle_limit=.025;bpy.ops.object.modifier_apply(modifier=mod.name)
 if name!='Housing':
  mod=o.modifiers.new('Control optimization','DECIMATE');mod.ratio=.55;bpy.ops.object.modifier_apply(modifier=mod.name)
def material(name,color,emission=0):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=.43;p.inputs['Metallic'].default_value=.25;p.inputs['Emission Color'].default_value=(*color,1);p.inputs['Emission Strength'].default_value=emission;return m
black=material('Console_Graphite',(.018,.032,.041));metal=material('Console_Trim',(.21,.29,.31));ink=material('Console_Label',(.8,.95,.96),.3)
glow=material('Console_Emission',(.02,.8,1),3)
n=Vector((0,-1,1)).normalized();rot=Vector((0,0,1)).rotation_difference(n)
def cylinder(name,radius,depth,loc,mat,verts=12):
 bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=radius,depth=depth,location=loc);o=bpy.context.object;o.name=name
 for c in list(o.users_collection):c.objects.unlink(o)
 col.objects.link(o);o.rotation_mode='QUATERNION';o.rotation_quaternion=rot;activate(o);bpy.ops.object.transform_apply(location=False,rotation=True,scale=True);o.data.materials.append(mat);o.parent=root;return o
def join_into(objects,target):
 bpy.ops.object.select_all(action='DESELECT')
 for o in objects+[target]:o.select_set(True)
 bpy.context.view_layer.objects.active=target;bpy.ops.object.join()
def label(text,loc,size,parent):
 # Low-poly stroke glyphs, no fonts/textures or dense converted text.
 patterns={'S':['111','100','111','001','111'],'M':['10001','11011','10101','10001','10001'],'X':['101','101','010','101','101'],'L':['100','100','100','100','111']}
 verts=[];faces=[];cursor=0
 for char in text:
  rows=patterns[char]
  for y,row in enumerate(rows):
   for x,val in enumerate(row):
    if val=='1':
     k=len(verts);xx=(cursor+x)*size;yy=(4-y)*size
     verts.extend([(xx,yy,0),(xx+size*.88,yy,0),(xx+size*.88,yy+size*.88,0),(xx,yy+size*.88,0)]);faces.append((k,k+1,k+2,k+3))
  cursor+=len(rows[0])+1
 center=Vector(((cursor-1)*size/2,2.5*size,0));me=bpy.data.meshes.new('Label_'+text);me.from_pydata([rot@(Vector(v)-center)+Vector(loc) for v in verts],[],faces);me.materials.append(ink);o=bpy.data.objects.new('Label_'+text,me);col.objects.link(o);join_into([o],parent)
buttons=[]
for name,x,text in [('Small',-1.0,'S'),('Medium',-.49,'M'),('ExtraLarge',.02,'XL')]:
 pos=Vector((x,-.39,1.74));bezel=cylinder('Bezel_'+name,.218,.048,pos,metal)
 cap=cylinder('Button_'+name,.177,.052,pos+n*.036,black)
 label(text,pos+n*.063,.022,cap)
 lens=cylinder('Lit_'+name,.177,.003,pos+n*.066,glow)
 # Label sits above the luminous overlay too.
 for v in cap.data.vertices:
  pass
 # Luminous rim uses a smaller strip below the label instead of obscuring text.
 bpy.data.objects.remove(lens,do_unlink=True)
 lens=cylinder('Lit_'+name,.039,.004,pos+rot@Vector((0,-.13,.066)),glow,8)
 mw=lens.matrix_world.copy();lens.parent=cap;lens.matrix_world=mw
 buttons.append((name,cap,lens))
cap=bpy.data.objects['Button_Generate'];lens=cylinder('Lit_Generate',.066,.004,(.823,-.557,1.80),glow,10);mw=lens.matrix_world.copy();lens.parent=cap;lens.matrix_world=mw;buttons.append(('Generate',cap,lens))
# Four independent named clips; transform-based emissive indicator works in glTF/FBX.
scene.render.fps=30;scene.frame_start=1;scene.frame_end=16
for name,cap,lens in buttons:
 rest=cap.location.copy()
 for frame,amount in [(1,0),(4,1),(10,1),(16,0)]:
  cap.location=rest-n*.035*amount;cap.keyframe_insert('location',frame=frame)
  lens.scale=(1,1,1) if amount else (.001,.001,.001);lens.keyframe_insert('scale',frame=frame)
 for o in [cap,lens]:
  action=o.animation_data.action;action.name=name+'_Press_'+o.name
  track=o.animation_data.nla_tracks.new();track.name=name+'_Press';track.strips.new(name+'_Press',1,action);o.animation_data.action=None
scene.frame_set(1)
# Fold static meshes into one draw hierarchy while retaining the original material.
static=[o for o in col.objects if o.type=='MESH' and o.name not in [v.name for _,c,l in buttons for v in [c,l]]]
join_into([o for o in static if o.name!='Housing'],bpy.data.objects['Housing'])
for o in col.objects:
 if o.type=='MESH':
  for p in o.data.polygons:p.use_smooth=False
# Original proportions, widened 2.7x; present at a usable 1.08m x .56m x .8m.
for o in col.objects:
 if o.type=='MESH':
  for v in o.data.vertices:v.co*=.4
  o.location*=.4
  if o.animation_data:
   for tr in o.animation_data.nla_tracks:
    for st in tr.strips:
     for layer in st.action.layers:
      for strip in layer.strips:
       for bag in strip.channelbags:
        for fc in bag.fcurves:
         if fc.data_path=='location':
          for kp in fc.keyframe_points:kp.co.y*=.4;kp.handle_left.y*=.4;kp.handle_right.y*=.4
scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
for o in col.objects:o.select_set(True)
bpy.context.view_layer.objects.active=bpy.data.objects['Housing']
for a in bpy.context.screen.areas:
 if a.type=='VIEW_3D':
  a.spaces.active.shading.type='MATERIAL';a.spaces.active.overlay.show_overlays=False
  a.spaces.active.region_3d.view_rotation=Quaternion((.88,.40,.10,.22)).normalized()
  with bpy.context.temp_override(area=a,region=next(r for r in a.regions if r.type=='WINDOW')):bpy.ops.view3d.view_selected()
result={'triangles':{o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in col.objects if o.type=='MESH'}}
print(json.dumps(result))
