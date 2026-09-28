using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class FishSchoolTests
    {
        private GameObject root;
        private GameObject prefab;
        private FishSchoolController school;
        private PlayerAreaVolumes playerArea;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private void Set(string name, object value) => typeof(FishSchoolController).GetField(name, Flags).SetValue(school, value);
        private void SetArea(string name, object value) => typeof(PlayerAreaVolumes).GetField(name, Flags).SetValue(playerArea, value);
        private T Get<T>(string name) => (T)typeof(FishSchoolController).GetField(name, Flags).GetValue(school);
        private void Call(string name, params object[] args) => typeof(FishSchoolController).GetMethod(name, Flags).Invoke(school, args);

        [SetUp] public void Setup()
        {
            root = new GameObject("School test");
            root.SetActive(false);
            playerArea = root.AddComponent<PlayerAreaVolumes>();
            school = root.AddComponent<FishSchoolController>();
            prefab = new GameObject("Fish test");
            Set("fishPrefab", prefab);
            var savedRandom = UnityEngine.Random.state;
            UnityEngine.Random.InitState(1234);
            Call("SpawnSchool");
            UnityEngine.Random.state = savedRandom;
        }
        [TearDown] public void Cleanup()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(prefab);
        }

        [Test] public void GridMatchesBruteForceSteeringAcrossNegativeAndBoundaryCells()
        {
            Set("wanderStrength", 0f); Set("boundaryWeight", 0f);
            Set("swimVolumeCenter", Vector3.zero); Set("swimVolumeSize", Vector3.one * 1000);
            SetArea("forbiddenVolumeCenter", Vector3.one * 1000);
            SetArea("corridorForbiddenVolumeCenter", Vector3.one * 1000);
            SetArea("playingRoomForbiddenVolumeCenter", Vector3.one * 1000);
            var p = Get<Vector3[]>("positions"); var v = Get<Vector3[]>("velocities");
            var rng = new System.Random(42);
            for (int i = 0; i < p.Length; i++)
            {
                p[i] = new Vector3((float)rng.NextDouble()*40-20, (float)rng.NextDouble()*20-10, (float)rng.NextDouble()*40-20);
                v[i] = new Vector3((float)rng.NextDouble()-.5f, (float)rng.NextDouble()-.5f, (float)rng.NextDouble()-.5f).normalized * 2;
            }
            p[0] = new Vector3(-7.001f, 0, 0); p[1] = new Vector3(-6.999f, 0, 0);
            p[2] = Vector3.zero; p[3] = Vector3.right*7; p[4] = p[2];
            var expected = new Vector3[v.Length];
            const float dt = 1f/30;
            for (int i=0;i<p.Length;i++)
            {
                Vector3 av=Vector3.zero, ap=Vector3.zero, sep=Vector3.zero; int n=0;
                for(int j=0;j<p.Length;j++)
                {
                    if(i==j)continue;var offset=p[j]-p[i];float d=offset.sqrMagnitude;
                    if(d>49)continue;av+=v[j];ap+=p[j];n++;
                    if(d<2.25f&&d>.0001f)sep-=offset/d;
                }
                Vector3 steering=Vector3.zero;
                if(n>0) steering=Steer(av/n,v[i])*1.15f+Steer(ap/n-p[i],v[i])*.8f+Steer(sep,v[i])*2.4f;
                Vector3 candidate=v[i]+Vector3.ClampMagnitude(steering,3.5f)*dt;
                expected[i]=candidate.normalized*Mathf.Clamp(candidate.magnitude,1.2f,2.8f);
            }
            Call("Simulate",dt);
            for(int i=0;i<v.Length;i++) Assert.That(Vector3.Distance(v[i],expected[i]),Is.LessThan(.0001f),"Fish "+i);
        }
        private static Vector3 Steer(Vector3 direction, Vector3 velocity) => direction.sqrMagnitude<.0001f ? Vector3.zero : direction.normalized*2.8f-velocity;

        [Test] public void SimulationIsIndependentOfPresentationFrameRate()
        {
            for(int i=0;i<300;i++)Call("Advance",1f/30);
            var expected=(Vector3[])Get<Vector3[]>("positions").Clone();
            Cleanup(); Setup();
            for(int i=0;i<1200;i++)Call("Advance",1f/120);
            var actual=Get<Vector3[]>("positions");
            for(int i=0;i<actual.Length;i++)Assert.That(Vector3.Distance(actual[i],expected[i]),Is.LessThan(.001f));
        }

        [Test] public void FishStayInSwimVolumeAndOutsideEntirePlayerAreaIncludingInterpolatedFrames()
        {
            Bounds swim = new Bounds(new Vector3(25,0,20),new Vector3(85,16,80));
            var exclusions = PlayerAreaBounds();
            for(int frame=0;frame<3600;frame++)
            {
                Call("Advance",1f/120);
                foreach(var fish in Get<Transform[]>("fish"))
                {
                    Assert.That(swim.Contains(fish.localPosition),Is.True);
                    foreach (var exclusion in exclusions)
                        Assert.That(exclusion.Contains(fish.localPosition),Is.False);
                    Assert.That(float.IsNaN(fish.localPosition.x),Is.False);
                }
            }
        }

        [Test] public void FishInsideEachPlayerAreaVolumeAreImmediatelyExpelled()
        {
            var positions = Get<Vector3[]>("positions");
            var velocities = Get<Vector3[]>("velocities");
            var exclusions = PlayerAreaBounds();
            for (int i = 0; i < exclusions.Length; i++)
            {
                positions[i] = exclusions[i].center;
                velocities[i] = Vector3.forward * 2f;
            }

            Call("Simulate", 1f / 30f);

            for (int i = 0; i < exclusions.Length; i++)
                foreach (var exclusion in exclusions)
                    Assert.That(exclusion.Contains(positions[i]), Is.False,
                        $"Fish in player-area volume {i} remained inside an exclusion.");
        }

        private static Bounds[] PlayerAreaBounds()
        {
            var result = new[]
            {
                new Bounds(new Vector3(58.1f,-3.29f,34.1f),new Vector3(20,10,20)),
                new Bounds(new Vector3(36.35f,-3.29f,27.91f),new Vector3(25,10,10)),
                new Bounds(new Vector3(17.35f,-3.29f,24.01f),new Vector3(17,10,21))
            };
            for (int i = 0; i < result.Length; i++) result[i].Expand(4.8f);
            return result;
        }

        [Test] public void PlayerAreaFootprintCoversAllRoomsAtEveryHeightAndFollowsTransform()
        {
            root.transform.SetPositionAndRotation(new Vector3(4f, 7f, -9f),
                Quaternion.Euler(0f, 31f, 0f));
            var centers = new[]
            {
                new Vector3(58.1f, 1000f, 34.1f),
                new Vector3(36.35f, -1000f, 27.91f),
                new Vector3(17.35f, 500f, 24.01f)
            };

            foreach (Vector3 center in centers)
            {
                Vector3 worldCenter = root.transform.TransformPoint(center);
                Assert.That(playerArea.ContainsFootprint(worldCenter), Is.True);
            }

            Vector3 justOutsideWaitingRoom = root.transform.TransformPoint(
                new Vector3(68.2f, -3.29f, 34.1f));
            Assert.That(playerArea.ContainsFootprint(justOutsideWaitingRoom), Is.False);
            Assert.That(playerArea.ContainsFootprint(justOutsideWaitingRoom, 0.2f), Is.True);
        }

        [Test] public void ImportedFishUsesCulledAnimationAndBoundsContainSwimmingClip()
        {
            Cleanup();
            root = new GameObject("Animated school test");
            root.SetActive(false);
            playerArea = root.AddComponent<PlayerAreaVolumes>();
            school = root.AddComponent<FishSchoolController>();
            prefab = null;
            Set("fishCount", 1);
            Set("fishPrefab", UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/EXTRA_Resources/LowPolyFish/animated_low_poly_fish.glb"));
            Call("SpawnSchool");
            var fish = Get<Transform[]>("fish")[0];
            var animation = fish.GetComponentInChildren<Animation>(true);
            var renderer = fish.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(animation, Is.Not.Null);
            Assert.That(renderer.updateWhenOffscreen, Is.False);
            Assert.That(animation.cullingType, Is.EqualTo(AnimationCullingType.BasedOnRenderers));
            var mesh = new Mesh();
            try
            {
                foreach (AnimationState state in animation)
                {
                    for (int frame = 0; frame <= 120; frame++)
                    {
                        state.clip.SampleAnimation(animation.gameObject, state.length * frame / 120f);
                        renderer.BakeMesh(mesh);
                        var bounds = renderer.localBounds;
                        bounds.Expand(0.001f);
                        foreach (var vertex in mesh.vertices)
                            Assert.That(bounds.Contains(vertex), Is.True,
                                "Animation exceeds fixed culling bounds at frame " + frame);
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test] public void SteadyStateSimulationAndPresentationAllocateNoManagedMemory()
        {
            var advance = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), school,
                typeof(FishSchoolController).GetMethod("Advance", Flags));
            for (int i = 0; i < 240; i++) advance(1f / 120f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) advance(1f / 120f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test] public void LongStallHasBoundedCatchUp()
        {
            Call("Advance",60f);
            Assert.That(Get<float>("simulationTime"),Is.LessThanOrEqualTo(4f/30+.00001f));
        }
    }
}
