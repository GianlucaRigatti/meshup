# Room Browser UI sample

Add `RoomBrowserPresenter` to a world-space Canvas and assign:

- A private browser panel with room-name, publish, and join-code controls.
- A `RoomListItemView` template below the room-list root.
- A current-room panel with a leave button.
- Optional busy and error panels.

Parent the Canvas under `Room Scene Session` (or under its persistent player
rig). A Canvas left as a private-scene root will correctly be unloaded by the
single-scene transition and therefore cannot provide the multiplayer leave
button.

The presenter finds the persistent `RoomSceneSwitcher` if its explicit session
reference is empty. It contains no direct Ubiq or scene-loading logic, so its
visual hierarchy can be replaced without changing the room workflow.
