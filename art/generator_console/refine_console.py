import bpy,bmesh,math,json,os
from mathutils import Vector
s=bpy.context.scene;s.frame_set(1);col=bpy.data.collections['GeneratorConsole_Asset'];root=bpy.data.objects['GeneratorConsole'];n=Vector((0,-1,1)).normalized();rot=Vector((0,0,1)).rotation_difference(n)
def activate(o):
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
# One recessed selector plate follows the slope and covers the stretched old control imprint.
bpy.ops.mesh.primitive_cube_add(size=1,location=(-.196,-.153,.692));p=bpy.context.object;p.name='SizeSelectorPlate';p.dimensions=(.636,.207,.015);p.rotation_mode='QUATERNION';p.rotation_quaternion=rot;activate(p);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
mod=p.modifiers.new('Machined corners','BEVEL');mod.width=.014;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name);p.data.materials.append(bpy.data.materials['Console_Graphite'])
for c in list(p.users_collection):c.objects.unlink(p)
col.objects.link(p);p.parent=root
# Replace pixel glyphs with readable low-resolution mesh lettering.
for name,text in [('Small','S'),('Medium','M'),('ExtraLarge','XL')]:
 cap=bpy.data.objects['Button_'+name];b=bmesh.new();b.from_mesh(cap.data);inds={i for i,m in enumerate(cap.data.materials) if m.name=='Console_Label'};bmesh.ops.delete(b,geom=[f for f in b.faces if f.material_index in inds],context='FACES');b.to_mesh(cap.data);b.free()
 cu=bpy.data.curves.new('Glyph_'+text,'FONT');cu.body=text;cu.align_x='CENTER';cu.align_y='CENTER';cu.size=.045 if text!='XL' else .038;cu.resolution_u=2
 o=bpy.data.objects.new('Glyph_'+text,cu);col.objects.link(o);o.location=cap.location+n*.0115;o.rotation_mode='QUATERNION';o.rotation_quaternion=rot;cu.materials.append(bpy.data.materials['Console_Label']);activate(o);bpy.ops.object.convert(target='MESH');o=bpy.context.object;o.select_set(True);cap.select_set(True);bpy.context.view_layer.objects.active=cap;bpy.ops.object.join()
# Emissive ring keeps the original red PUSH face fully readable.
lens=bpy.data.objects['Lit_Generate'];verts=[];faces=[]
for r in [.068,.061]:
 for i in range(20):
  t=2*math.pi*i/20;verts.append(rot@Vector((r*math.cos(t),r*math.sin(t),0)))
for i in range(20):j=(i+1)%20;faces.append((i,j,20+j,20+i))
me=bpy.data.meshes.new('Generate luminous rim');me.from_pydata(verts,[],faces);me.materials.append(bpy.data.materials['Console_Emission']);lens.data=me;lens.location=Vector((.3292,-.1776,.7259))+n*.002
# Shared, downsampled copies retain the source texture style at lower memory cost.
old=bpy.data.materials['UV_switch'];new=old.copy();new.name='Console_Original_512';copies={}
for node in new.node_tree.nodes:
 if node.type=='TEX_IMAGE' and node.image:
  im=node.image
  if im.name not in copies:
   cp=im.copy();cp.name='Console_'+im.name+'_512';cp.scale(512,512);cp.pack();copies[im.name]=cp
  node.image=copies[im.name]
for o in col.objects:
 if o.type=='MESH':
  for slot in o.material_slots:
   if slot.material==old:slot.material=new
# Join plate to static housing.
activate(p);h=bpy.data.objects['Housing'];h.select_set(True);bpy.context.view_layer.objects.active=h;bpy.ops.object.join()
s.frame_set(5)
result={'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in col.objects if o.type=='MESH')}
print(json.dumps(result))
