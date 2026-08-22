using UnityEngine;

public class TerrainBubbleSpawner : MonoBehaviour
{
    [Header("Area")]
    public Vector2 areaSize = new Vector2(20f, 20f); // larghezza (X) e profondità (Z) area di spawn
    public int spawnCount = 20;
    public float minDistanceBetween = 0.5f; // distanza minima fra emettitori

    [Header("Rilevamento Terreno")]
    public bool preferTerrainAPI = true; // usa Terrain.activeTerrain se presente, altrimenti raycast
    public LayerMask groundLayer = ~0; // layer usato per il raycast (default: tutto)
    public float raycastHeight = 50f; // altezza di partenza per il raycast verso il basso
    public float verticalOffset = 0.05f; // offset sulla normale per evitare clipping nella superficie

    [Header("Prefab")]
    public ParticleSystem bubblePrefab; // se assegnato, verrà instanziato; altrimenti verrà creato uno ps di default

    [Header("Randomizzazione (opzionale)")]
    public Vector2 randomEmitRate = new Vector2(8f, 18f);
    public Vector2 randomStartSize = new Vector2(0.06f, 0.22f);

    private System.Random rng = new System.Random();

    void Start()
    {
        SpawnBubbles();
    }

    public void SpawnBubbles()
    {
        int attempts = 0;
        int spawned = 0;

        Vector3[] positions = new Vector3[spawnCount];

        while (spawned < spawnCount && attempts < spawnCount * 8)
        {
            attempts++;
            // genera posizione casuale nell'area locale
            float rx = (float)(rng.NextDouble() * 2.0 - 1.0) * (areaSize.x * 0.5f);
            float rz = (float)(rng.NextDouble() * 2.0 - 1.0) * (areaSize.y * 0.5f);
            Vector3 worldCheck = transform.position + new Vector3(rx, 0f, rz);

            // trova altezza del terreno o collisione
            bool found = false;
            Vector3 spawnPos = Vector3.zero;

            if (preferTerrainAPI && Terrain.activeTerrain != null)
            {
                Terrain t = Terrain.activeTerrain;
                float h = t.SampleHeight(worldCheck) + t.transform.position.y;
                spawnPos = new Vector3(worldCheck.x, h + verticalOffset, worldCheck.z);
                found = true;
            }
            else
            {
                Ray ray = new Ray(worldCheck + Vector3.up * raycastHeight, Vector3.down);
                if (Physics.Raycast(ray, out RaycastHit hit, raycastHeight * 2f, groundLayer))
                {
                    spawnPos = hit.point + hit.normal * verticalOffset;
                    found = true;
                }
            }

            if (!found) continue;

            // verifica distanza minima dagli altri spawn
            bool ok = true;
            for (int i = 0; i < spawned; i++)
            {
                if ((positions[i] - spawnPos).sqrMagnitude < minDistanceBetween * minDistanceBetween)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;

            // istanzia o crea un ParticleSystem
            ParticleSystem psInstance;
            if (bubblePrefab != null)
            {
                var go = Instantiate(bubblePrefab.gameObject);
                go.name = "BubbleEmitter_" + spawned;
                psInstance = go.GetComponent<ParticleSystem>();
            }
            else
            {
                psInstance = CreateDefaultBubbleSystem();
                psInstance.gameObject.name = "BubbleEmitter_" + spawned;
            }

            // posiziona e non parentare (le bolle salgono nello spazio World)
            psInstance.transform.SetParent(null);
            psInstance.transform.position = spawnPos;

            // randomizza alcuni parametri utili per variare l'effetto
            var main = psInstance.main;
            float size = Random.Range(randomStartSize.x, randomStartSize.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.2f);
            var emission = psInstance.emission;
            float rate = Random.Range(randomEmitRate.x, randomEmitRate.y);
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);

            positions[spawned] = spawnPos;
            spawned++;
        }

        if (spawned == 0)
            Debug.LogWarning("[TerrainBubbleSpawner] Non sono stati trovati punti validi per spawnare bolle.");
    }

    // Crea un ParticleSystem pensato per le bolle (coerente con le protezioni contro l'errore velocity-mode)
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
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.rateOverTime = 12f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.12f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        // impostiamo X/Y/Z nello stesso modo (TwoConstants) per evitare l'errore
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.95f, 0.97f, 1f), 0f),
                new GradientColorKey(new Color(0.95f, 0.97f, 1f), 1f)
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
        curve.AddKey(1f, 1.15f);
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = 1000;

        return ps;
    }
}