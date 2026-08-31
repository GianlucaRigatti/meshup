# meshup

## Game runtime configuration

The multiplayer game logic is installed automatically when `GameScene` loads.
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

Quest and macOS microphone access is requested for push-to-talk guesses and
held-button asset descriptions. Guess audio is processed locally and is not
uploaded. Asset descriptions are a separate feature and are uploaded to the
configured `asset_generator_server`; production consent and privacy text must
describe that distinction.

The bundled model, native libraries, source pins, licenses, and checksums are
documented in `meshup-game/THIRD_PARTY_NOTICES.md`. Binary assets use Git LFS,
so contributors must install Git LFS before cloning or committing updates.
