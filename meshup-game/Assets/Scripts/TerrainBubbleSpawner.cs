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

    [Header("Esclusione Struttura")]
    [Tooltip("Volumi che descrivono le impronte della sala d'attesa, del corridoio e dell'area di gioco.")]
    [SerializeField] private PlayerAreaVolumes playerArea;
    [Min(0f)] public float structureClearance = 10f;

    [Header("Prefab")]
    public ParticleSystem bubblePrefab;

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
        if (bubblePrefab == null)
        {
            Debug.LogError("[TerrainBubbleSpawner] Bubble prefab is not assigned.", this);
            return;
        }

        if (playerArea == null)
        {
            playerArea = FindAnyObjectByType<PlayerAreaVolumes>();
        }

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
            if (playerArea != null
                && playerArea.ContainsFootprint(spawnPos, structureClearance))
            {
                continue;
            }

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

            var psInstance = Instantiate(bubblePrefab);
            psInstance.gameObject.name = "BubbleEmitter_" + spawned;

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
}
