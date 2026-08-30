import { copyFile, writeFile } from 'node:fs/promises';
import { parseArgs } from 'node:util';

import { Logger, NodeIO } from '@gltf-transform/core';
import { simplify, weld } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';

const { values } = parseArgs({
  options: {
    input: { type: 'string' },
    output: { type: 'string' },
    stats: { type: 'string' },
    'max-triangles': { type: 'string' },
    error: { type: 'string' },
    help: { type: 'boolean', short: 'h' },
  },
});

if (values.help) {
  console.log(
    'Usage: simplify_glb.mjs --input INPUT --output OUTPUT --stats STATS ' +
      '--max-triangles COUNT --error FRACTION',
  );
  process.exit(0);
}

for (const name of ['input', 'output', 'stats', 'max-triangles', 'error']) {
  if (values[name] === undefined) throw new Error(`Missing --${name}.`);
}

const maxTriangles = Number.parseInt(values['max-triangles'], 10);
const maxError = Number.parseFloat(values.error);
if (!Number.isSafeInteger(maxTriangles) || maxTriangles <= 0) {
  throw new Error('--max-triangles must be a positive integer.');
}
if (!Number.isFinite(maxError) || maxError < 0 || maxError > 1) {
  throw new Error('--error must be between 0 and 1.');
}

const io = new NodeIO();
const document = await io.read(values.input);
document.setLogger(new Logger(Logger.Verbosity.SILENT));
const sourceTriangles = countTriangles(document);
let outputTriangles = sourceTriangles;
let wasSimplified = false;

if (sourceTriangles > maxTriangles) {
  await MeshoptSimplifier.ready;
  await document.transform(
    weld(),
    simplify({
      simplifier: MeshoptSimplifier,
      ratio: maxTriangles / sourceTriangles,
      error: maxError,
    }),
  );
  outputTriangles = countTriangles(document);
  wasSimplified = outputTriangles < sourceTriangles;
  await io.write(values.output, document);
} else {
  await copyFile(values.input, values.output);
}

await writeFile(
  values.stats,
  `${JSON.stringify({
    source_triangles: sourceTriangles,
    output_triangles: outputTriangles,
    simplified: wasSimplified,
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
