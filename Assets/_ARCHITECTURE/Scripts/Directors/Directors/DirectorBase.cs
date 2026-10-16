using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DirectorBase<T> : MonoBehaviour
{
    public TokenBasedContainerBase<T> data;
    public float tokens=0;
    public float tokenIncome=0;
    public float tokenAccrualTime = 1f;

    public float tooCheapCoef = 6;
    
    [SerializeField] private bool startIncomeOnStart = false;

    public virtual void Start()
    {
        if (startIncomeOnStart) StartTokenIncome();
    }

    Coroutine tokenIncomeCoroutine;

    public void StartTokenIncome()
    {
        tokenIncomeCoroutine = StartCoroutine(TokenIncome());
    }

    public void StopTokenIncome()
    {
        if (tokenIncomeCoroutine != null) StopCoroutine(tokenIncomeCoroutine);
        tokenIncomeCoroutine = null;
    }

    public virtual IEnumerator TokenIncome()
    {
        yield return null;

        float timeElapsed=0;
        while (true)
        {
            while (timeElapsed < tokenAccrualTime)
            {
                if (GameFlowManager.Instance.IsPaused())
                {
                    yield return new WaitForEndOfFrame();
                    continue;
                }
                timeElapsed+=Time.deltaTime;
                yield return null;
            }
            
            tokens += tokenIncome;
            timeElapsed=0;

            AfterTokenIncome();
            
            yield return null;
        }
        yield return null;
    }

    public virtual void AfterTokenIncome()
    {
        
    }

    public List<T> ChooseSeveralPrefabs(
        int count,
        float additionalTokens=0,
        float maxCost = float.MaxValue,
        float minCost = 0f,
        bool spentTokens = true,
        int mostExpensiveCount = 0
        )
    {
        List<T> result = new List<T>();
        tokens += additionalTokens;
        for (int i = 0; i < count; i++)
        {
            T pfb = ChoosePrefab(
                maxCost: maxCost,
                minCost: minCost,
                spentTokens: spentTokens,
                mostExpensive: mostExpensiveCount > i?true:false
                );
            
            if (pfb == null) break;
            result.Add(pfb);
        }
        
        return result;
    }
    
    public List<T> ChoosePrefabsUntilEmpty(
        float additionalTokens = 0,
        int maxPrefabCount = -1,
        float maxCost = float.MaxValue,
        float minCost = 0f,
        int mostExpensiveCount = 0
        )
    {
        List<T> result = new List<T>();
        tokens += additionalTokens;
        while (maxPrefabCount == -1 || result.Count < maxPrefabCount)
        {
            T pfb = ChoosePrefab(
                maxCost: maxCost,
                minCost: minCost,
                spentTokens: true,
                mostExpensive: mostExpensiveCount > result.Count
                );
            if (pfb == null) break;
            result.Add(pfb);
        }
        return result;
    }


    public T ChoosePrefab(
        float maxCost = float.MaxValue,
        float minCost = 0f,
        bool spentTokens = true,
        bool mostExpensive = false
        )
    {
        // h.Out(mostExpensive);
        if (!data || data.items == null || data.items.Count == 0) return default;

        // Items are pre-sorted by cost ascending (done in OnEnable of the container)

        // Filter affordable items (cost <= tokens and cost <= maxCost)
        List<TokenBasedItemData<T>> affordable = data.items
            .FindAll(item => item.cost <= tokens && item.cost <= maxCost && item.cost >= minCost);

        if (affordable.Count == 0)
        {

            return default;
        }

        if (mostExpensive)
        {
            float maxPrice = affordable[affordable.Count - 1].cost;
            List<TokenBasedItemData<T>> priciest = affordable.FindAll(item => item.cost == maxPrice);
            TokenBasedItemData<T> chosen = priciest[UnityEngine.Random.Range(0, priciest.Count)];
            if (spentTokens) tokens -= chosen.cost;
            return chosen.value;
        }

        List<TokenBasedItemData<T>> candidates;

        // Filter out items that are "too cheap"
        float cheapThreshold = tokens / tooCheapCoef;
        candidates = affordable.FindAll(item => item.cost > cheapThreshold);

        if (candidates.Count == 0) candidates = affordable;

        float totalWeight = 0f;
        foreach (var item in candidates) totalWeight += item.weight;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float cumulative = 0f;
        foreach (var item in candidates)
        {
            cumulative += item.weight;
            if (roll < cumulative)
            {
                if (spentTokens) tokens -= item.cost;
                return item.value;
            }
        }

        
        TokenBasedItemData<T> lastChance = h.RandChoice(candidates);
        if (spentTokens) tokens -= lastChance.cost;
        return lastChance.value;
    }
}
