using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FishSchoolController : MonoBehaviour
{
    [Header("School")]
    [SerializeField, Min(1)] private int fishCount = 150;
    [SerializeField] private GameObject fishPrefab;
    [SerializeField] private Vector2 fishScaleRange = new Vector2(1f, 1.35f);
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Performance")]
    [SerializeField, Range(10f, 60f)] private float simulationRate = 30f;

    [Header("Swimming")]
    [SerializeField, Min(0.01f)] private float minimumSpeed = 1.2f;
    [SerializeField, Min(0.01f)] private float maximumSpeed = 2.8f;
    [SerializeField, Min(0.01f)] private float maximumSteering = 3.5f;
    [SerializeField, Min(0.01f)] private float rotationResponsiveness = 4f;
    [SerializeField, Min(0f)] private float wanderStrength = 0.35f;
    [SerializeField, Min(0f)] private float wanderFrequency = 0.25f;

    [Header("Flocking")]
    [SerializeField, Min(0.01f)] private float neighborDistance = 7f;
    [SerializeField, Min(0.01f)] private float separationDistance = 1.5f;
    [SerializeField, Min(0f)] private float alignmentWeight = 1.15f;
    [SerializeField, Min(0f)] private float cohesionWeight = 0.8f;
    [SerializeField, Min(0f)] private float separationWeight = 2.4f;

    [Header("Volumes (local space)")]
    [SerializeField] private Vector3 swimVolumeCenter = new Vector3(25f, 0f, 20f);
    [SerializeField] private Vector3 swimVolumeSize = new Vector3(85f, 16f, 80f);
    [Tooltip("Interior of the glass player enclosure. Fish may swim above this finite-height volume.")]
    [SerializeField] private Vector3 forbiddenVolumeCenter = new Vector3(58.1f, -3.29f, 34.1f);
    [SerializeField] private Vector3 forbiddenVolumeSize = new Vector3(20f, 10f, 20f);
    [SerializeField, Min(0f)] private float forbiddenSafetyMargin = 2f;
    [SerializeField, Min(0.01f)] private float boundaryLookAhead = 4f;
    [SerializeField, Min(0f)] private float boundaryWeight = 4.5f;
    [SerializeField, Min(0f)] private float fishRadius = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool drawVolumeGizmos;

    private static readonly ProfilerMarker SimulationMarker = new("Meshup.Fish.Simulate");
    private static readonly ProfilerMarker PresentationMarker = new("Meshup.Fish.Present");
    private Transform[] fish;
    private Vector3[] positions;
    private Vector3[] previousPositions;
    private Quaternion[] rotations;
    private Quaternion[] previousRotations;
    // Each occupied cell points to a linked list stored in nextInCell; no per-tick lists.
    private Dictionary<Vector3Int, int> cellHeads;
    private int[] nextInCell;
    private float accumulatedTime;
    private float simulationTime;
    private Vector3[] velocities;
    private Vector3[] nextVelocities;
    private Vector3[] wanderAxes;
    private float[] wanderPhases;

    private void Awake()
    {
        if (fishPrefab == null)
        {
            Debug.LogError("Fish School Controller needs a fish prefab.", this);
            enabled = false;
            return;
        }

        if (fishPrefab.GetComponent<FishSchoolController>() != null || fishPrefab.GetComponent<NVBoids>() != null)
        {
            Debug.LogError("The fish prefab must be a fish model, not another flock controller.", this);
            enabled = false;
            return;
        }

        SpawnSchool();
    }

    private void SpawnSchool()
    {
        fish = new Transform[fishCount];
        positions = new Vector3[fishCount];
        previousPositions = new Vector3[fishCount];
        rotations = new Quaternion[fishCount];
        previousRotations = new Quaternion[fishCount];
        nextInCell = new int[fishCount];
        cellHeads = new Dictionary<Vector3Int, int>(fishCount);
        velocities = new Vector3[fishCount];
        nextVelocities = new Vector3[fishCount];
        wanderAxes = new Vector3[fishCount];
        wanderPhases = new float[fishCount];

        Bounds swimBounds = GetSwimBounds();
        Bounds exclusionBounds = GetForbiddenBounds();

        for (int i = 0; i < fishCount; i++)
        {
            GameObject instance = Instantiate(fishPrefab, transform);
            instance.name = $"Fish {i + 1:000}";
            fish[i] = instance.transform;
            positions[i] = previousPositions[i] = FindSpawnPosition(swimBounds, exclusionBounds);
            fish[i].localPosition = positions[i];
            // Decorative animation must not keep every offscreen school member updating.
            foreach (var animation in instance.GetComponentsInChildren<Animation>(true))
                animation.cullingType = AnimationCullingType.BasedOnRenderers;
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = false;
                // Leave room for the tail animation when Unity uses fixed culling bounds.
                var bounds = renderer.localBounds;
                bounds.Expand(bounds.size);
                renderer.localBounds = bounds;
            }
            fish[i].localScale = Vector3.one * Random.Range(fishScaleRange.x, fishScaleRange.y);

            Vector3 direction = Random.onUnitSphere;
            direction.y *= 0.35f;
            direction.Normalize();
            velocities[i] = direction * Random.Range(minimumSpeed, maximumSpeed);
            nextVelocities[i] = velocities[i];
            wanderAxes[i] = Random.onUnitSphere;
            wanderPhases[i] = Random.Range(0f, Mathf.PI * 2f);
            rotations[i] = previousRotations[i] = RotationForVelocity(velocities[i]);
            fish[i].localRotation = rotations[i];
        }
    }

    private void Update()
    {
        if (fish == null || fish.Length == 0)
        {
            return;
        }

        Advance(Time.deltaTime);
    }

    private void Advance(float deltaTime)
    {
        float step = 1f / Mathf.Clamp(simulationRate, 10f, 60f);
        // Cap catch-up after stalls; decorative fish must never create a spiral of work.
        accumulatedTime = Mathf.Min(accumulatedTime + Mathf.Max(0f, deltaTime), step * 4f);
        while (accumulatedTime >= step)
        {
            using (SimulationMarker.Auto()) Simulate(step);
            accumulatedTime -= step;
        }

        using (PresentationMarker.Auto())
        {
            float alpha = accumulatedTime / step;
            Bounds exclusionBounds = GetForbiddenBounds();
            for (int i = 0; i < fish.Length; i++)
            {
                Vector3 position = Vector3.Lerp(previousPositions[i], positions[i], alpha);
                // Interpolation near a corner must not cut through the player enclosure.
                Vector3 ignoredVelocity = Vector3.zero;
                position = PushOutsideForbiddenVolume(position, exclusionBounds, ref ignoredVelocity);
                fish[i].SetLocalPositionAndRotation(position,
                    Quaternion.Slerp(previousRotations[i], rotations[i], alpha));
            }
        }
    }

    private Vector3Int CellFor(Vector3 position)
    {
        float size = Mathf.Max(0.01f, neighborDistance);
        return new Vector3Int(Mathf.FloorToInt(position.x / size),
            Mathf.FloorToInt(position.y / size), Mathf.FloorToInt(position.z / size));
    }

    private void Simulate(float deltaTime)
    {
        simulationTime += deltaTime;
        cellHeads.Clear();
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3Int cell = CellFor(positions[i]);
            nextInCell[i] = cellHeads.TryGetValue(cell, out int head) ? head : -1;
            cellHeads[cell] = i;
            previousPositions[i] = positions[i];
            previousRotations[i] = rotations[i];
        }
        Bounds swimBounds = GetSwimBounds();
        Bounds exclusionBounds = GetForbiddenBounds();
        float neighborDistanceSquared = neighborDistance * neighborDistance;
        float separationDistanceSquared = separationDistance * separationDistance;

        for (int i = 0; i < fish.Length; i++)
        {
            Vector3 position = positions[i];
            Vector3 averageVelocity = Vector3.zero;
            Vector3 averagePosition = Vector3.zero;
            Vector3 separation = Vector3.zero;
            int neighbors = 0;

            Vector3Int cell = CellFor(position);
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (!cellHeads.TryGetValue(cell + new Vector3Int(x, y, z), out int head))
                    continue;
                for (int j = head; j >= 0; j = nextInCell[j])
                {
                    if (i == j) continue;
                    Vector3 offset = positions[j] - position;
                    float distanceSquared = offset.sqrMagnitude;
                    if (distanceSquared > neighborDistanceSquared) continue;
                    averageVelocity += velocities[j];
                    averagePosition += positions[j];
                    neighbors++;
                    if (distanceSquared < separationDistanceSquared && distanceSquared > 0.0001f)
                        separation -= offset / distanceSquared;
                }
            }

            Vector3 steering = Vector3.zero;
            if (neighbors > 0)
            {
                averageVelocity /= neighbors;
                averagePosition /= neighbors;
                steering += SteerTowards(averageVelocity, velocities[i]) * alignmentWeight;
                steering += SteerTowards(averagePosition - position, velocities[i]) * cohesionWeight;
                steering += SteerTowards(separation, velocities[i]) * separationWeight;
            }

            float wanderTime = simulationTime * wanderFrequency + wanderPhases[i];
            Vector3 wander = new Vector3(
                Mathf.Sin(wanderTime),
                Mathf.Sin(wanderTime * 0.73f + wanderAxes[i].x) * 0.35f,
                Mathf.Cos(wanderTime * 0.91f + wanderAxes[i].z));
            steering += wander.normalized * wanderStrength;
            steering += GetBoundarySteering(position, velocities[i], swimBounds, exclusionBounds) * boundaryWeight;
            steering = Vector3.ClampMagnitude(steering, maximumSteering);

            Vector3 candidateVelocity = velocities[i] + steering * deltaTime;
            if (candidateVelocity.sqrMagnitude < 0.0001f)
            {
                candidateVelocity = velocities[i].sqrMagnitude > 0.0001f ? velocities[i] : Vector3.forward;
            }

            float speed = Mathf.Clamp(candidateVelocity.magnitude, minimumSpeed, maximumSpeed);
            nextVelocities[i] = candidateVelocity.normalized * speed;
        }

        for (int i = 0; i < fish.Length; i++)
        {
            velocities[i] = nextVelocities[i];
            Vector3 position = positions[i] + velocities[i] * deltaTime;
            position = ConstrainToSwimVolume(position, swimBounds, ref velocities[i]);
            position = PushOutsideForbiddenVolume(position, exclusionBounds, ref velocities[i]);
            positions[i] = position;

            Quaternion targetRotation = RotationForVelocity(velocities[i]);
            rotations[i] = Quaternion.Slerp(
                rotations[i],
                targetRotation,
                1f - Mathf.Exp(-rotationResponsiveness * deltaTime));
        }
    }

    private Vector3 GetBoundarySteering(Vector3 position, Vector3 velocity, Bounds swimBounds, Bounds exclusionBounds)
    {
        Vector3 predictedPosition = position + velocity.normalized * boundaryLookAhead;
        Vector3 steering = Vector3.zero;

        Vector3 closestInsideSwim = swimBounds.ClosestPoint(predictedPosition);
        if ((closestInsideSwim - predictedPosition).sqrMagnitude > 0.0001f)
        {
            steering += (closestInsideSwim - predictedPosition).normalized;
        }

        Vector3 closestOnExclusion = exclusionBounds.ClosestPoint(predictedPosition);
        if (exclusionBounds.Contains(predictedPosition))
        {
            steering += NearestExitNormal(predictedPosition, exclusionBounds) * 2f;
        }
        else
        {
            Vector3 away = predictedPosition - closestOnExclusion;
            float distance = away.magnitude;
            if (distance < boundaryLookAhead && distance > 0.0001f)
            {
                steering += away.normalized * (1f - distance / boundaryLookAhead);
            }
        }

        return steering;
    }

    private Vector3 ConstrainToSwimVolume(Vector3 position, Bounds bounds, ref Vector3 velocity)
    {
        Vector3 minimum = bounds.min + Vector3.one * fishRadius;
        Vector3 maximum = bounds.max - Vector3.one * fishRadius;

        if (position.x < minimum.x || position.x > maximum.x)
        {
            position.x = Mathf.Clamp(position.x, minimum.x, maximum.x);
            velocity.x = position.x <= minimum.x ? Mathf.Abs(velocity.x) : -Mathf.Abs(velocity.x);
        }
        if (position.y < minimum.y || position.y > maximum.y)
        {
            position.y = Mathf.Clamp(position.y, minimum.y, maximum.y);
            velocity.y = position.y <= minimum.y ? Mathf.Abs(velocity.y) : -Mathf.Abs(velocity.y);
        }
        if (position.z < minimum.z || position.z > maximum.z)
        {
            position.z = Mathf.Clamp(position.z, minimum.z, maximum.z);
            velocity.z = position.z <= minimum.z ? Mathf.Abs(velocity.z) : -Mathf.Abs(velocity.z);
        }

        return position;
    }

    private Vector3 PushOutsideForbiddenVolume(Vector3 position, Bounds bounds, ref Vector3 velocity)
    {
        if (!bounds.Contains(position))
        {
            return position;
        }

        Vector3 exitNormal = NearestExitNormal(position, bounds);
        if (Mathf.Abs(exitNormal.x) > 0f)
        {
            position.x = exitNormal.x < 0f ? bounds.min.x : bounds.max.x;
        }
        else if (Mathf.Abs(exitNormal.y) > 0f)
        {
            position.y = exitNormal.y < 0f ? bounds.min.y : bounds.max.y;
        }
        else
        {
            position.z = exitNormal.z < 0f ? bounds.min.z : bounds.max.z;
        }

        float inwardSpeed = Vector3.Dot(velocity, -exitNormal);
        if (inwardSpeed > 0f)
        {
            velocity += exitNormal * inwardSpeed * 1.5f;
        }

        return position + exitNormal * 0.001f;
    }

    private Vector3 FindSpawnPosition(Bounds swimBounds, Bounds exclusionBounds)
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(swimBounds.min.x + fishRadius, swimBounds.max.x - fishRadius),
                Random.Range(swimBounds.min.y + fishRadius, swimBounds.max.y - fishRadius),
                Random.Range(swimBounds.min.z + fishRadius, swimBounds.max.z - fishRadius));
            if (!exclusionBounds.Contains(candidate))
            {
                return candidate;
            }
        }

        return swimBounds.min + Vector3.one * fishRadius;
    }

    private Vector3 SteerTowards(Vector3 desiredDirection, Vector3 currentVelocity)
    {
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        return desiredDirection.normalized * maximumSpeed - currentVelocity;
    }

    private Quaternion RotationForVelocity(Vector3 velocity)
    {
        Quaternion swimmingRotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
        return swimmingRotation * Quaternion.Euler(modelRotationOffset);
    }

    private Bounds GetSwimBounds()
    {
        return new Bounds(swimVolumeCenter, PositiveSize(swimVolumeSize));
    }

    private Bounds GetForbiddenBounds()
    {
        Bounds bounds = new Bounds(forbiddenVolumeCenter, PositiveSize(forbiddenVolumeSize));
        bounds.Expand((forbiddenSafetyMargin + fishRadius) * 2f);
        return bounds;
    }

    private static Vector3 NearestExitNormal(Vector3 position, Bounds bounds)
    {
        float left = position.x - bounds.min.x;
        float right = bounds.max.x - position.x;
        float bottom = position.y - bounds.min.y;
        float top = bounds.max.y - position.y;
        float back = position.z - bounds.min.z;
        float front = bounds.max.z - position.z;

        float nearest = left;
        Vector3 normal = Vector3.left;
        if (right < nearest) { nearest = right; normal = Vector3.right; }
        if (bottom < nearest) { nearest = bottom; normal = Vector3.down; }
        if (top < nearest) { nearest = top; normal = Vector3.up; }
        if (back < nearest) { nearest = back; normal = Vector3.back; }
        if (front < nearest) { normal = Vector3.forward; }
        return normal;
    }

    private static Vector3 PositiveSize(Vector3 size)
    {
        return new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), Mathf.Max(0.01f, size.z));
    }

    private void OnValidate()
    {
        fishCount = Mathf.Max(1, fishCount);
        maximumSpeed = Mathf.Max(minimumSpeed, maximumSpeed);
        maximumSteering = Mathf.Max(0.01f, maximumSteering);
        neighborDistance = Mathf.Max(0.01f, neighborDistance);
        separationDistance = Mathf.Clamp(separationDistance, 0.01f, neighborDistance);
        fishScaleRange.x = Mathf.Max(0.01f, fishScaleRange.x);
        fishScaleRange.y = Mathf.Max(fishScaleRange.x, fishScaleRange.y);
        swimVolumeSize = PositiveSize(swimVolumeSize);
        forbiddenVolumeSize = PositiveSize(forbiddenVolumeSize);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawVolumeGizmos)
        {
            return;
        }

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireCube(swimVolumeCenter, PositiveSize(swimVolumeSize));
        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.8f);
        Gizmos.DrawWireCube(forbiddenVolumeCenter, PositiveSize(forbiddenVolumeSize) + Vector3.one * forbiddenSafetyMargin * 2f);
        Gizmos.matrix = oldMatrix;
    }
}
