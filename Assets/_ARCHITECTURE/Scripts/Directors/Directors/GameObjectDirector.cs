using UnityEngine;

public class GameObjectDirector : EntityDirector<GameObject>
{
    public override GameObject SpawnPrefab(GameObject prefab)
    {
        GameObject instance = base.SpawnPrefab(prefab);
        if (instance != null && instance.TryGetComponent<EntityEventBase>(out var entityEvent))
            entityEvent.entityManager = entityManager;
        return instance;
    }
}
