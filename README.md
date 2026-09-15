# meshup

## Game runtime configuration

`Assets/Scenes/SampleScene.unity` and `Assets/Scenes/GameScene.unity` in
`meshup-game` are the authoritative, hand-edited scenes. The old environment,
lobby, player-rig, and formation rebuild/install scripts have been removed;
edit the existing scene objects and prefabs in Unity instead.

`GameScene` contains a `MeshUp Game Runtime` root with the game coordinator and
FPS counter. The coordinator's Scene fields reference the existing player,
formation sequence, wall, monitors, generator button, and particles directly.
Renaming those objects does not break the coordinator's wiring. Keep these
references assigned when replacing an object. The game still creates its
dynamic UI and generated models at runtime with the existing behavior.

`GeneratedObjectManager` owns generated-object state, local instances, and
pending imports. The coordinator handles match permissions and network messages.
Round cleanup hides retired objects immediately and cancels their imports;
import targets and resources are released after the pending work finishes.
Scene teardown also disposes the manager, and late completions cannot affect
replacement objects or display errors from a previous round.

The `Meshup/Lobby/Validate …` and `Meshup/Game/Validate …` editor commands check
the authored scenes without rebuilding or saving them. They inspect the current
in-memory scene if it is already open, preserving unsaved edits; otherwise they
open it temporarily and close only that scene. Some checks enforce the existing
layout and interaction requirements, so review them when intentionally changing
the design. Lighting baking and model-import processing remain available.

Before testing on headsets, edit
`meshup-game/Assets/Resources/Game/meshup_game_config.json` so
`assetServerBaseUrl` is the LAN-reachable address of the machine running
`asset_generator_server`. Start that server with the same address in
`PUBLIC_BASE_URL`; URLs containing `127.0.0.1` are only suitable for a local
Editor client.

Guess transcription uses the bundled Vosk US-English model with a grammar
restricted to the game's verb list. Recognition runs entirely on the local
Quest or desktop computer: hold either controller's primary button, or `G` on
desktop, say one guess, and release. The model is extracted to the application's
persistent data directory on first use; no network connection or voice-service
credential is required.

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
