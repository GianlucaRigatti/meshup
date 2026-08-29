using System;
using System.Linq;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupGeneratedObject : MonoBehaviour
    {
        private MeshupGameCoordinator coordinator;
        private string objectId;
        private GltfImport importer;
        private XRGrabInteractable grab;
        private bool held;
        private float nextSendTime;

        public string ObjectId => objectId;

        public async Task<bool> Initialize(MeshupGameCoordinator owner,
            MeshupGeneratedObjectState state)
        {
            coordinator = owner;
            objectId = state.objectId;
            name = $"Generated Object {objectId}";
            ApplyState(state, true);
            importer = new GltfImport();
            try
            {
                if (!await importer.Load(state.url)
                    || !await importer.InstantiateMainSceneAsync(transform))
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MeshUp] GLB load failed: {exception.Message}");
                return false;
            }

            AddInteractionComponents();
            return true;
        }

        public void ApplyState(MeshupGeneratedObjectState state, bool force)
        {
            if (!force && held)
            {
                return;
            }
            transform.SetPositionAndRotation(state.position, state.rotation);
            transform.localScale = state.scale == Vector3.zero
                ? Vector3.one : state.scale;
        }

        private void AddInteractionComponents()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            var bounds = renderers.Length > 0
                ? renderers[0].bounds
                : new Bounds(transform.position, Vector3.one * 0.4f);
            foreach (var renderer in renderers.Skip(1))
            {
                bounds.Encapsulate(renderer.bounds);
            }
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.center = transform.InverseTransformPoint(bounds.center);
            var scale = transform.lossyScale;
            collider.size = new Vector3(
                bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
            var body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            grab = gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.selectEntered.AddListener(HandleGrabbed);
            grab.selectExited.AddListener(HandleReleased);
        }

        private void Update()
        {
            if (grab != null)
            {
                grab.enabled = coordinator != null
                    && coordinator.CanManipulateGeneratedObjectsLocally;
            }
            if (held && Time.unscaledTime >= nextSendTime)
            {
                nextSendTime = Time.unscaledTime + 0.05f;
                coordinator?.SubmitObjectTransform(objectId, transform, false);
            }
        }

        private void HandleGrabbed(SelectEnterEventArgs args)
        {
            if (coordinator == null
                || !coordinator.CanManipulateGeneratedObjectsLocally)
            {
                return;
            }
            held = true;
        }

        private void HandleReleased(SelectExitEventArgs args)
        {
            if (!held)
            {
                return;
            }
            held = false;
            coordinator?.SubmitObjectTransform(objectId, transform, true);
        }

        private void OnDestroy()
        {
            if (grab != null)
            {
                grab.selectEntered.RemoveListener(HandleGrabbed);
                grab.selectExited.RemoveListener(HandleReleased);
            }
            importer?.Dispose();
        }
    }
}
