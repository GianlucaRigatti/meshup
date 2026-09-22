import bpy,bmesh,math,json
from mathutils import Vector,Quaternion
s=bpy.context.scene;s.frame_set(1);col=bpy.data.collections['GeneratorConsole_Asset'];root=bpy.data.objects['GeneratorConsole'];old=bpy.data.objects['Housing']
n=Vector((0,-1,1)).normalized();rot=Vector((0,0,1)).rotation_difference(n)
# Keep the original Generate bezel only; replace the perforated widened shell.
b=bmesh.new();b.from_mesh(old.data);b.verts.ensure_lookup_table();todo=set(b.verts);keep=set()
while todo:
 stack=[todo.pop()];comp=[]
 while stack:
  v=stack.pop();comp.append(v)
  for e in v.link_edges:
   w=e.other_vert(v)
   if w in todo:todo.remove(w);stack.append(w)
 if min(v.co.x for v in comp)>.18 and min(v.co.z for v in comp)>.55:
  keep.update(comp)
bmesh.ops.delete(b,geom=[v for v in b.verts if v not in keep],context='VERTS');b.to_mesh(old.data);b.free();old.name='GenerateBezel'
def mat(name,c,metal=0,rough=.5):
 m=bpy.data.materials.new(name);m.diffuse_color=(*c,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough;return m
ivory=mat('Console_Ceramic',(.63,.67,.65),.12,.42);graphite=bpy.data.materials['Console_Graphite'];trim=bpy.data.materials['Console_Trim'];ink=bpy.data.materials['Console_Label']
def link(o):
 for c in list(o.users_collection):c.objects.unlink(o)
 col.objects.link(o);o.parent=root
 return o
def active(o):
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
def bevel(o,width):
 active(o);mod=o.modifiers.new('Edge chamfers','BEVEL');mod.width=width;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)
 for p in o.data.polygons:p.use_smooth=False
# Closed extrusion with the same sloped silhouette and footprint.
profile=[(-.258,0),(.302,0),(.302,.8),(-.008,.8),(-.258,.55)]
verts=[(x,y,z) for x in [-.54,.54] for y,z in profile];faces=[tuple(reversed(range(5))),tuple(range(5,10))]+[(i,(i+1)%5,(i+1)%5+5,i+5) for i in range(5)]
me=bpy.data.meshes.new('ClosedHousing');me.from_pydata(verts,[],faces);o=bpy.data.objects.new('Housing',me);col.objects.link(o);o.parent=root;me.materials.append(ivory)
b=bmesh.new();b.from_mesh(me);bmesh.ops.recalc_face_normals(b,faces=list(b.faces));b.to_mesh(me);b.free();bevel(o,.018)
b=bmesh.new();b.from_mesh(o.data);closed={'boundary_edges':sum(e.is_boundary for e in b.edges),'non_manifold_edges':sum(not e.is_manifold for e in b.edges)};b.free();assert closed['non_manifold_edges']==0
static=[o,old]
def box(name,loc,dim,material,rotation=None,chamfer=0):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);q=link(bpy.context.object);q.name=name;q.dimensions=dim;active(q);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if rotation:q.rotation_mode='QUATERNION';q.rotation_quaternion=rotation
 q.data.materials.append(material)
 if chamfer:bevel(q,chamfer)
 static.append(q);return q
panel=box('UnifiedControlPanel',(0,-.145,.680),(1.002,.284,.014),graphite,rot,.012)
# Rebuild clean collars; each cap can move fully down while remaining above the plate.
for name in ['Small','Medium','ExtraLarge']:
 cap=bpy.data.objects['Button_'+name];center=cap.location-n*.006
 vv=[];ff=[]
 for radius,height in [(.087,-.009),(.087,.001),(.073,.001),(.073,-.009)]:
  for i in range(12):
   t=2*math.pi*i/12;vv.append(center+rot@Vector((radius*math.cos(t),radius*math.sin(t),height)))
 for j in range(4):
  for i in range(12):ff.append((j*12+i,j*12+(i+1)%12,((j+1)%4)*12+(i+1)%12,((j+1)%4)*12+i))
 mm=bpy.data.meshes.new('Collar');mm.from_pydata(vv,[],ff);mm.materials.append(trim);q=bpy.data.objects.new('Collar_'+name,mm);col.objects.link(q);q.parent=root;static.append(q)
# Small flush fasteners at the corners of the single panel.
for x in [-.474,.474]:
 for t in [-.116,.116]:
  pos=Vector((x,-.145,.68))+rot@Vector((0,t,.008))
  bpy.ops.mesh.primitive_cylinder_add(vertices=6,radius=.009,depth=.003,location=pos);q=link(bpy.context.object);q.rotation_mode='QUATERNION';q.rotation_quaternion=rot;q.data.materials.append(trim);static.append(q)
# Crisp geometry graphics instead of stretching the old warning label.
front_rot=Quaternion((1,0,0),math.pi/2)
def text(body,location,size,material):
 cu=bpy.data.curves.new('Face marking','FONT');cu.body=body;cu.size=size;cu.resolution_u=2;cu.space_character=1.15
 q=bpy.data.objects.new('Face marking',cu);col.objects.link(q);q.parent=root;q.location=location;q.rotation_mode='QUATERNION';q.rotation_quaternion=front_rot;cu.materials.append(material);active(q);bpy.ops.object.convert(target='MESH');static.append(bpy.context.object)
box('FrontAccent',(-.465,-.259,.433),(.009,.003,.085),trim,chamfer=.002)
for i in range(4):box('Vent detail',(.36,-.259,.10+i*.025),(.19,.003,.008),graphite,chamfer=.003)
box('Lower seam',(0,-.257,.047),(1.0,.004,.004),trim)
# Keep original control texture at source resolution on its small, unscaled surface.
# All widened surfaces now use UV-independent PBR colors.
active(o)
for q in static:q.select_set(True)
bpy.context.view_layer.objects.active=o;bpy.ops.object.join()
active(o)
original=next(i for i,m in enumerate(o.data.materials) if m.name=='Console_Original_512')
graphite_index=next(i for i,m in enumerate(o.data.materials) if m.name=='Console_Graphite')
for p in o.data.polygons:
 if p.material_index==original:p.material_index=graphite_index
for a in bpy.context.screen.areas:
 if a.type=='VIEW_3D':
  a.spaces.active.shading.type='MATERIAL';a.spaces.active.overlay.show_overlays=False;a.spaces.active.region_3d.view_rotation=Quaternion((.88,.40,.10,.22)).normalized();a.spaces.active.region_3d.view_distance=2.2;a.spaces.active.region_3d.view_location=Vector((0,0,.43))
triangles=sum(sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in col.objects if ob.type=='MESH')
result={'housing_shell':closed,'triangles':triangles,'materials':[m.name for m in o.data.materials]}
open('/Users/mattia/Documents/programming/meshup/art/generator_console/surface_validation.json','w').write(json.dumps(result,indent=2))
print(json.dumps(result))
