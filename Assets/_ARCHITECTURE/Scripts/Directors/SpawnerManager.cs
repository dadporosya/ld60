using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using M = System.Math;
using Unity.Mathematics.Geometry;

public class SpawnerManager : MonoBehaviour
{
    public ObjectsKindsContainer rawPrefabs;
    [SerializeField] private bool initByRawPrefabs=true;
    public ObjectsWithDistributionContainer prefabs;
    public Transform spawnPoint;
    public GameObject spawnMap;
    public float spawnRate;
    [Range(0f, 1f)]
    public float spawnRateDist;
    [Range(0f, 1f)]
    public float additionalUnitChance;

    private float radius;
    private CircleCollider2D mapCollider;
    private Coroutine spawningCoroutine;

    public Transform targetParent;
    public string parentTag;

    public enum SpawnPositions { outside, inside };
    public SpawnPositions spawnPosition;
    public bool cameraCollision;

    public bool isDelayBeforeSpawn=false;
    public float delayBeforeSpawn=0f;

    public bool _off;
    public bool off
    {
        get { return _off; }
        set
        {
            _off = value;
            CheckSpawning();
        }
    }

    void Start()
    {
        if (initByRawPrefabs && rawPrefabs)
        {
            prefabs = rawPrefabs.ConvertToDistributionContainer();
        }
        if (!spawnMap) spawnMap = GameObject.FindGameObjectWithTag("Map");
        mapCollider = spawnMap.GetComponent<CircleCollider2D>();
        radius = mapCollider.radius * mapCollider.transform.localScale.x;
        if (!targetParent) targetParent = GameObject.FindGameObjectWithTag(parentTag).transform;
        CheckSpawning();
    }

    void Update()
    {
        // Toggle spawning on/off when Space is pressed
        // if (Input.GetKeyDown(KeyCode.F))
        // {
        //     SpawnPrefab();
        // }
    }

    public void CheckSpawning()
    {
        if (!off) StartSpawning();
        else if (spawningCoroutine != null) StopCoroutine(spawningCoroutine);
    }

    public void StartSpawning()
    {
        spawningCoroutine = StartCoroutine(SpawnCoroutine());
    }

    IEnumerator SpawnCoroutine()
    {   
        if (isDelayBeforeSpawn) yield return new WaitForSeconds(Mathf.Abs(delayBeforeSpawn + spawnRate * Random.Range(-1, 1)));
        int n ;
        while (true)
        {
            // yield return new WaitForSeconds(spawnRate * Random.Range(1-spawnRateDist, 1+spawnRateDist));
            yield return new WaitForSeconds(spawnRate*3);
            while (GameFlowManager.Instance.IsPaused())
            {
                yield return new WaitForEndOfFrame();
            }
            
            n = additionalUnitChance == 0 ? 0 : (int)M.Log(Random.value, additionalUnitChance);
            for (int i = 0; i<n+1; i++)
            {
                SpawnPrefab();
            }
            yield return null;
        }
    }

    public GameObject SpawnPrefab(GameObject prefab=null, bool spawnInTheMap=true){
        if (!prefab){
            prefab = h.RandChoice(prefabs.objects.Keys.ToList());
        }
        if (!spawnInTheMap) return SpawnAtSpawnPoint(prefab);
        if (spawnPosition == SpawnPositions.outside) return SpawnOutsideMap(prefab);
        if (spawnPosition == SpawnPositions.inside) return SpawnInsideMap(prefab, cameraCollision);
        return default;
    }

    GameObject SpawnAtSpawnPoint(GameObject prefab)
    {
        Vector3 position = spawnPoint ? spawnPoint.position : transform.position;
        return Instantiate(prefab, position, Quaternion.identity, targetParent);
    }

    GameObject SpawnOutsideMap(GameObject prefab)
    {
        Vector2 dir = h.RandomDirection();
        float hyp = h.GetSpriteHypotenuse(prefab);

        float newX = (radius + hyp) * dir.x;
        float newY = (radius + hyp) * dir.y;
        Vector3 newPosition = new Vector3(newX, newY, 0);

        return Instantiate(prefab, newPosition, Quaternion.identity, targetParent);
    }

    GameObject SpawnInsideMap(GameObject prefab, bool checkCameraCollision=false)
    {
        Vector3 newPosition = h.RandomPositionInCircle(radius);

        if (checkCameraCollision && h.CheckInsideCamera(newPosition))
        {
            float hyp = h.GetSpriteHypotenuse(prefab);
            newPosition = GetClosestPointOutsideCamera(newPosition, hyp);
        }

        return Instantiate(prefab, newPosition, Quaternion.identity, targetParent);
    }

    Vector3 GetClosestPointOutsideCamera(Vector3 pos, float gap=0)
    {
        float newX = pos.x;
        float newY = pos.y;

        Camera cam = Camera.main;

        float height = cam.orthographicSize * 2f;
        float width = height * cam.aspect;

        Vector3 camPos = cam.transform.position;

        float toLeft   = M.Abs(pos.x - (camPos.x - width / 2f));
        float toRight  = M.Abs(pos.x - (camPos.x + width / 2f));
        float toBottom = M.Abs(pos.y - (camPos.y - height / 2f));
        float toTop    = M.Abs(pos.y - (camPos.y + height / 2f));

        float dX;
        if (toLeft < toRight){
            dX = -1*toLeft-gap;
        } else {
            dX = toRight+gap;
        }

        float dY;
        if (toBottom < toTop){
            dY = -1*toBottom-gap;
        } else {
            dY = toTop+gap;
        }

        if (dX < dY){
            newX += dX;
        } else newY += dY;

        return new Vector3(newX, newY, 0);
    }
    
}
