# Integrated generator console

Widened derivative of `meshup-game/Assets/EXTRA_Resources/Asset_generator_machine/scifi_button.glb`. Retains the original sloped housing silhouette and red PUSH control design. The control and bezel were rebuilt with closed geometry to eliminate gaps and overlapping faces. The housing is rebuilt as a closed, chamfered solid. S / M / XL and Generate share one fitted panel. The widened surfaces use clean PBR materials instead of stretched source artwork. Original source attribution/license still applies.

- 1,497 triangles total, including labels and all four illuminated indicators.
- 1.08 m wide × 0.56 m deep × 0.80 m high; bottom origin. Scale the root to fit the game.
- 9 rigid mesh objects, 6 shared materials, 15 material primitives before engine batching. No armature, real-time lights or transparency.
- Texture-free PBR materials throughout; GLB is 142,436 bytes.
- `GeneratorConsole.blend`: isolated editable scene.
- `GeneratorConsole.glb`: asset and four independent clips. Copy also installed at `meshup-game/Assets/Art/GeneratorConsole/GeneratorConsole.glb`.

## Animation

`Small_Press`, `Medium_Press`, `ExtraLarge_Press`, `Generate_Press`: 0.5 seconds each, 14 mm inward travel, press/hold/release. Each clip targets only its named button and child `Lit_*` indicator. The indicator uses emissive geometry with animated scale, avoiding material-property animation that may not survive import. Emission does not illuminate nearby surfaces; no bloom is required to see the indicator.

In Blender, frames 1–16 demonstrate all four buttons together; frame 5 shows them pressed and lit. Mute other NLA tracks to preview one button. Each exported clip is separate.

## Unity hookup

Import using the project's existing GLB importer. Bind the four `Button_*` objects to the game's interaction events and play their corresponding non-looping clips. Add simple box colliders for interaction. For a held interaction, hold the pressed pose until release. For persistent size selection, keep the selected `Lit_*` at scale one and others at 0.001 after the press clip completes.

This is an asset delivery: the existing game scene and its interaction code are not replaced. `GeneratedObjectSizeSelector.FindPhysicalButtons` currently discovers renderers by materials starting with `Button` and expects the older three-button layout. Bind these named objects explicitly when integrating; the current heuristic will not automatically find them. Configure imported animation components/controller according to the project's importer settings.

## Verification

Verified the housing shell has zero boundary or non-manifold edges before joining decorative geometry; verified evaluated Blender triangle count, packed source textures, a single exported scene, all four exported animation channels, 0.5-second duration, 14 mm travel, lit state and return to rest. `python3 art/generator_console/verify_export.py` checks the actual GLB data. Visuals checked at idle and pressed states. Unity playback and on-device Quest performance have not been tested.

Authoring scripts document the construction steps: `build_console.py` expects the original GLB imported into a scene named `Scene` with mesh `Object_9`; run `refine_console.py` and then `export_console.py` once in that order. They are not idempotent. Open the delivered blend for normal editing.

Surface revision: `repair_surface.py` replaces the initial widened shell after the original authoring steps. Open the delivered blend for the final revision; do not rerun the old export script, which also applies the initial positioning adjustment.

Push revision: `repair_push.py` replaces the inherited control geometry. Cap checked for closed topology, with 2 mm radial socket clearance and 9 mm face clearance at full press. `package_console.py` exports the current asset without modifying its geometry or animation.

Final spacing: Generate assembly shifted 25 mm left, leaving 22.8 mm horizontal clearance to the right fasteners. Front accent bar removed.

Size controls shifted 25 mm right as a group, including collars, labels, indicators and animation positions, to balance the panel spacing.
