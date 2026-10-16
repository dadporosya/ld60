using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntityEventBase : GameObjectDirector, IConsumeAllTokens
{
    [Header("Entity Event Settings")]
    [SerializeField] private float _tokensCount=0;
    public float tokensCount
    {
        get => _tokensCount;
        set => _tokensCount = value;
    }
//
    public float spawnRadius = 5f;

    public enum TargetChoosingType { targetTag, targetParentTag }
    [SerializeField] private TargetChoosingType targetChoosingType;
    public List<string> targetTags = new List<string>();
    public List<string> targetParentTags = new List<string>();

    public override void Start()
    {
        tokens = tokensCount;
        StartCoroutine(InitSequence());
    }

    private IEnumerator InitSequence()
    {
        yield return null;

        List<GameObject> prefabsToSpawn = ChoosePrefabsUntilEmpty();
        Transform chosenTarget = ChooseTarget();
        List<GameObject> spawned = SpawnAllAround(prefabsToSpawn);
    }

    private Transform ChooseTarget()
    {
        if (targetChoosingType == TargetChoosingType.targetTag)
        {
            List<GameObject> candidates = new List<GameObject>();
            foreach (string tag in targetTags)
            {
                GameObject[] found = GameObject.FindGameObjectsWithTag(tag);
                if (found != null) candidates.AddRange(found);
            }
            if (candidates.Count == 0) return null;
            return h.RandChoice(candidates).transform;
        }
        else
        {
            List<Transform> parents = new List<Transform>();
            foreach (string tag in targetParentTags)
            {
                GameObject parent = GameObject.FindGameObjectWithTag(tag);
                if (parent != null) parents.Add(parent.transform);
            }
            if (parents.Count == 0) return null;
            Transform chosen = h.RandChoice(parents);
            return h.GetRndChild(chosen);
        }
    }

    private List<GameObject> SpawnAllAround(List<GameObject> prefabs)
    {
        List<GameObject> spawned = new List<GameObject>();
        Transform parent = entityManager ? entityManager.unitParent : null;

        foreach (GameObject prefab in prefabs)
        {
            if (prefab == null) continue;
            Vector3 pos = transform.position + (Vector3)(Random.insideUnitCircle * spawnRadius);
            GameObject instance = Instantiate(prefab, pos, Quaternion.identity, parent);
            if (instance != null) spawned.Add(instance);
        }
        return spawned;
    }
    
}
