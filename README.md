# meshup

## Asset generation server

Run the GPU asset generation server with the root `compose.yaml`. Build and
install its models once, then start it with `docker compose up -d asset-generator`.
See the [Docker setup instructions](asset_generator_server/README.md#run-with-docker-compose)
for prerequisites and the initial installation command.

## Game runtime configuration

`Assets/Scenes/SampleScene.unity` and `Assets/Scenes/GameScene.unity` in
`meshup-game` are the authoritative, hand-edited scenes. The old environment,
lobby, player-rig, and formation rebuild/install scripts have been removed;
edit the existing scene objects and prefabs in Unity instead.

`GameScene` contains a `MeshUp Game Runtime` root with the game coordinator and
FPS counter, transcriber, size selector, and victory fireworks component. The
coordinator's Scene and Components fields reference the existing player,
formation sequence, wall, generator anchor, particles, and gameplay components.
`MeshupGameView` owns the monitor mounts, local viewer, and authored UI controls;
`GeneratedObjectSizeSelector` owns its three physical buttons. Renaming these
objects does not break their wiring. Keep each component's Inspector references
assigned when replacing an object. Generated models are created at runtime.
The monitor and mime terminal use authored UI prefabs in
`Assets/Prefabs/Game UI`. The monitor moves its Canvas between the view's authored
front and back mounts; the terminal stays on its authored mount. Keep these
references, both Canvas raycasters, and the terminal buttons' XR interactables, colliders, and click
sounds when editing the displays. The lobby contains an
authored instance of the copied Ubiq menu prefab. `RoomTotemPanel` references its
controls directly; edit the prefab instead of reconstructing it at runtime.
Keep both raycasters on the menu's nested Canvas, its camera reference, and
the Ubiq keyboard and button sound wiring when editing the menu. The saved
scene keeps the menu at its original 0.005 world units per UI pixel.

The lobby rig's `LobbyTrackedSpawn` waits for a valid headset position, then
places the tracked body at the rig's saved spawn point. Keep this component on
the lobby XR Origin so room-scale offsets cannot place players outside the room
on entry or when returning from a game.

Victory fireworks use the same saved ParticleSystem prefabs on desktop and Quest,
under `Assets/Prefabs/Fireworks`, with their shared material in
`Assets/Art/Fireworks`. `MeshupVictoryFireworks` references these prefabs, its monitor,
and explosion clips directly. Edit the burst on each prefab's root and the rocket
trail beneath it in Unity. The old desktop VFX Graph fireworks package has been
removed. Generator completion and failure cues are saved WAV clips in
`Assets/Sounds/Generator`, assigned on `GeneratorActivityAudio`.

The coordinator registers directly with Ubiq and publishes host responses by
applying them locally before sending them to peers. Private responses still
apply only to their target player. Generated-object poses are sent while held
and immediately on release. F7/F8/F9 previews live in
`MeshupDevelopmentShortcuts` and run only in the Editor or development builds.

Gameplay player IDs are advertised in the `meshup.player` peer property and
survive Ubiq connection UUID changes. Automatic room rejoining resynchronizes
the existing match and private mime word without reloading the scene. Players
retain their scores; a mime turn already skipped after the ten-second departure
grace period is not replayed. Room protocol version 2 prevents mixing these
identities with older builds.

`GameInteractionState` owns cursor state and the pause movement lock for both
the terminal and session menu. Both interfaces reference the same scene
component. Closing either interface keeps the cursor available if the other
still needs it. Its authored desktop overlay list excludes the pause menu and
world-space UI; XR raycasters stay available. Terminal interaction keeps player
movement available during preparation.

`GeneratedObjectManager` owns generated-object state, local instances, and
pending imports. The coordinator handles match permissions and network messages.
Round cleanup hides retired objects immediately and cancels their imports;
import targets and resources are released after the pending work finishes.
Scene teardown also disposes the manager, and late completions cannot affect
replacement objects or display errors from a previous round.

The `Meshup/Lobby/Validate …` and `Meshup/Game/Validate …` editor commands check
the authored scenes without rebuilding or saving them. They inspect the current
in-memory scene if it is already open, preserving unsaved edits; otherwise they
open it temporarily and close only that scene. Validation focuses on required
runtime references, scene-reload safety, and desktop/XR interaction wiring.
Furniture, book counts, lighting, and the book reveal trajectory can be edited
without updating validators. Lighting baking and model-import processing remain
available.

Before testing on headsets, edit
`meshup-game/Assets/Resources/Game/meshup_game_config.json` so
`assetServerBaseUrl` is the LAN-reachable address of the machine running
`asset_generator_server`. Start that server with the same address in
`PUBLIC_BASE_URL`; URLs containing `127.0.0.1` are only suitable for a local
Editor client.

The server simplifies generated meshes as far as its geometric-error limit
allows. The default `SIMPLIFICATION_ERROR=0.0001` permits up to 0.01% error
relative to the mesh radius. To preserve the reconstructed triangle count,
start it with `--no-mesh-simplification`:

```bash
cd asset_generator_server
uv run python -m app.cli --no-mesh-simplification
```

The same setting can be persisted as `MESH_SIMPLIFICATION=false` in the server's
environment or `.env` file. Texture processing is controlled independently by
`TEXTURE_SIMPLIFICATION` or `--no-texture-simplification`, and its size limit
can be set with `MAX_TEXTURE_SIZE`. See
[`asset_generator_server/README.md`](asset_generator_server/README.md) for the
full server configuration.

Guess transcription uses the bundled Vosk US-English model with a grammar
restricted to the game's verb list. Recognition runs entirely on the local
Quest or desktop computer: hold either controller's primary button, or `G` on
desktop, say one guess, and release. The model is extracted to the application's
persistent data directory on first use; no network connection or voice-service
credential is required.

`VoiceChatController` owns microphone permissions and shared capture for voice
chat, guesses, and object descriptions. Both recording features use
`AudioEncoding` for mono PCM conversion; object descriptions also use its WAV
encoder. Keep permission requests in the shared controller.

Quest, macOS, and Windows microphone access is used for push-to-talk guesses,
held-button asset descriptions, and live spatial voice chat with the other
players in the current Ubiq room. UWP/MSIX builds declare Unity's Microphone
capability; Win32 builds and the Windows Editor rely on the Windows desktop-app
microphone privacy setting. Outgoing voice is automatically muted while a
guess or asset description is being recorded; incoming voice remains audible.
Press `M` on desktop, or use the Voice button in the in-game pause menu, to keep
your outgoing voice manually muted. Guess audio is processed locally and is not
uploaded. Asset descriptions are a separate feature and are uploaded to the
configured `asset_generator_server`; production consent and privacy text must
describe these distinct microphone uses.

On Quest, point at the lobby player-name field and pull the trigger to open the
native Meta keyboard. During a game, press the left controller's menu button to
open or close the pause menu; its controller-ray buttons can mute voice, resume,
or leave the room. On desktop, `Escape` opens the same menu.

The bundled model, native libraries, source pins, licenses, and checksums are
documented in `meshup-game/THIRD_PARTY_NOTICES.md`. Binary assets use Git LFS,
so contributors must install Git LFS before cloning or committing updates.
