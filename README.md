<h1 align="center">MeshUp - Immersive Technologies Course @UniTN</h1>
<p align="center">
  <img src="https://img.shields.io/badge/Unity-6000.4.5f1-222C37?style=flat&amp;logo=unity&amp;logoColor=white" alt="Unity 6000.4.5f1">
  <img src="https://img.shields.io/badge/Platform-Desktop-60A5FA?style=flat" alt="Desktop">
  <img src="https://img.shields.io/badge/VR-Meta_Quest-5EEAD4?style=flat" alt="Meta Quest">
</p>

<p align="center"> <img src="readme-assets/prop-generation.gif" alt="A generated tree appearing in the arena" /> </p>

<p align="center">
This project investigates whether it is possible to integrate generative artificial intelligence into real-time virtual reality experiences and whether communication and enjoyment can benefit by on-demand generated objects in social environments.
</p>
<p align="center">
MeshUp is designed as a social VR game in which players have to communicate an action through mimicking and manipulation of 3D objects. Our solution integrates a local gen-
eration pipeline combining speech recognition, prompt optimisation, text-to-image, and image-to-3D designed to integrate with Unity and the Ubiq framework and able to return
a complete asset in less than 2 minutes on consumer hardware.
</p>
<p align="center">
A user study comparing MeshUp with a webcam-based and drawing-based setting suggests that generative 3D content can provide additional resources for unleashing creativity and contribute to more engaging VR experiences, motivating further experiments on the role of AI as a creative assistant.
</p>

---

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
4. In the lobby, set **Asset server** to the server machine's IP address and port, then create or join a room.

## Build

Use Unity's **Build Profiles** for desktop or Android; keep `SampleScene` and `GameScene` enabled in that order.

## Group Contributions

| Team Member | Contributions |
|---|---|
| **Mattia Ferretti** | Implementation of the bedroom scene in Unity, development of core game logic and server integration, and development of the asset generator server. |
| **Patrick Barberi** | Design and implementation of the underwater environment, including scene design and realization in Unity, and preparation of the project presentation. |
| **Gianluca Rigatti** | Development and testing of the asset generator server, initial project setup, report writing, and design of the evaluation methodology. |

**Note:** Throughout both the design and implementation phases of all *MeshUp* components, we worked collaboratively, providing mutual feedback, fixing bugs, debugging, and integrating the different components into a cohesive project.
