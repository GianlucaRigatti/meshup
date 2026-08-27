using System;
using System.IO;
using System.Linq;
using Meshup.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meshup.EditorTools
{
    public static class LobbyTokenExperienceBuilder
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MaterialFolder = "Assets/Materials/Lobby/Bedroom";
        private static Font uiFont;

        private sealed class TokenParts
        {
            public GameObject FlightBook;
            public GameObject OpenBook;
            public Transform LeftCover;
            public Transform RightCover;
            public Transform LeftPages;
            public Transform RightPages;
            public Transform ImpactRing;
            public GameObject ProjectorEffects;
            public Transform HologramAnchor;
            public Light ProjectorLight;
            public Vector3 ShelfPosition;
            public Vector3 ShelfEuler;
            public Vector3 LandingPosition;
            public Vector3 ArcControlPoint;
        }

        private sealed class UiParts
        {
            public GameObject PanelRoot;
            public RectTransform PanelRect;
            public CanvasGroup PanelGroup;
            public GameObject IdleRoot;
            public GameObject LegacyPrompt;
            public Text RoomNameText;
            public Button CreateButton;
            public Button RefreshButton;
            public Button CloseButton;
            public Transform RoomListContent;
            public RoomListItemView RoomListTemplate;
            public Text StatusText;
            public GameObject NoRoomsMessage;
        }

        [MenuItem("Meshup/Lobby/Build Falling Book Hologram")]
        public static void BuildExperience()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var player = RequireRoot(scene, "Lobby Player");
            var totem = RequireRoot(scene, "Room Totem");
            var lobbyUi = RequireRoot(scene, "Lobby UI");
            var environment = RequireRoot(scene, "Bedroom Environment");
            var camera = player.GetComponentInChildren<Camera>(true)
                ?? throw new InvalidOperationException("Lobby camera is missing.");
            var bookcase = FindTransform(environment.transform, "Kenney Bookcase");

            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var token = BuildToken(totem.transform, bookcase);
            var ui = BuildWorldSpaceUi(lobbyUi, camera, token.HologramAnchor);
            ConfigureRuntime(totem, lobbyUi, player, token, ui);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Falling-book hologram token built without regenerating the manually edited bedroom.");
        }

        public static void BuildFromCommandLine()
        {
            BuildExperience();
            ValidateExperience();
            LobbyBedroomPolishPass.ValidatePolishPass();
            LobbyBedroomBuilder.ValidateLobby();
        }

        public static void EnterPlayModeForVerification()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Meshup/Lobby/Validate Falling Book Hologram")]
        public static void ValidateExperience()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var player = RequireRoot(scene, "Lobby Player");
            var totem = RequireRoot(scene, "Room Totem");
            var lobbyUi = RequireRoot(scene, "Lobby UI");
            var reveal = totem.GetComponent<FallingBookReveal>();
            var interaction = totem.GetComponent<RoomTotemInteraction>();
            var panel = lobbyUi.GetComponent<RoomTotemPanel>();
            var billboard = lobbyUi.GetComponent<HologramBillboard>();
            var canvas = lobbyUi.GetComponent<Canvas>();

            if (reveal == null || interaction == null || panel == null || billboard == null
                || canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            {
                throw new InvalidOperationException("The falling-book hologram runtime is incomplete.");
            }
            if (totem.transform.Find("Magic Storybook/Flight Book") == null
                || totem.transform.Find("Magic Storybook/Open Book") == null
                || totem.transform.Find("Magic Storybook/Open Book/Hologram Effects") == null
                || totem.transform.Find("Magic Storybook/Hologram Anchor") == null)
            {
                throw new InvalidOperationException("The animated book geometry is incomplete.");
            }
            if (lobbyUi.transform.Find("Hologram Idle") == null
                || lobbyUi.transform.Find("Room Totem Panel") == null
                || lobbyUi.transform.Find("Interaction Prompt") != null)
            {
                throw new InvalidOperationException("The automatic VR hologram UI is incomplete or still requires E.");
            }

            var revealData = new SerializedObject(reveal);
            var landing = revealData.FindProperty("landingPosition").vector3Value;
            var shelf = revealData.FindProperty("shelfPosition").vector3Value;
            if (shelf.y < 1f || landing.y > 0.2f || Vector3.Distance(shelf, landing) < 1.5f
                || revealData.FindProperty("panel").objectReferenceValue != panel
                || revealData.FindProperty("lobbyPlayer").objectReferenceValue
                    != player.GetComponent<LobbyFirstPersonController>())
            {
                throw new InvalidOperationException("The automatic reveal trajectory or menu opening is not configured.");
            }

            var panelData = new SerializedObject(panel);
            foreach (var field in new[]
                     {
                         "panelRoot", "roomNameText", "createButton", "refreshButton", "closeButton",
                         "roomListContent", "roomListItemTemplate", "statusText", "noRoomsMessage"
                     })
            {
                if (panelData.FindProperty(field).objectReferenceValue == null)
                {
                    throw new InvalidOperationException($"The hologram panel field is missing: {field}");
                }
            }
            if (panelData.FindProperty("allowClose").boolValue
                || panelData.FindProperty("lockPlayerInputWhenOpen").boolValue
                || uiHasActiveCloseButton(lobbyUi.transform))
            {
                throw new InvalidOperationException("The persistent VR hologram must not close or lock locomotion.");
            }

            if (lobbyUi.GetComponentInChildren<InputField>(true) != null)
            {
                throw new InvalidOperationException("The minimal hologram must not contain an editable text field.");
            }

            var buttons = lobbyUi.GetComponentsInChildren<Button>(true);
            if (buttons.Length < 4 || buttons.Any(button => button.GetComponent<RectTransform>().rect.height < 54f))
            {
                throw new InvalidOperationException("The VR hologram needs large ray-friendly button targets.");
            }

            Debug.Log($"Falling-book hologram validation passed with {buttons.Length} large UI targets.");
        }

        public static void CaptureFinalPreviews()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var player = RequireRoot(scene, "Lobby Player");
            var totem = RequireRoot(scene, "Room Totem");
            var lobbyUi = RequireRoot(scene, "Lobby UI");
            var camera = player.GetComponentInChildren<Camera>(true)
                ?? throw new InvalidOperationException("Lobby camera is missing.");
            var reveal = totem.GetComponent<FallingBookReveal>();
            var revealData = new SerializedObject(reveal);
            var landing = revealData.FindProperty("landingPosition").vector3Value;
            var landingEuler = revealData.FindProperty("landingEuler").vector3Value;

            totem.transform.SetPositionAndRotation(landing, Quaternion.Euler(landingEuler));
            totem.transform.Find("Magic Storybook/Flight Book").gameObject.SetActive(false);
            totem.transform.Find("Magic Storybook/Open Book").gameObject.SetActive(true);
            totem.transform.Find("Magic Storybook/Open Book/Hologram Effects").gameObject.SetActive(true);
            totem.transform.Find("Magic Storybook/Impact Ring").gameObject.SetActive(false);
            totem.transform.Find("Magic Storybook/Open Book/Left Cover Pivot").localRotation = Quaternion.Euler(0f, 0f, -7f);
            totem.transform.Find("Magic Storybook/Open Book/Right Cover Pivot").localRotation = Quaternion.Euler(0f, 0f, 7f);
            totem.transform.Find("Magic Storybook/Open Book/Left Pages Pivot").localRotation = Quaternion.Euler(0f, 0f, -4f);
            totem.transform.Find("Magic Storybook/Open Book/Right Pages Pivot").localRotation = Quaternion.Euler(0f, 0f, 4f);

            lobbyUi.SetActive(true);
            lobbyUi.GetComponent<CanvasGroup>().alpha = 1f;
            lobbyUi.transform.Find("Hologram Idle").gameObject.SetActive(false);
            lobbyUi.transform.Find("Room Totem Panel").gameObject.SetActive(true);
            var anchor = totem.transform.Find("Magic Storybook/Hologram Anchor");

            var views = new[]
            {
                ("/tmp/meshup-hologram-menu.png", new Vector3(0.16f, 1.55f, -2.15f), anchor.position),
                ("/tmp/meshup-open-book.png", new Vector3(0.10f, 0.62f, -1.02f), landing + new Vector3(0f, 0.24f, 0f))
            };
            foreach (var view in views)
            {
                camera.transform.position = view.Item2;
                camera.transform.rotation = Quaternion.LookRotation(view.Item3 - view.Item2, Vector3.up);
                lobbyUi.transform.position = anchor.position;
                lobbyUi.transform.rotation = Quaternion.LookRotation(anchor.position - camera.transform.position, Vector3.up);
                Capture(camera, view.Item1);
            }
        }

        public static void CaptureShelfPreview()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var player = RequireRoot(scene, "Lobby Player");
            var totem = RequireRoot(scene, "Room Totem");
            var lobbyUi = RequireRoot(scene, "Lobby UI");
            var camera = player.GetComponentInChildren<Camera>(true)
                ?? throw new InvalidOperationException("Lobby camera is missing.");
            lobbyUi.SetActive(false);
            totem.transform.Find("Magic Storybook/Flight Book").gameObject.SetActive(true);
            totem.transform.Find("Magic Storybook/Open Book").gameObject.SetActive(false);
            camera.transform.position = new Vector3(1.78f, 1.56f, -0.08f);
            var target = totem.transform.position + new Vector3(0f, 0.02f, 0f);
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
            Capture(camera, "/tmp/meshup-token-shelf.png");
        }

        private static TokenParts BuildToken(Transform totem, Transform bookcase)
        {
            for (var i = totem.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(totem.GetChild(i).gameObject);
            }

            // Reserve a clean slot at the right end of the upper shelf for the
            // storybook instead of laying it across the other books.
            foreach (var extraName in new[] { "Extra Book 15", "Extra Book 16" })
            {
                var extra = bookcase.root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == extraName);
                if (extra != null)
                {
                    UnityEngine.Object.DestroyImmediate(extra.gameObject);
                }
            }

            var bookcaseBounds = CalculateBounds(bookcase.gameObject);
            const float shelfScale = 0.52f;
            const float closedBookWidth = 0.49f;
            const float closedBookHeight = 0.63f;
            var upperShelfSurface = bookcaseBounds.min.y + bookcaseBounds.size.y * 0.70f;
            var parts = new TokenParts
            {
                ShelfPosition = new Vector3(bookcaseBounds.min.x + 0.14f,
                    upperShelfSurface + closedBookHeight * shelfScale * 0.5f,
                    bookcaseBounds.center.z + 0.18f),
                LandingPosition = new Vector3(0.16f, 0.085f, -0.18f)
            };
            // Local Z is the book's height, local X its cover width, and local Y
            // the cover normal. Map those to world up, shelf-left, and room-facing.
            parts.ShelfEuler = Quaternion.LookRotation(Vector3.up, Vector3.left).eulerAngles;
            parts.ArcControlPoint = Vector3.Lerp(parts.ShelfPosition, parts.LandingPosition, 0.48f)
                + new Vector3(-0.28f, 1.25f, 0.12f);

            totem.SetPositionAndRotation(parts.ShelfPosition, Quaternion.Euler(parts.ShelfEuler));
            totem.localScale = Vector3.one * shelfScale;
            var storybook = Child(totem, "Magic Storybook");
            var cover = Material("Storybook Cover");
            var pages = Material("Storybook Pages");
            var gold = HologramMaterial("Hologram Gold Dim", "#D6A958", 0.42f);
            var cyan = HologramMaterial("Hologram Cyan Dim", "#4AC7D8", 0.5f);
            var glass = HologramBeamMaterial();

            var flight = Child(storybook, "Flight Book");
            parts.FlightBook = flight.gameObject;
            CreateCube("Closed Pages", flight, new Vector3(0f, 0f, 0f), new Vector3(0.43f, 0.09f, 0.57f), pages);
            CreateCube("Closed Lower Cover", flight, new Vector3(0f, -0.055f, 0f), new Vector3(0.49f, 0.035f, 0.63f), cover);
            CreateCube("Closed Upper Cover", flight, new Vector3(0f, 0.055f, 0f), new Vector3(0.49f, 0.035f, 0.63f), cover);
            CreateCylinder("Closed Spine", flight, new Vector3(-0.255f, 0f, 0f), 0.055f, 0.62f, cover,
                Quaternion.Euler(90f, 0f, 0f));
            var coverSigil = Child(flight, "Story Archive Sigil");
            coverSigil.localPosition = new Vector3(0f, 0.078f, 0f);
            CreateRing("Circular Sigil", coverSigil, 0.115f, 14, gold);
            CreateSphere("Sigil Core", coverSigil, Vector3.zero,
                new Vector3(0.045f, 0.012f, 0.045f), gold);

            var open = Child(storybook, "Open Book");
            parts.OpenBook = open.gameObject;
            parts.LeftCover = Child(open, "Left Cover Pivot");
            parts.RightCover = Child(open, "Right Cover Pivot");
            parts.LeftPages = Child(open, "Left Pages Pivot");
            parts.RightPages = Child(open, "Right Pages Pivot");
            CreateCube("Left Cover", parts.LeftCover, new Vector3(-0.22f, 0f, 0f),
                new Vector3(0.44f, 0.035f, 0.61f), cover);
            CreateCube("Right Cover", parts.RightCover, new Vector3(0.22f, 0f, 0f),
                new Vector3(0.44f, 0.035f, 0.61f), cover);
            CreateCube("Left Page Block", parts.LeftPages, new Vector3(-0.205f, 0.035f, 0f),
                new Vector3(0.40f, 0.045f, 0.56f), pages);
            CreateCube("Right Page Block", parts.RightPages, new Vector3(0.205f, 0.035f, 0f),
                new Vector3(0.40f, 0.045f, 0.56f), pages);
            for (var i = 0; i < 3; i++)
            {
                var z = -0.16f + i * 0.16f;
                CreateCube($"Left Glowing Text {i + 1}", parts.LeftPages,
                    new Vector3(-0.205f, 0.062f, z), new Vector3(0.22f, 0.009f, 0.016f), gold);
                CreateCube($"Right Glowing Text {i + 1}", parts.RightPages,
                    new Vector3(0.205f, 0.062f, z), new Vector3(0.22f, 0.009f, 0.016f), gold);
            }
            CreateCylinder("Open Spine", open, new Vector3(0f, 0.025f, 0f), 0.025f, 0.59f, cover,
                Quaternion.Euler(90f, 0f, 0f));

            var impact = Child(storybook, "Impact Ring");
            parts.ImpactRing = impact;
            CreateRing("Impact Glow", impact, 0.42f, 24, cyan);

            var effects = Child(open, "Hologram Effects");
            parts.ProjectorEffects = effects.gameObject;
            var beam = CreateCylinder("Projection Beam", effects, new Vector3(0f, 0.68f, 0f),
                0.20f, 1.16f, glass);
            var innerRing = Child(effects, "Inner Projection Ring");
            CreateRing("Inner Ring Segments", innerRing, 0.28f, 20, gold);
            innerRing.localPosition = new Vector3(0f, 0.14f, 0f);
            var outerRing = Child(effects, "Outer Projection Ring");
            CreateRing("Outer Ring Segments", outerRing, 0.40f, 26, cyan);
            outerRing.localPosition = new Vector3(0f, 0.19f, 0f);
            CreateSphere("Projection Core", effects, new Vector3(0f, 0.17f, 0f),
                Vector3.one * 0.085f, cyan);
            var effect = effects.gameObject.AddComponent<HologramProjectorEffect>();
            SetObject(effect, "innerRing", innerRing);
            SetObject(effect, "outerRing", outerRing);
            SetObject(effect, "beamRenderer", beam.GetComponent<Renderer>());

            var projectorLightObject = new GameObject("Hologram Projector Light");
            projectorLightObject.transform.SetParent(effects, false);
            projectorLightObject.transform.localPosition = new Vector3(0f, 0.27f, 0f);
            parts.ProjectorLight = projectorLightObject.AddComponent<Light>();
            parts.ProjectorLight.type = LightType.Point;
            parts.ProjectorLight.color = Html("#63D9ED");
            parts.ProjectorLight.intensity = 0.18f;
            parts.ProjectorLight.range = 1.45f;
            parts.ProjectorLight.shadows = LightShadows.None;

            parts.HologramAnchor = Child(storybook, "Hologram Anchor");
            parts.HologramAnchor.localPosition = new Vector3(0f, 1.26f, 0f);

            flight.gameObject.SetActive(true);
            open.gameObject.SetActive(false);
            effects.gameObject.SetActive(false);
            impact.gameObject.SetActive(false);
            return parts;
        }

        private static UiParts BuildWorldSpaceUi(GameObject lobbyUi, Camera camera, Transform anchor)
        {
            for (var i = lobbyUi.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(lobbyUi.transform.GetChild(i).gameObject);
            }

            var rect = lobbyUi.GetComponent<RectTransform>();
            rect.SetParent(null, false);
            rect.sizeDelta = new Vector2(840f, 520f);
            rect.localScale = Vector3.one * 0.0015f;
            rect.position = anchor.position;
            rect.rotation = Quaternion.LookRotation(rect.position - camera.transform.position, Vector3.up);

            var canvas = lobbyUi.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.sortingOrder = 30;
            var scaler = lobbyUi.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 18f;
            if (lobbyUi.GetComponent<GraphicRaycaster>() == null)
            {
                lobbyUi.AddComponent<GraphicRaycaster>();
            }
            var rootGroup = GetOrAdd<CanvasGroup>(lobbyUi);
            rootGroup.alpha = 1f;

            var result = new UiParts();
            var idle = MakeImage("Hologram Idle", rect, new Vector2(280f, 96f), Vector2.zero,
                new Color(0.012f, 0.04f, 0.065f, 0.9f));
            result.IdleRoot = idle.gameObject;
            Border(idle, new Color(0.18f, 0.78f, 0.9f, 0.75f), 3f);
            MakeText("Idle Title", idle, "ROOMS", 34,
                new Vector2(230f, 58f), Vector2.zero, Html("#D9FCFF"), TextAnchor.MiddleCenter,
                FontStyle.Bold);

            result.LegacyPrompt = new GameObject("Legacy Interaction Prompt");
            result.LegacyPrompt.transform.SetParent(rect, false);
            result.LegacyPrompt.SetActive(false);

            var panelRect = MakeImage("Room Totem Panel", rect, new Vector2(820f, 500f), Vector2.zero,
                new Color(0.008f, 0.03f, 0.05f, 0.96f));
            result.PanelRoot = panelRect.gameObject;
            result.PanelRect = panelRect;
            result.PanelGroup = panelRect.gameObject.AddComponent<CanvasGroup>();
            Border(panelRect, new Color(0.18f, 0.78f, 0.9f, 0.75f), 3f);

            MakeText("Rooms Header", panelRect, "ROOMS", 34,
                new Vector2(240f, 54f), new Vector2(-270f, 210f), Html("#D9FCFF"),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            result.CloseButton = MakeButton("Disabled Close Control", panelRect, "CLOSE",
                new Vector2(128f, 62f), new Vector2(0f, -500f), Color.clear, Color.clear);
            result.CloseButton.gameObject.SetActive(false);
            result.RefreshButton = MakeButton("Refresh Button", panelRect, "REFRESH",
                new Vector2(128f, 56f), new Vector2(330f, 210f),
                new Color(0.035f, 0.15f, 0.19f, 1f), Html("#DDFBFF"));
            result.StatusText = MakeText("Status Text", panelRect, string.Empty, 17,
                new Vector2(410f, 36f), new Vector2(30f, 210f), Html("#74D6E1"),
                TextAnchor.MiddleCenter);
            MakeImage("Header Divider", panelRect, new Vector2(760f, 2f), new Vector2(0f, 171f),
                new Color(0.18f, 0.78f, 0.9f, 0.32f));

            var scrollRoot = MakeImage("Room List", panelRect, new Vector2(760f, 270f),
                new Vector2(0f, 20f), new Color(0.004f, 0.02f, 0.034f, 0.72f));
            var viewport = MakeImage("Viewport", scrollRoot, new Vector2(740f, 250f), Vector2.zero,
                new Color(0f, 0f, 0f, 0f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = MakeRect("Content", viewport, new Vector2(720f, 0f), new Vector2(0f, 120f));
            content.anchorMin = new Vector2(0.5f, 1f);
            content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 26f;
            result.RoomListContent = content;
            result.RoomListTemplate = MakeRoomItemTemplate(content);

            var noRooms = MakeText("No Rooms Message", viewport,
                "No rooms yet", 23, new Vector2(360f, 60f), Vector2.zero,
                Html("#6EAAB3"), TextAnchor.MiddleCenter);
            result.NoRoomsMessage = noRooms.gameObject;

            MakeImage("Create Divider", panelRect, new Vector2(760f, 2f), new Vector2(0f, -135f),
                new Color(0.18f, 0.78f, 0.9f, 0.22f));
            MakeText("New Room Label", panelRect, "NEW ROOM", 15,
                new Vector2(180f, 26f), new Vector2(-290f, -174f), Html("#62AEB8"),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            result.RoomNameText = MakeText("Generated Room Name", panelRect, "Cozy Comet", 29,
                new Vector2(430f, 48f), new Vector2(-165f, -209f), Html("#E2FCFF"),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            result.CreateButton = MakeButton("Create Button", panelRect, "CREATE",
                new Vector2(176f, 66f), new Vector2(292f, -197f),
                new Color(0.04f, 0.52f, 0.62f, 1f), Color.white);

            panelRect.gameObject.SetActive(true);
            idle.gameObject.SetActive(true);
            return result;
        }

        private static void ConfigureRuntime(GameObject totem, GameObject lobbyUi, GameObject player,
            TokenParts token, UiParts ui)
        {
            var interaction = totem.GetComponent<RoomTotemInteraction>()
                ?? totem.AddComponent<RoomTotemInteraction>();
            var trigger = totem.GetComponent<SphereCollider>() ?? totem.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 2.4f;
            trigger.center = new Vector3(0f, 0.65f, 0f);
            trigger.enabled = true;
            var body = totem.GetComponent<Rigidbody>() ?? totem.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var panel = lobbyUi.GetComponent<RoomTotemPanel>() ?? lobbyUi.AddComponent<RoomTotemPanel>();
            SetObject(panel, "panelRoot", ui.PanelRoot);
            SetObject(panel, "roomNameText", ui.RoomNameText);
            SetObject(panel, "createButton", ui.CreateButton);
            SetObject(panel, "refreshButton", ui.RefreshButton);
            SetObject(panel, "closeButton", ui.CloseButton);
            SetObject(panel, "roomListContent", ui.RoomListContent);
            SetObject(panel, "roomListItemTemplate", ui.RoomListTemplate);
            SetObject(panel, "statusText", ui.StatusText);
            SetObject(panel, "noRoomsMessage", ui.NoRoomsMessage);
            SetBool(panel, "allowClose", false);
            SetBool(panel, "lockPlayerInputWhenOpen", false);

            SetObject(interaction, "interactionPrompt", ui.LegacyPrompt);
            SetObject(interaction, "panel", panel);

            var playerController = player.GetComponent<LobbyFirstPersonController>()
                ?? throw new InvalidOperationException("Lobby first-person controller is missing.");
            var canvasGroup = lobbyUi.GetComponent<CanvasGroup>() ?? lobbyUi.AddComponent<CanvasGroup>();
            var billboard = lobbyUi.GetComponent<HologramBillboard>() ?? lobbyUi.AddComponent<HologramBillboard>();
            SetObject(billboard, "followAnchor", token.HologramAnchor);
            SetObject(billboard, "panel", panel);
            SetObject(billboard, "idleRoot", ui.IdleRoot);
            SetObject(billboard, "panelRoot", ui.PanelRect);
            SetObject(billboard, "panelCanvasGroup", ui.PanelGroup);

            var oldReveal = totem.GetComponent<FallingBookReveal>();
            if (oldReveal != null)
            {
                UnityEngine.Object.DestroyImmediate(oldReveal);
            }
            var reveal = totem.AddComponent<FallingBookReveal>();
            SetVector(reveal, "shelfPosition", token.ShelfPosition);
            SetVector(reveal, "landingPosition", token.LandingPosition);
            SetVector(reveal, "arcControlPoint", token.ArcControlPoint);
            SetVector(reveal, "shelfEuler", token.ShelfEuler);
            SetObject(reveal, "flightBook", token.FlightBook);
            SetObject(reveal, "openBook", token.OpenBook);
            SetObject(reveal, "leftCoverPivot", token.LeftCover);
            SetObject(reveal, "rightCoverPivot", token.RightCover);
            SetObject(reveal, "leftPagesPivot", token.LeftPages);
            SetObject(reveal, "rightPagesPivot", token.RightPages);
            SetObject(reveal, "impactRing", token.ImpactRing);
            SetObject(reveal, "projectorEffects", token.ProjectorEffects);
            SetObject(reveal, "hologramCanvas", lobbyUi);
            SetObject(reveal, "hologramCanvasGroup", canvasGroup);
            SetObject(reveal, "projectorLight", token.ProjectorLight);
            SetObject(reveal, "interaction", interaction);
            SetObject(reveal, "interactionTrigger", trigger);
            SetObject(reveal, "panel", panel);
            SetObject(reveal, "lobbyPlayer", playerController);
        }

        private static RoomListItemView MakeRoomItemTemplate(Transform parent)
        {
            var root = MakeImage("Room List Item Template", parent, new Vector2(710f, 72f), Vector2.zero,
                new Color(0.025f, 0.09f, 0.115f, 0.92f));
            var layout = root.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 72f;
            var name = MakeText("Room Name", root, "ROOM NAME", 20,
                new Vector2(410f, 30f), new Vector2(-130f, 13f), Html("#E5FDFF"), TextAnchor.MiddleLeft,
                FontStyle.Bold);
            var code = MakeText("Join Code", root, "Code: ------", 15,
                new Vector2(410f, 24f), new Vector2(-130f, -16f), Html("#72B8C2"), TextAnchor.MiddleLeft);
            var join = MakeButton("Join Button", root, "JOIN", new Vector2(120f, 56f),
                new Vector2(278f, 0f), new Color(0.04f, 0.42f, 0.5f, 1f), Color.white);
            var item = root.gameObject.AddComponent<RoomListItemView>();
            SetObject(item, "roomNameText", name);
            SetObject(item, "joinCodeText", code);
            SetObject(item, "joinButton", join);
            root.gameObject.SetActive(false);
            return item;
        }

        private static InputField MakeInput(string name, Transform parent, string defaultValue,
            Vector2 size, Vector2 position)
        {
            var root = MakeImage(name, parent, size, position, new Color(0.01f, 0.035f, 0.055f, 0.98f));
            Border(root, new Color(0.12f, 0.5f, 0.6f, 0.75f), 3f);
            var text = MakeText("Text", root, defaultValue, 22,
                size - new Vector2(34f, 8f), Vector2.zero, Html("#E7FDFF"), TextAnchor.MiddleLeft);
            var placeholder = MakeText("Placeholder", root, "Room name", 22,
                size - new Vector2(34f, 8f), Vector2.zero, Html("#56858D"), TextAnchor.MiddleLeft,
                FontStyle.Italic);
            var input = root.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = defaultValue;
            input.characterLimit = 28;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Button MakeButton(string name, Transform parent, string label, Vector2 size,
            Vector2 position, Color background, Color foreground)
        {
            var root = MakeImage(name, parent, size, position, background);
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.72f, 0.88f, 0.92f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.35f, 0.45f, 0.48f, 0.6f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            MakeText("Label", root, label, 19, size - new Vector2(12f, 8f), Vector2.zero,
                foreground, TextAnchor.MiddleCenter, FontStyle.Bold);
            return button;
        }

        private static RectTransform MakeRect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var item = new GameObject(name, typeof(RectTransform));
            var rect = item.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static RectTransform MakeImage(string name, Transform parent, Vector2 size,
            Vector2 position, Color color)
        {
            var rect = MakeRect(name, parent, size, position);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        private static Text MakeText(string name, Transform parent, string content, int fontSize,
            Vector2 size, Vector2 position, Color color, TextAnchor alignment,
            FontStyle style = FontStyle.Normal)
        {
            var rect = MakeRect(name, parent, size, position);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = uiFont;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void Border(RectTransform parent, Color color, float thickness)
        {
            var size = parent.sizeDelta;
            MakeImage("Border Top", parent, new Vector2(size.x, thickness), new Vector2(0f, size.y * 0.5f), color);
            MakeImage("Border Bottom", parent, new Vector2(size.x, thickness), new Vector2(0f, -size.y * 0.5f), color);
            MakeImage("Border Left", parent, new Vector2(thickness, size.y), new Vector2(-size.x * 0.5f, 0f), color);
            MakeImage("Border Right", parent, new Vector2(thickness, size.y), new Vector2(size.x * 0.5f, 0f), color);
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static GameObject CreateCube(string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material, Quaternion? localRotation = null)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localRotation = localRotation ?? Quaternion.identity;
            item.transform.localScale = localScale;
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        private static GameObject CreateSphere(string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localScale = localScale;
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        private static GameObject CreateCylinder(string name, Transform parent, Vector3 localPosition,
            float radius, float height, Material material, Quaternion? localRotation = null)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localRotation = localRotation ?? Quaternion.identity;
            item.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            item.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        private static void CreateRing(string name, Transform parent, float radius, int segments, Material material)
        {
            var ring = Child(parent, name);
            var circumference = 2f * Mathf.PI * radius;
            var segmentLength = circumference / segments * 0.56f;
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                CreateCube($"Segment {i + 1:00}", ring, position,
                    new Vector3(segmentLength, 0.007f, 0.014f), material,
                    Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f));
            }
        }

        private static Bounds CalculateBounds(GameObject item)
        {
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException($"Object has no renderer: {item.name}");
            }
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(item => item.name == name)
                ?? throw new InvalidOperationException($"Required root is missing: {name}");
        }

        private static Transform FindTransform(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name)
                ?? throw new InvalidOperationException($"Required object is missing: {name}");
        }

        private static Material Material(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat")
                ?? throw new InvalidOperationException($"Material is missing: {name}");
        }

        private static Material HologramMaterial(string name, string html, float emission)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            var color = Html(html);
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", color * emission);
            material.SetFloat("_Smoothness", 0.34f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material HologramBeamMaterial()
        {
            const string name = "Hologram Beam Soft";
            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? throw new InvalidOperationException("URP Unlit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            var color = Html("#5BD6E7");
            color.a = 0.075f;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static T GetOrAdd<T>(GameObject item) where T : Component
        {
            var component = item.GetComponent<T>();
            return component != null ? component : item.AddComponent<T>();
        }

        private static void SetObject(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector(UnityEngine.Object target, string field, Vector3 value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(UnityEngine.Object target, string field, bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool uiHasActiveCloseButton(Transform root)
        {
            return root.GetComponentsInChildren<Button>(false)
                .Any(button => button.gameObject.name.Contains("Close", StringComparison.OrdinalIgnoreCase));
        }

        private static Color Html(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out var color))
            {
                throw new ArgumentException($"Invalid color: {value}", nameof(value));
            }
            return color;
        }

        private static void Capture(Camera camera, string path)
        {
            const int width = 1280;
            const int height = 720;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
