import { writeFile } from 'node:fs/promises';
import { parseArgs } from 'node:util';

import { Logger, NodeIO } from '@gltf-transform/core';
import { simplify, textureCompress, weld } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';

const { values } = parseArgs({
  options: {
    input: { type: 'string' },
    output: { type: 'string' },
    stats: { type: 'string' },
    'max-texture-size': { type: 'string' },
    error: { type: 'string' },
    'lock-border': { type: 'boolean' },
    'skip-simplification': { type: 'boolean' },
    'skip-texture-simplification': { type: 'boolean' },
    help: { type: 'boolean', short: 'h' },
  },
});

if (values.help) {
  console.log(
    'Usage: simplify_glb.mjs --input INPUT --output OUTPUT --stats STATS ' +
      '--max-texture-size PIXELS --error FRACTION ' +
      '[--lock-border] [--skip-simplification] [--skip-texture-simplification]',
  );
  process.exit(0);
}

for (const name of [
  'input',
  'output',
  'stats',
  'max-texture-size',
  'error',
]) {
  if (values[name] === undefined) throw new Error(`Missing --${name}.`);
}

const maxTextureSize = Number.parseInt(values['max-texture-size'], 10);
const maxError = Number.parseFloat(values.error);
if (!Number.isSafeInteger(maxTextureSize) || maxTextureSize <= 0) {
  throw new Error('--max-texture-size must be a positive integer.');
}
if (!Number.isFinite(maxError) || maxError < 0 || maxError > 1) {
  throw new Error('--error must be between 0 and 1.');
}

const io = new NodeIO();
const document = await io.read(values.input);
document.setLogger(new Logger(Logger.Verbosity.SILENT));
const sourceTriangles = countTriangles(document);
const sourceTextureBytes = countTextureBytes(document);
let texturesResized = false;
let outputTriangles = sourceTriangles;
let wasSimplified = false;

if (!values['skip-simplification']) {
  await MeshoptSimplifier.ready;
  await document.transform(
    weld(),
    simplify({
      simplifier: MeshoptSimplifier,
      ratio: 0,
      error: maxError,
      lockBorder: values['lock-border'] ?? false,
    }),
  );
  outputTriangles = countTriangles(document);
  wasSimplified = outputTriangles < sourceTriangles;
}

if (!values['skip-texture-simplification']) {
  texturesResized = document
    .getRoot()
    .listTextures()
    .some((texture) => texture.getSize()?.some((size) => size > maxTextureSize));
  await document.transform(
    textureCompress({
      encoder: sharp,
      resize: [maxTextureSize, maxTextureSize],
      targetFormat: 'png',
      effort: 80,
    }),
  );
}
const outputTextureBytes = countTextureBytes(document);
await io.write(values.output, document);

await writeFile(
  values.stats,
  `${JSON.stringify({
    source_triangles: sourceTriangles,
    output_triangles: outputTriangles,
    simplified: wasSimplified,
    source_texture_bytes: sourceTextureBytes,
    output_texture_bytes: outputTextureBytes,
    textures_resized: texturesResized,
  })}\n`,
  'utf8',
);

function countTriangles(asset) {
  let triangles = 0;
  for (const mesh of asset.getRoot().listMeshes()) {
    for (const primitive of mesh.listPrimitives()) {
      const count = (primitive.getIndices() ?? primitive.getAttribute('POSITION'))?.getCount() ?? 0;
      const mode = primitive.getMode();
      if (mode === 4) triangles += Math.floor(count / 3);
      if (mode === 5 || mode === 6) triangles += Math.max(0, count - 2);
    }
  }
  return triangles;
}

function countTextureBytes(asset) {
  return asset
    .getRoot()
    .listTextures()
    .reduce((total, texture) => total + (texture.getImage()?.byteLength ?? 0), 0);
}
