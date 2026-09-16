#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Meshup.Lobby
{
    internal sealed class LobbyTokenRuntimeVerifier : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Environment.GetCommandLineArgs().Contains("-verifyLobbyToken"))
            {
                return;
            }
            new GameObject("Lobby Token Runtime Verifier").AddComponent<LobbyTokenRuntimeVerifier>();
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(7.2f);
            try
            {
                var reveal = FindAnyObjectByType<FallingBookReveal>();
                var panel = FindAnyObjectByType<RoomTotemPanel>();
                var interaction = FindAnyObjectByType<RoomTotemInteraction>();
                var canvas = FindAnyObjectByType<HologramBillboard>();
                var player = FindAnyObjectByType<LobbyFirstPersonController>(
                    FindObjectsInactive.Include);
                var camera = Camera.main;
                var dualModeRig = GameObject.Find("Lobby Dual Mode Controls");
                if (reveal == null || panel == null || interaction == null || canvas == null
                    || player == null || camera == null || dualModeRig == null)
                {
                    throw new InvalidOperationException("A runtime hologram component is missing.");
                }
                if (!reveal.IsRevealed || !panel.IsOpen)
                {
                    throw new InvalidOperationException("The book did not reveal and open the archive automatically.");
                }
                if (string.IsNullOrWhiteSpace(panel.GeneratedRoomName)
                    || panel.GeneratedRoomName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 2)
                {
                    throw new InvalidOperationException(
                        "The lobby did not provide a read-only generated room name.");
                }
                if (interaction.InteractionAvailable)
                {
                    throw new InvalidOperationException("The legacy E interaction was unexpectedly enabled.");
                }
                panel.Close();
                if (!panel.IsOpen || player.gameObject.activeSelf)
                {
                    throw new InvalidOperationException(
                        "The persistent panel closed or the legacy desktop controller became active.");
                }
                if (Vector3.Distance(reveal.transform.position, new Vector3(0.16f, 0.085f, -0.18f)) > 0.03f)
                {
                    throw new InvalidOperationException("The animated book did not finish at its landing point.");
                }
                var directionToCamera = (camera.transform.position - canvas.transform.position).normalized;
                if (Vector3.Dot(canvas.transform.forward, directionToCamera) > -0.8f)
                {
                    throw new InvalidOperationException("The VR hologram is not facing the player correctly.");
                }

                Capture(camera, "/tmp/meshup-token-runtime.png");
                Debug.Log("Lobby token runtime verification passed: fall, impact, opening, projection, automatic persistent UI.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Capture(Camera camera, string path)
        {
            const int width = 1280;
            const int height = 720;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
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
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                Destroy(renderTexture);
                Destroy(texture);
            }
        }
    }
}
#endif
