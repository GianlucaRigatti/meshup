# meshup

## Game runtime configuration

The multiplayer game logic is installed automatically when `GameScene` loads.
Before testing on headsets, edit
`meshup-game/Assets/Resources/Game/meshup_game_config.json` so
`assetServerBaseUrl` is the LAN-reachable address of the machine running
`asset_generator_server`. Start that server with the same address in
`PUBLIC_BASE_URL`; URLs containing `127.0.0.1` are only suitable for a local
Editor client.

Guess transcription uses Meta XR Voice SDK's `AppVoiceExperience` when it is
available. Meta distributes `com.meta.xr.sdk.voice` as a restricted Unity Asset
Store UPM package, so it cannot be resolved by adding an unauthenticated
registry dependency. Acquire Meta XR Voice SDK 205 for the Unity account, add
it through Package Manager, create a project-specific Wit configuration, and
place one configured `AppVoiceExperience` in `GameScene`. The runtime adapter
will discover it automatically. Do not commit private Wit tokens.

Quest microphone access is requested for push-to-talk guesses and held-button
asset descriptions. A production build must present consent before recording
and link to a privacy policy describing Meta/Wit voice processing.
