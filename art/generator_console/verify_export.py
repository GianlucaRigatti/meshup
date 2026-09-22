import json, struct, pathlib, math
path=pathlib.Path(__file__).with_name('GeneratorConsole.glb')
b=path.read_bytes();length=struct.unpack_from('<I',b,12)[0];j=json.loads(b[20:20+length]);binary=b[28+length:]
def values(index):
 a=j['accessors'][index];v=j['bufferViews'][a['bufferView']];n={'SCALAR':1,'VEC3':3,'VEC4':4}[a['type']];assert a['componentType']==5126
 offset=v.get('byteOffset',0)+a.get('byteOffset',0);stride=v.get('byteStride',4*n)
 return [struct.unpack_from('<'+'f'*n,binary,offset+i*stride) for i in range(a['count'])]
triangles=sum(j['accessors'][p['indices']]['count']//3 for m in j['meshes'] for p in m['primitives'])
assert triangles==1497 and len(j['scenes'])==1
assert {a['name'] for a in j['animations']}=={'Small_Press','Medium_Press','ExtraLarge_Press','Generate_Press'}
report=[]
for a in j['animations']:
 assert len(a['channels'])==2
 for c in a['channels']:
  sampler=a['samplers'][c['sampler']];times=values(sampler['input']);vs=values(sampler['output']);assert abs(times[-1][0]-times[0][0]-.5)<1e-5
  assert max(abs(x-y) for x,y in zip(vs[0],vs[-1]))<1e-6
  if c['target']['path']=='translation':
   travel=max(math.dist(v,vs[0]) for v in vs);assert abs(travel-.014)<1e-5
  else:assert max(v[0] for v in vs)==1 and min(v[0] for v in vs)<.002
 report.append({'name':a['name'],'duration_seconds':.5,'travel_m':round(travel,4),'returns_to_rest':True,'emission_indicator_animated':True})
result={'triangles':triangles,'export_bytes':len(b),'scene_count':1,'clips':report,'materials':len(j['materials']),'mesh_primitives':sum(len(m['primitives']) for m in j['meshes'])}
path.with_name('export_validation.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
