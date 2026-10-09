<p align="center">
  <img src="readme-assets/banner.svg" alt="MeshUp — multiplayer charades with AI-generated props" width="100%">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Unity-6000.4.5f1-222C37?style=flat&amp;logo=unity&amp;logoColor=white" alt="Unity 6000.4.5f1">
  <img src="https://img.shields.io/badge/Platform-Desktop-60A5FA?style=flat" alt="Desktop">
  <img src="https://img.shields.io/badge/VR-Meta_Quest-5EEAD4?style=flat" alt="Meta Quest">
</p>

<p align="center">
  Multiplayer charades with AI-generated 3D props.<br>
  Built in <strong>Unity</strong> for <strong>desktop</strong> and <strong>Meta Quest</strong>.
</p>

<p align="center">
  <a href="#install">Get started</a> ·
  <a href="#run">Run the game</a> ·
  <a href="asset_generator_server/README.md">Asset generator</a> ·
  <a href="#controls">Controls</a>
</p>

---

## In the arena

![A generated tree appearing in the arena](readme-assets/prop-generation.gif)

*A generated prop comes to life in the arena.*

![Generated prop in the multiplayer arena](readme-assets/multiplayer.jpg)

## Install

| Requirement | Setup |
| --- | --- |
| Unity | **6000.4.5f1**, installed through Unity Hub |
| Repository assets | Git LFS |
| Quest builds | Android Build Support in Unity Hub |

With Git LFS installed, clone the repository and download its assets:

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

## Build

Use Unity's **Build Profiles** for desktop or Android; keep
`SampleScene` and `GameScene` enabled in that order.

## Controls

| Action | Desktop | Meta Quest |
| --- | --- | --- |
| Speak a guess | Hold **G** | Hold either controller's **primary button** |
| Pause menu | **Escape** | Left controller's **menu button** |
| Mute voice | **M** | — |

Allow microphone access to use voice input.

---

[Asset generator](asset_generator_server/README.md) ·
[Model experiments](model_experiments/README.md) ·
[Third-party notices](meshup-game/THIRD_PARTY_NOTICES.md)
