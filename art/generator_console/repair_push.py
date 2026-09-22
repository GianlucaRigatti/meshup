import bpy,bmesh,math,json
from mathutils import Vector,Quaternion
s=bpy.context.scene;s.frame_set(1);col=bpy.data.collections['GeneratorConsole_Asset'];root=bpy.data.objects['GeneratorConsole'];h=bpy.data.objects['Housing'];cap=bpy.data.objects['Button_Generate'];lens=bpy.data.objects['Lit_Generate']
n=Vector((0,-1,1)).normalized();rot=Vector((0,0,1)).rotation_difference(n);C=Vector((.3292,-.156,.696))
b=bmesh.new();b.from_mesh(h.data);todo=set(b.verts);remove=[]
while todo:
 stack=[todo.pop()];comp=[]
 while stack:
  v=stack.pop();comp.append(v)
  for e in v.link_edges:
   w=e.other_vert(v)
   if w in todo:todo.remove(w);stack.append(w)
 if min(v.co.x for v in comp)>.18 and max(v.co.x for v in comp)<.469 and min(v.co.z for v in comp)>.55:remove.extend(comp)
bmesh.ops.delete(b,geom=remove,context='VERTS');b.to_mesh(h.data);b.free()
def active(o):
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
def mesh(name,verts,faces,material):
 me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.materials.append(material);b=bmesh.new();b.from_mesh(me);bmesh.ops.recalc_face_normals(b,faces=list(b.faces));b.to_mesh(me);b.free();return me
def lathe(name,profile,closed,mat):
 count=24;vv=[C+rot@Vector((r*math.cos(2*math.pi*i/count),r*math.sin(2*math.pi*i/count),z)) for r,z in profile for i in range(count)];ff=[]
 for j in range(len(profile) if closed else len(profile)-1):
  for i in range(count):ff.append((j*count+i,j*count+(i+1)%count,((j+1)%len(profile))*count+(i+1)%count,((j+1)%len(profile))*count+i))
 if not closed:ff.extend([tuple(reversed(range(count))),tuple(range((len(profile)-1)*count,len(profile)*count))])
 return mesh(name,vv,ff,mat)
red=bpy.data.materials.new('Console_Red');red.diffuse_color=(.48,.035,.025,1);red.use_nodes=True;p=red.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=red.diffuse_color;p.inputs['Roughness'].default_value=.36;p.inputs['Metallic'].default_value=.15
cap.data=lathe('CleanPushCap',[(.067,-.012),(.076,-.004),(.076,.019),(.067,.028)],False,red)
# Check the cap's closed topology before adding the separate lettering.
b=bmesh.new();b.from_mesh(cap.data);audit={'cap_boundary_edges':sum(e.is_boundary for e in b.edges),'cap_non_manifold_edges':sum(not e.is_manifold for e in b.edges)};b.free();assert audit['cap_non_manifold_edges']==0
cu=bpy.data.curves.new('PushLabel','FONT');cu.body='PUSH';cu.size=.031;cu.align_x='CENTER';cu.align_y='CENTER';cu.resolution_u=2;cu.materials.append(bpy.data.materials['Console_Label']);txt=bpy.data.objects.new('PushLabel',cu);col.objects.link(txt);txt.location=C+n*.029;txt.rotation_mode='QUATERNION';txt.rotation_quaternion=rot;active(txt);bpy.ops.object.convert(target='MESH');cap.select_set(True);bpy.context.view_layer.objects.active=cap;bpy.ops.object.join()
# Ring at the same top face, outside the lettering; distinct depth avoids z-fighting.
vv=[];ff=[]
for r in [.065,.0605]:
 for i in range(24):vv.append(rot@Vector((r*math.cos(2*math.pi*i/24),r*math.sin(2*math.pi*i/24),0)))
for i in range(24):ff.append((i,(i+1)%24,24+(i+1)%24,24+i))
lens.data=mesh('CleanLightRing',vv,ff,bpy.data.materials['Console_Emission']);lens.location=C+n*.0295
static=[]
for name,dims,z,material,bw in [('GenerateFrame',(.276,.252,.016),-.012,'Console_Trim',.008),('GenerateInset',(.25,.226,.009),-.001,'Console_Graphite',.006)]:
 bpy.ops.mesh.primitive_cube_add(size=1,location=C+n*z);q=bpy.context.object;q.name=name
 for c in list(q.users_collection):c.objects.unlink(q)
 col.objects.link(q);q.parent=root;q.dimensions=dims;active(q);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);q.rotation_mode='QUATERNION';q.rotation_quaternion=rot;q.data.materials.append(bpy.data.materials[material]);mod=q.modifiers.new('Bevel','BEVEL');mod.width=bw;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name);static.append(q)
me=lathe('ButtonSocket',[(.085,-.001),(.085,.005),(.078,.005),(.078,-.001)],True,bpy.data.materials['Console_Trim']);q=bpy.data.objects.new('ButtonSocket',me);col.objects.link(q);q.parent=root;static.append(q)
active(h)
for q in static:q.select_set(True)
bpy.context.view_layer.objects.active=h;bpy.ops.object.join()
audit['socket_radial_clearance_m']=.002;audit['pressed_face_clearance_m']=.028-.014-.005;audit['triangles']=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in col.objects if o.type=='MESH')
open('/Users/mattia/Documents/programming/meshup/art/generator_console/push_validation.json','w').write(json.dumps(audit,indent=2))
for a in bpy.context.screen.areas:
 if a.type=='VIEW_3D':a.spaces.active.region_3d.view_rotation=Quaternion((.88,.40,.10,.22)).normalized();a.spaces.active.region_3d.view_distance=.62;a.spaces.active.region_3d.view_location=C
result=audit
