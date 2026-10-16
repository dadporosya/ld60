using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntityDirector<T> : DirectorBase<GameObject>
{
    public EntityManager<T> entityManager;
    public float spawnPeriod=1f;
    [SerializeField] private float minDistribution=1f;
    [SerializeField] private float maxDistribution=1f;
    [SerializeField] private bool _spawning=true;

    [SerializeField] private float minDelayBeforeSpawnInWave = 0.001f;
    [SerializeField] private float maxDelayBeforeSpawnInWave = 0.5f;
    
    public bool spawning
    {
        get => _spawning;
        set
        {
            _spawning = value;
            if (spawning)
            {
                StopSpawning();
            }
            else
            {
                StartSpawning();
            }
        }
    }

    [SerializeField] private int minMostExpensiveCardsPerWave = 1;

    [SerializeField] private int maxEntitiesPerWave = 5;
    
    Coroutine spawningCoroutine;
    
    public override void Start()
    {
        base.Start();
        if (spawning)
        {
            StartSpawning();
        }
    }

    public void StartSpawning()
    {
        StopSpawning();
        spawningCoroutine = StartCoroutine(SpawningCoroutine());
    }

    public void StopSpawning()
    {
        if (spawningCoroutine != null) StopCoroutine(spawningCoroutine);
        spawningCoroutine = null;
    }

    private IEnumerator SpawningCoroutine()
    {
        yield return null;
        float timeElapsed = 0f;
        float desiredSpawnPeriod = spawnPeriod;
        while (true)
        {
            desiredSpawnPeriod = h.Range(
                spawnPeriod * minDistribution,
                spawnPeriod * maxDistribution
                );
            timeElapsed = 0f;
            while (timeElapsed < desiredSpawnPeriod)
            {
                if (GameFlowManager.Instance.IsPaused())
                {
                    yield return new WaitForEndOfFrame();
                }
                timeElapsed +=  Time.deltaTime;
                yield return null;
            }
            
            yield return StartCoroutine(SpawnWaveCoroutine());
            yield return null;
        }

        yield return null;
    }

    public void SpawnWave()
    {
        StartCoroutine(SpawnWaveCoroutine());
    }
    public virtual IEnumerator SpawnWaveCoroutine()
    {
        // h.Out("wave");
        int entitiesCounter = 0;
        int expensiveCount = h.Max(h.Range(
            Mathf.CeilToInt(minMostExpensiveCardsPerWave/2f), minMostExpensiveCardsPerWave
        ), 0);
        for (int i = 0; i < expensiveCount && tokens > 0; i++)
        {
            yield return new WaitForSeconds(h.Range(minDelayBeforeSpawnInWave, maxDelayBeforeSpawnInWave));
            GameObject instance = SpawnMostExpensiveCard();
            entitiesCounter++;
            if (instance == null || entitiesCounter >= maxEntitiesPerWave) yield break;
            
        }

        while (tokens > 0)
        {
            yield return new WaitForSeconds(h.Range(minDelayBeforeSpawnInWave, maxDelayBeforeSpawnInWave));
            GameObject instance = SpawnRandomCard();
            entitiesCounter++;
            if (instance == null || entitiesCounter >= maxEntitiesPerWave) yield break;
            
        }
    }

    public virtual GameObject SpawnRandomCard()
    {
        var entity =  SpawnPrefab(ChoosePrefab());

        AssignTokens(entity);
        
        return entity;
    }

    public virtual GameObject SpawnMostExpensiveCard()
    {
        var entity =  SpawnPrefab(ChoosePrefab(mostExpensive:true));
        
        AssignTokens(entity);
        
        return entity;
    }
    
    private bool AssignTokens(GameObject entity)
    {
        if (!entity) return false; // 
        if (!entity.TryGetComponent<IConsumeAllTokens>(out var tokenConsumer))
        {
            return false;
        }
        tokenConsumer.tokensCount = tokens;
        tokens = 0;
        return true;
    }
    
    public virtual GameObject SpawnPrefab(GameObject prefab)
    {
        if (!entityManager)
        {
            h.Out("No enemy man");
            return null;
        }

        if (!entityManager.AbleToSpawn(prefab))
        {
            // h.Out("Spawn failed");
            return null;
        }

        return entityManager.SpawnPrefab(prefab);
    }

    public void ToggleOff()
    {
        spawning = !spawning;
    }
}

