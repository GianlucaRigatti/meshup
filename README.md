# MeshUp

Multiplayer charades in Unity for desktop and Meta Quest, with AI-generated 3D props.

![A generated tree appearing in the arena](readme-assets/prop-generation.gif)

![Generated prop in the multiplayer arena](readme-assets/multiplayer.jpg)

## Install

Install Git LFS and Unity **6000.4.5f1** (add Android Build Support for Quest).

```bash
git lfs install
git clone https://github.com/GianlucaRigatti/meshup.git
cd meshup
git lfs pull
```

## Run

1. Start the [asset generation server](asset_generator_server/README.md#docker-compose).
2. Open `meshup-game` through Unity Hub.
3. Open `Assets/Scenes/SampleScene.unity` and press **Play**.
4. In the lobby, set **Asset server** to the server machine's IP address and port,
   then create or join a room.

For Quest or other computers, set the server's `PUBLIC_BASE_URL` to its
LAN address (for example, `http://192.168.1.10:8000`).

To build, use Unity's **Build Profiles** for desktop or Android; keep
`SampleScene` and `GameScene` enabled in that order.

Desktop controls: hold **G** to speak a guess, **M** to mute voice, **Escape**
for the pause menu. On Quest, hold a controller's primary button to guess
and use the left controller's menu button to pause. Allow microphone access.

[Model experiments](model_experiments/README.md) ·
[Third-party notices](meshup-game/THIRD_PARTY_NOTICES.md)
