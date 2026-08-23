# Ubiq Room Scene Switcher

A reusable Unity package for browsing, creating, joining, and leaving Ubiq
rooms that share a multiplayer scene. It keeps one Ubiq networking root and one
project-owned player/XR rig alive while Unity switches between a private lobby
scene and a multiplayer scene.

## Requirements

- Unity `6000.4.5f1`
- Ubiq `upm-unity-v1.0.0-pre.16`
- uGUI `2.0.0` (installed automatically by this package)

The package is the root of this repository. Do not copy a surrounding Unity
project, `Library` directory, or `ProjectSettings` directory.

## 1. Install Ubiq in the target project

This package deliberately does not bundle Ubiq. Pin the supported Ubiq tag in
the project first:

1. Open the target project in Unity `6000.4.5f1`.
2. Open **Window > Package Management > Package Manager**.
3. Press **+** and select **Install package from git URL**.
4. Enter:

   ```text
   https://github.com/UCL-VR/ubiq.git#upm-unity-v1.0.0-pre.16
   ```

The target project's `Packages/manifest.json` should then contain:

```json
"com.ucl.ubiq": "https://github.com/UCL-VR/ubiq.git#upm-unity-v1.0.0-pre.16"
```

If the project already contains Ubiq, verify its tag before changing it. Do not
install a second copy.

## 2. Install this package

Choose one installation method.

### Local installation

Use this while developing the package locally:

1. In Package Manager, press **+**.
2. Select **Install package from disk**.
3. Select this repository's `package.json`.

Unity adds a local `file:` dependency. This path is machine-specific, so it is
not the best choice for a project shared by a team.

### Embedded installation

Use this when the package should travel with the Unity project:

1. Copy this entire repository into:

   ```text
   YourUnityProject/Packages/com.mattia.ubiq-scene-switcher/
   ```

2. Add this entry to the target project's `Packages/manifest.json`:

   ```json
   "com.mattia.ubiq-scene-switcher": "file:com.mattia.ubiq-scene-switcher"
   ```

### Git installation

Once this package repository is hosted remotely, select **Install package from
git URL** and provide the repository URL, optionally followed by a tag:

```text
https://your-host/your-org/ubiq-scene-switcher.git#v0.1.0
```

Because `package.json` is at the repository root, no `?path=` suffix is needed.

After installation, wait for Unity to finish compiling. The Console should
contain no compilation errors, and these menus should appear:

```text
GameObject > Ubiq > Room Scene Session
GameObject > Ubiq > Room Scene Anchor
Tools > Ubiq Scene Switcher
```

## 3. Prepare the scenes

Create or identify two scenes:

- A private lobby scene where users browse and create rooms.
- A multiplayer scene shared by users in a compatible Ubiq room.

The application must start in the private scene. Returning from multiplayer
reloads this scene, so its runtime state is intentionally not preserved.

## 4. Add the persistent session

Open the private scene and either:

- Drag **Packages > Ubiq Room Scene Switcher > Runtime > Prefabs > Room Scene
  Session** into the hierarchy, or
- Choose **GameObject > Ubiq > Room Scene Session**.

The initial hierarchy is:

```text
Room Scene Session
└── Player Rig Root
```

Move the project-owned camera, desktop player, or XR Origin under
`Player Rig Root`:

```text
Room Scene Session
└── Player Rig Root
    └── Your Player or XR Origin
```

Assign the `Player Rig Root` transform to the matching field on
`RoomSceneSwitcher`. The switcher moves this whole container to each scene's
spawn anchor.

Persistent Ubiq systems such as avatar management, avatar input, display-name
management, and VoIP should also live under `Room Scene Session`.

## 5. Configure RoomSceneSwitcher

Select `Room Scene Session` and configure:

| Field | Value |
| --- | --- |
| Private Scene | The lobby/private scene |
| Multiplayer Scene | The shared environment scene |
| Player Rig Root | The persistent rig container |
| Application ID | A stable ID such as `com.company.product` |
| Protocol Version | `1` initially |
| Room Client | The `RoomClient` on the same root |
| Auto Refresh Rooms | Enable for automatic lobby discovery |

Application ID, multiplayer scene GUID, and protocol version form the room
compatibility contract. Clients must match all three values to discover or
join each other.

## 6. Configure RoomClient

Configure the `RoomClient` server definitions as in a normal Ubiq application.
Every client that should meet must use the same Ubiq room server.

The multiplayer scene must not contain another:

- Root `NetworkScene`
- `RoomClient`
- Local player/XR rig
- Local camera or audio listener

Scene-local networked objects are allowed. They register with the persistent
`NetworkScene` after the multiplayer scene loads.

## 7. Add scene anchors

Each scene requires exactly one `RoomSceneAnchor`.

In the private scene:

1. Choose **GameObject > Ubiq > Room Scene Anchor**.
2. Set **Role** to `Private`.
3. Assign **Player Spawn**, or position the anchor itself at the desired spawn.

In the multiplayer scene:

1. Create another `Room Scene Anchor`.
2. Set **Role** to `Multiplayer`.
3. Assign or position its player spawn.

If `Player Spawn` is empty, the anchor's own transform is used.

Components implementing `IPlayerRigReset` beneath the persistent rig are
called after it moves. Use this hook to clear locomotion velocity or teleport a
custom character controller correctly.

## 8. Add and validate the scenes

Return to the private scene and select `Room Scene Session`.

1. Click **Add Missing Scenes** in the `RoomSceneSwitcher` Inspector.
2. Click **Validate Scene Contents**.
3. Fix every reported error.
4. Ensure the private scene is the first enabled scene in the build profile.

The validator checks scene references, Build Settings, hierarchy persistence,
anchors, duplicate networking roots, and competing `RoomJoiner` components.

## 9. Import the optional room browser

1. Open Package Manager.
2. Select **Ubiq Room Scene Switcher**.
3. Open **Samples** and import **Room Browser UI**.
4. Drag `Room Browser Canvas` into the private scene.
5. Parent it under `Room Scene Session` or the persistent player rig.

The Canvas must be persistent. If left as a private-scene root, Unity unloads
it during the multiplayer transition and its Leave button becomes unavailable.

The sample is a world-space uGUI Canvas. For XR, configure the project's
EventSystem and XR UI raycasters to interact with it.

## 10. Run the flow

Start Play Mode from the private scene. The expected sequence is:

1. `RoomClient` connects to the Ubiq room server.
2. The switcher enters an unpublished private room.
3. The browser discovers compatible published rooms.
4. Creating or joining loads the multiplayer scene before entering the room.
5. Leaving first exits shared peer scope, then reloads the private scene.

For a multiplayer test, run two standalone builds or one build alongside the
Unity Editor. Both clients need the same server, application ID, multiplayer
scene, and protocol version.

## Custom UI API

Application UI should call `RoomSceneSwitcher`, not `RoomClient` or
`SceneManager` directly:

```csharp
using Ubiq.SceneSwitcher;
using UnityEngine;

public sealed class MyRoomControls : MonoBehaviour
{
    [SerializeField] private RoomSceneSwitcher switcher;

    public async void RefreshRooms()
    {
        var result = await switcher.RefreshRoomsAsync();
        if (!result.Succeeded)
        {
            Debug.LogError(result.Failure);
        }
    }

    public async void CreateRoom(string roomName)
    {
        var result = await switcher.CreateAndEnterAsync(
            new RoomCreateOptions(roomName, publish: true));
        if (!result.Succeeded)
        {
            Debug.LogError(result.Failure);
        }
    }

    public async void JoinByCode(string joinCode)
    {
        var result = await switcher.JoinByCodeAsync(joinCode);
        if (!result.Succeeded)
        {
            Debug.LogError(result.Failure);
        }
    }

    public async void LeaveRoom()
    {
        await switcher.ReturnToPrivateSceneAsync();
    }
}
```

Available C# events are:

```csharp
switcher.StateChanged += HandleStateChanged;
switcher.RoomsChanged += HandleRoomsChanged;
switcher.CurrentRoomChanged += HandleCurrentRoomChanged;
switcher.OperationFailed += HandleFailure;
```

Equivalent Inspector `UnityEvent` hooks are exposed on the component.

## Room compatibility metadata

New rooms remain hidden until the server confirms these properties:

```text
ubiq.scene-switcher.app
ubiq.scene-switcher.scene
ubiq.scene-switcher.protocol
```

Rooms with missing or different values are not listed. Join-by-code also
validates these properties before changing scenes.

## Leaving and reconnection

Ubiq has no separate `Leave` API. The switcher leaves shared peer scope with
`RoomClient.Join("", false)`, entering an empty unpublished room while keeping
the server connection available for browsing.

Ubiq's `ReconnectAndRejoin` behavior remains enabled. A transient empty-room
event during reconnection does not cause an unwanted scene change.

## Troubleshooting

- **Package does not compile:** verify Unity `6000.4.5f1` and the exact Ubiq
  pre.16 tag.
- **No rooms appear:** confirm both clients use the same server, application ID,
  scene asset, and protocol version. Only published rooms appear in browsing.
- **Join code is rejected before loading:** the target room is missing or has
  incompatible metadata.
- **Player or UI disappears:** ensure it is a child of `Room Scene Session`.
- **Duplicate camera/audio warnings:** remove the local rig from the multiplayer
  scene.
- **Anchor validation fails:** each configured scene must have exactly one
  anchor with the correct role.

## Development verification

The package includes EditMode tests for metadata and filtering plus PlayMode
tests for stale Ubiq response handling. In a host Unity project, add this
package to the `testables` array in `Packages/manifest.json` to expose them in
the Test Runner:

```json
"testables": ["com.mattia.ubiq-scene-switcher"]
```
