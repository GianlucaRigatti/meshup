using System.Threading;
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
        private float normalizationScale = 1f;

        public string ObjectId => objectId;

        public async Task<bool> Initialize(MeshupGameCoordinator owner,
            MeshupGeneratedObjectState state, CancellationToken cancellationToken)
        {
            coordinator = owner;
            objectId = state.objectId;
            name = $"Generated Object {objectId}";
            ApplyState(state, true);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, destroyCancellationToken);
            var token = lifetime.Token;
            var loadingImporter = new GltfImport();
            try
            {
                token.ThrowIfCancellationRequested();
                if (!await loadingImporter.Load(state.url, cancellationToken: token))
                {
                    return false;
                }
                token.ThrowIfCancellationRequested();
                if (this == null || !await loadingImporter.InstantiateMainSceneAsync(
                    transform, token))
                {
                    return false;
                }
                token.ThrowIfCancellationRequested();
                if (this == null)
                {
                    return false;
                }

                var localBounds = CalculateLocalRendererBounds();
                if (localBounds.HasValue)
                {
                    CenterImportedContent(localBounds.Value.center);
                    normalizationScale = CalculateNormalizationScale(
                        GeneratedObjectSizes.TargetMaxWidth(state.size),
                        GeneratedObjectSizes.TargetMaxHeight(state.size),
                        localBounds.Value.size);
                }
                ApplyState(state, true);
                AddInteractionComponentsForBounds(localBounds);
                importer = loadingImporter;
                return true;
            }
            finally
            {
                // OnDestroy owns completed imports. An in-flight import keeps
                // its resources until its async work has stopped using them.
                if (importer != loadingImporter)
                {
                    loadingImporter.Dispose();
                }
            }
        }

        public void ApplyState(MeshupGeneratedObjectState state, bool force)
        {
            if (!force && held)
            {
                return;
            }
            transform.SetPositionAndRotation(state.position, state.rotation);
            var relativeScale = state.scale == Vector3.zero
                ? Vector3.one : state.scale;
            transform.localScale = relativeScale * normalizationScale;
        }

        public static float CalculateNormalizationScale(float maxWidth,
            float maxHeight, Vector3 sourceSize)
        {
            if (!float.IsFinite(maxWidth) || maxWidth <= 0f
                || !float.IsFinite(maxHeight) || maxHeight <= 0f
                || !float.IsFinite(sourceSize.x)
                || !float.IsFinite(sourceSize.y)
                || !float.IsFinite(sourceSize.z))
            {
                return 1f;
            }

            var sourceWidth = Mathf.Max(sourceSize.x, sourceSize.z);
            var widthScale = sourceWidth > 0.0001f
                ? maxWidth / sourceWidth : float.PositiveInfinity;
            var heightScale = sourceSize.y > 0.0001f
                ? maxHeight / sourceSize.y : float.PositiveInfinity;
            var scale = Mathf.Min(widthScale, heightScale);
            return float.IsFinite(scale) ? scale : 1f;
        }

        private Bounds? CalculateLocalRendererBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return null;
            }
            var hasPoint = false;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                var local = renderer.localBounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3(x, y, z));
                    var point = transform.InverseTransformPoint(
                        renderer.transform.TransformPoint(corner));
                    if (!hasPoint)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }
            return hasPoint ? bounds : null;
        }

        private void CenterImportedContent(Vector3 localCenter)
        {
            foreach (Transform child in transform)
            {
                child.localPosition -= localCenter;
            }
        }

        private void AddInteractionComponentsForBounds(Bounds? importedBounds)
        {
            var bounds = importedBounds ?? new Bounds(Vector3.zero,
                Vector3.one * 0.4f);
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = bounds.size;
            var body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            grab = gameObject.AddComponent<XRGrabInteractable>();
            // Match the controller's pose at the moment of each grab so the
            // object keeps its placed orientation, then follows hand rotation.
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true;
            grab.matchAttachRotation = true;
            grab.trackRotation = true;
            // Kinematic grab motion follows the controller even when a static
            // collider is in the way. Velocity tracking temporarily makes this
            // body dynamic while held, allowing floors and walls to resolve the
            // contact instead of letting the object pass through them.
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            // Velocity tracking temporarily makes the body dynamic while held,
            // then restores this authored kinematic state before XR's detach
            // step. Throw velocity cannot be applied to that kinematic body and
            // would also move it after our final synchronized transform.
            grab.throwOnDetach = false;
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
                coordinator?.SubmitObjectTransform(objectId, transform.position,
                    transform.rotation, RelativeScale, false);
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
            coordinator?.SubmitObjectTransform(objectId, transform.position,
                transform.rotation, RelativeScale, true);
        }

        private Vector3 RelativeScale => normalizationScale > 0.0001f
            ? transform.localScale / normalizationScale : transform.localScale;

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
