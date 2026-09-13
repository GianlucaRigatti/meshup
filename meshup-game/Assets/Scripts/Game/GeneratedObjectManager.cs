using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.Game
{
    /// <summary>
    /// Owns generated-object state, local instances, and their pending imports.
    /// Match permissions and network messages remain with the coordinator.
    /// </summary>
    public sealed class GeneratedObjectManager : IDisposable
    {
        private const float SpawnHeight = 2.5f;

        private sealed class Entry
        {
            public MeshupGeneratedObject Instance;
            public readonly CancellationTokenSource Cancellation = new();
            public bool Loading = true;
            public bool Removed;
        }

        private readonly Transform anchor;
        private readonly Func<MeshupGeneratedObject, MeshupGeneratedObjectState,
            CancellationToken, Task<bool>> load;
        private readonly Action<string> reportError;
        private readonly List<MeshupGeneratedObjectState> states = new();
        private readonly Dictionary<string, Entry> instances = new(StringComparer.Ordinal);
        private bool disposed;

        public IReadOnlyList<MeshupGeneratedObjectState> States => states;

        public GeneratedObjectManager(Transform anchor,
            Func<MeshupGeneratedObject, MeshupGeneratedObjectState,
                CancellationToken, Task<bool>> load,
            Action<string> reportError)
        {
            this.anchor = anchor;
            this.load = load ?? throw new ArgumentNullException(nameof(load));
            this.reportError = reportError;
        }

        public MeshupGeneratedObjectState Add(string url,
            GeneratedObjectSize size = GeneratedObjectSize.Medium)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GeneratedObjectManager));
            }
            var state = new MeshupGeneratedObjectState
            {
                objectId = Guid.NewGuid().ToString("N"),
                url = url,
                size = GeneratedObjectSizes.Normalize(size),
                // The authored generator's local up points sideways. Lift in
                // world space and keep every object on its center line.
                position = anchor.position + Vector3.up * SpawnHeight,
                rotation = Quaternion.identity,
                scale = Vector3.one
            };
            states.Add(state);
            return state;
        }

        public MeshupGeneratedObjectState UpdateHostTransform(
            MeshupGeneratedObjectState incoming)
        {
            var state = states.FirstOrDefault(item => item.objectId == incoming.objectId);
            if (state != null)
            {
                state.position = incoming.position;
                state.rotation = incoming.rotation;
                state.scale = incoming.scale;
            }
            return state;
        }

        public void ApplyTransform(MeshupGeneratedObjectState state)
        {
            if (state != null && instances.TryGetValue(state.objectId, out var entry)
                && entry.Instance != null)
            {
                entry.Instance.ApplyState(state, false);
            }
        }

        public void Reconcile(IReadOnlyList<MeshupGeneratedObjectState> snapshot)
        {
            if (disposed)
            {
                return;
            }
            snapshot ??= Array.Empty<MeshupGeneratedObjectState>();
            var expected = new HashSet<string>(snapshot.Select(item => item.objectId),
                StringComparer.Ordinal);
            foreach (var pair in instances.ToArray())
            {
                if (!expected.Contains(pair.Key) || pair.Value.Instance == null)
                {
                    Remove(pair.Key, pair.Value);
                }
            }
            foreach (var state in snapshot)
            {
                if (instances.TryGetValue(state.objectId, out var entry))
                {
                    entry.Instance.ApplyState(state, false);
                    continue;
                }
                var root = new GameObject($"Generated Object {state.objectId}");
                SceneManager.MoveGameObjectToScene(root, anchor.gameObject.scene);
                entry = new Entry { Instance = root.AddComponent<MeshupGeneratedObject>() };
                instances.Add(state.objectId, entry);
                _ = LoadAsync(state, entry);
            }
        }

        public void Clear()
        {
            states.Clear();
            foreach (var pair in instances.ToArray())
            {
                Remove(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            disposed = true;
            Clear();
        }

        private async Task LoadAsync(MeshupGeneratedObjectState state, Entry entry)
        {
            try
            {
                var loaded = false;
                Exception failure = null;
                try
                {
                    loaded = await load(entry.Instance, state, entry.Cancellation.Token);
                }
                catch (OperationCanceledException) when (
                    entry.Cancellation.IsCancellationRequested || entry.Instance == null)
                {
                    // Round cleanup and scene teardown are normal cancellation.
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                // An old completion must never remove a replacement with the
                // same ID, or show an error after the round has been cleared.
                if (entry.Removed)
                {
                    return;
                }
                if (entry.Instance == null)
                {
                    Remove(state.objectId, entry);
                    return;
                }
                if (!loaded)
                {
                    Remove(state.objectId, entry);
                    if (failure != null)
                    {
                        Debug.LogWarning($"[MeshUp] GLB load failed: {failure.Message}");
                    }
                    reportError?.Invoke("A generated model could not be loaded.");
                }
            }
            finally
            {
                entry.Loading = false;
                if (entry.Removed)
                {
                    DestroyEntry(entry);
                }
            }
        }

        private void Remove(string id, Entry entry)
        {
            instances.Remove(id);
            entry.Removed = true;
            if (entry.Instance != null)
            {
                entry.Instance.gameObject.SetActive(false);
            }
            // Keep the inactive import target alive until glTFast unwinds.
            // Cancellation may complete the task synchronously.
            var wasLoading = entry.Loading;
            entry.Cancellation.Cancel();
            if (!wasLoading)
            {
                DestroyEntry(entry);
            }
        }

        private static void DestroyEntry(Entry entry)
        {
            entry.Cancellation.Dispose();
            if (entry.Instance == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(entry.Instance.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(entry.Instance.gameObject);
            }
        }
    }
}
