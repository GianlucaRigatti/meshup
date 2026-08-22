using UnityEngine;

public class BubbleSpawner : MonoBehaviour
{
    [Header("Target")]
    public Transform followTarget;                  // Assegna il transform del giocatore (opzionale)
    public CharacterController targetController;    // Se assegnato, usato per la velocit� (opzionale)
    public Rigidbody targetRigidbody;               // Se assegnato, usato per la velocit� (opzionale)
    public bool followLocalOffset = true;           // Se true usa localSpawnOffset relativo al target

    [Header("Prefab (opzionale)")]
    public ParticleSystem bubblePrefab; // Se assegnato verr� instanziato, altrimenti viene creato un sistema di default

    [Header("Impostazioni Emissione")]
    public float emitRate = 12f;                // bolle al secondo quando in movimento
    public Vector3 localSpawnOffset = new Vector3(0f, -0.4f, 0.6f);
    public bool emitWhenMovingOnly = true;
    public float minSpeedToEmit = 0.1f;         // soglia velocit� per emettere

    private ParticleSystem psInstance;
    private float currentRate = 0f;
    private Vector3 prevTargetPosition;

    void Start()
    {
        if (bubblePrefab != null)
        {
            var go = Instantiate(bubblePrefab.gameObject);
            go.name = "Bubbles_Instance";
            psInstance = go.GetComponent<ParticleSystem>();
        }
        else
        {
            psInstance = CreateDefaultBubbleSystem();
        }

        // Non parentiamo il sistema: lo posizioniamo ogni frame
        psInstance.transform.SetParent(null);

        // Inizializzazione prev position
        prevTargetPosition = (followTarget != null) ? followTarget.position : transform.position;

        // Inizio senza emissione; verr� controllata in Update
        SetEmissionRate(0f);
    }

    void Update()
    {
        if (psInstance == null) return;

        Vector3 basePos = (followTarget != null) ? (followLocalOffset ? followTarget.TransformPoint(localSpawnOffset) : followTarget.position + localSpawnOffset) : transform.TransformPoint(localSpawnOffset);
        psInstance.transform.position = basePos;

        // Calcola velocit� del target
        float speed = 0f;
        if (targetController != null)
        {
            speed = targetController.velocity.magnitude;
        }
        else if (targetRigidbody != null)
        {
            speed = targetRigidbody.linearVelocity.magnitude;
        }
        else
        {
            Vector3 currPos = (followTarget != null) ? followTarget.position : transform.position;
            speed = (currPos - prevTargetPosition).magnitude / Mathf.Max(Time.deltaTime, 1e-6f);
            prevTargetPosition = currPos;
        }

        bool shouldEmit = true;
        if (emitWhenMovingOnly)
        {
            shouldEmit = speed > minSpeedToEmit;
        }

        float targetRate = shouldEmit ? emitRate : 0f;
        if (!Mathf.Approximately(currentRate, targetRate))
        {
            SetEmissionRate(targetRate);
        }
    }

    private void SetEmissionRate(float rate)
    {
        var emission = psInstance.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);
        currentRate = rate;
    }

    private ParticleSystem CreateDefaultBubbleSystem()
    {
        var go = new GameObject("Bubbles_Default");
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.24f);
        main.startColor = Color.white;
        main.gravityModifier = -0.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = emitRate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.15f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        // IMPORTANT: impostiamo esplicitamente X/Y/Z nello stesso modo (TwoConstants) per evitare l'errore
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);   // TwoConstants (0..0)
        vel.y = new ParticleSystem.MinMaxCurve(0.3f, 1.4f); // TwoConstants (min..max)
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);   // TwoConstants (0..0)

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.9f, 0.95f, 1f), 0f),
                new GradientColorKey(new Color(0.9f, 0.95f, 1f), 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.0f, 0f),
                new GradientAlphaKey(0.9f, 0.08f),
                new GradientAlphaKey(0.0f, 1f)
            }
        );
        col.color = grad;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0f, 0f);
        curve.AddKey(0.05f, 1f);
        curve.AddKey(1f, 1.2f);
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = 1000;

        return ps;
    }
}