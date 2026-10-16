using UnityEngine;

public class EntityManager<T> : MonoBehaviour
{
    public Transform unitParent;
    [SerializeField] private string unitParentTag;
    public int entityCount=0;
    public int maxEntityCount = 40;
    public SpawnerManager spawner;



    private void Awake()
    {
        if (unitParentTag != "" && unitParent == null)
        {
            unitParent = GameObject.FindGameObjectWithTag(unitParentTag).transform;
        }

        if (unitParent)
        {
            foreach (Transform child in unitParent)
            {
                if (!child.TryGetComponent(out T entity)) continue;
                entityCount++;
            }
        }

        if (spawner)
        {
            spawner.targetParent = unitParent;
            spawner.parentTag = unitParentTag;
        }
    }

    public void OnEntityDestroyed()
    {
        entityCount = Mathf.Max(0, entityCount - 1);
    }

    public GameObject SpawnPrefab(GameObject prefab)
    {
        if (!AbleToSpawn(prefab)) return null;
        var instance = spawner.SpawnPrefab(prefab);
        if (instance == null) return null;
        entityCount++;
        instance.AddComponent<DestroyNotifier>().AddOnDestroyListener(OnEntityDestroyed);
        return instance;
    }

    public bool AbleToSpawn(GameObject prefab=null)
    {
        if (prefab == null)
        {
            // h.Out("No prefab to spawn");
            return false;
        }
        
        if (entityCount >= maxEntityCount)
        {
            // h.Out("Max entity count reached", entityCount);
            return false;
        }

        return true;
    }
}

