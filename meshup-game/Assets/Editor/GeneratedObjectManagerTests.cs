using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meshup.Game;
using NUnit.Framework;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class GeneratedObjectManagerTests
    {
        private sealed class PendingImport
        {
            public MeshupGeneratedObject Instance;
            public CancellationToken Cancellation;
            public readonly TaskCompletionSource<bool> Completion = new();
        }

        private GameObject anchor;
        private GeneratedObjectManager objects;
        private readonly List<PendingImport> imports = new();
        private readonly List<string> errors = new();

        [SetUp]
        public void SetUp()
        {
            imports.Clear();
            errors.Clear();
            anchor = new GameObject("Generator anchor");
            objects = new GeneratedObjectManager(anchor.transform, Load, errors.Add);
        }

        private Task<bool> Load(MeshupGeneratedObject instance,
            MeshupGeneratedObjectState state, CancellationToken cancellation)
        {
            instance.ApplyState(state, true);
            var pending = new PendingImport
            {
                Instance = instance,
                Cancellation = cancellation
            };
            imports.Add(pending);
            return pending.Completion.Task;
        }

        [TearDown]
        public async Task TearDown()
        {
            objects.Dispose();
            foreach (var pending in imports)
            {
                pending.Completion.TrySetCanceled();
            }
            await Task.Yield();
            UnityEngine.Object.DestroyImmediate(anchor);
        }

        [Test]
        public async Task RepeatedSnapshotsReuseThePendingImportAndKeepLatestTransforms()
        {
            var state = State("object");
            objects.Reconcile(new[] { state });
            objects.Reconcile(new[] { State("object", Vector3.right) });
            Assert.That(imports, Has.Count.EqualTo(1));
            var latest = State("object", new Vector3(3f, 4f, 5f));
            latest.rotation = Quaternion.Euler(0f, 45f, 0f);
            latest.scale = Vector3.one * 2f;
            objects.ApplyTransform(latest);
            imports[0].Completion.SetResult(true);
            await Task.Yield();

            var instance = imports[0].Instance;
            Assert.That(instance.transform.position, Is.EqualTo(latest.position));
            Assert.That(Quaternion.Angle(instance.transform.rotation, latest.rotation),
                Is.LessThan(0.001f));
            Assert.That(instance.transform.localScale, Is.EqualTo(latest.scale));
            Assert.That(instance.gameObject.scene, Is.EqualTo(anchor.scene));
            Assert.That(errors, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ClearingARoundRetiresReadyAndPendingObjectsWithoutLateErrors(
            bool lateResult)
        {
            objects.Add("https://example.test/ready.glb");
            objects.Add("https://example.test/pending.glb");
            objects.Reconcile(objects.States);
            imports[0].Completion.SetResult(true);
            await Task.Yield();

            objects.Clear();
            Assert.That(objects.States, Is.Empty);
            Assert.That(imports[0].Instance == null, Is.True);
            Assert.That(imports[1].Cancellation.IsCancellationRequested, Is.True);
            Assert.That(imports[1].Instance.gameObject.activeSelf, Is.False,
                "A retired model must disappear immediately, even while its import unwinds.");
            imports[1].Completion.SetResult(lateResult);
            await Task.Yield();
            Assert.That(imports[1].Instance == null, Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public async Task LateFailureCannotRemoveAReplacementWithTheSameId()
        {
            objects.Reconcile(new[] { State("same-id") });
            objects.Reconcile(Array.Empty<MeshupGeneratedObjectState>());
            objects.Reconcile(new[] { State("same-id", Vector3.right) });
            Assert.That(imports, Has.Count.EqualTo(2));
            imports[0].Completion.SetResult(false);
            await Task.Yield();

            var replacement = State("same-id", Vector3.up);
            objects.Reconcile(new[] { replacement });
            Assert.That(imports, Has.Count.EqualTo(2),
                "The stale failure must not evict the new entry and trigger a third import.");
            imports[1].Completion.SetResult(true);
            await Task.Yield();
            Assert.That(imports[0].Instance == null, Is.True);
            Assert.That(imports[1].Instance.gameObject.activeSelf, Is.True);
            Assert.That(imports[1].Instance.transform.position, Is.EqualTo(Vector3.up));
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public async Task FailedCurrentImportReportsOnceAndCanRetryOnTheNextSnapshot()
        {
            var state = State("retry");
            objects.Reconcile(new[] { state });
            imports[0].Completion.SetResult(false);
            await Task.Yield();
            Assert.That(errors, Is.EqualTo(new[] { "A generated model could not be loaded." }));
            Assert.That(imports[0].Instance == null, Is.True);

            objects.Reconcile(new[] { state });
            Assert.That(imports, Has.Count.EqualTo(2));
            imports[1].Completion.SetResult(true);
            await Task.Yield();
            objects.Reconcile(new[] { state });
            Assert.That(imports, Has.Count.EqualTo(2));
            Assert.That(errors, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DisposalCancelsPendingImportsAndIgnoresLaterSnapshotsAndErrors()
        {
            var state = State("scene-exit");
            objects.Reconcile(new[] { state });
            objects.Dispose();
            Assert.That(imports[0].Cancellation.IsCancellationRequested, Is.True);
            imports[0].Completion.SetException(new InvalidOperationException("Late import error"));
            await Task.Yield();
            objects.Reconcile(new[] { state });
            objects.ApplyTransform(state);
            Assert.That(imports, Has.Count.EqualTo(1));
            Assert.That(imports[0].Instance == null, Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void HostTransformsPreserveIdentityUrlAndSnapshotOrdering()
        {
            var first = objects.Add("https://example.test/first.glb");
            var second = objects.Add("https://example.test/second.glb");
            var incoming = State(first.objectId, Vector3.right * 4f);
            incoming.url = "https://example.test/untrusted-replacement.glb";
            incoming.rotation = Quaternion.Euler(0f, 90f, 0f);
            incoming.scale = Vector3.one * 3f;

            Assert.That(objects.UpdateHostTransform(incoming), Is.SameAs(first));
            Assert.That(first.url, Is.EqualTo("https://example.test/first.glb"));
            Assert.That(first.position, Is.EqualTo(incoming.position));
            Assert.That(first.rotation, Is.EqualTo(incoming.rotation));
            Assert.That(first.scale, Is.EqualTo(incoming.scale));
            Assert.That(objects.UpdateHostTransform(State("unknown")), Is.Null);
            var match = new MeshupMatchState();
            Assert.That(match.CreateSnapshot(objects.States).generatedObjects,
                Is.EqualTo(new[] { first, second }));
            Assert.That(imports, Is.Empty);
        }

        private static MeshupGeneratedObjectState State(string id, Vector3 position = default)
        {
            return new MeshupGeneratedObjectState
            {
                objectId = id,
                url = "https://example.test/model.glb",
                position = position,
                rotation = Quaternion.identity,
                scale = Vector3.one
            };
        }
    }
}
