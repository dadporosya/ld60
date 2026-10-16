using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

public class CirclingMovement : AnimationBase
{
    public List<GameObject> objectPfbs = new();
    public int objectCount = 3;
    public float radius = 1f;
    public float stretchingByX = 1f;
    public float stretchingByY = 1f;
    public float loopDuration = 2f;

    private List<GameObject> _spawnedObjects = new();
    private float _elapsed;

    public override void Awake()
    {
        base.Awake();
        SpawnObjects();
    }

    private void SpawnObjects()
    {
        foreach (var obj in _spawnedObjects)
            if (obj != null) Destroy(obj);
        _spawnedObjects.Clear();

        for (int i = 0; i < objectCount; i++)
        {
            var pfb = objectPfbs[Random.Range(0, objectPfbs.Count)];
            var obj = Instantiate(pfb, transform);
            _spawnedObjects.Add(obj);
            PlaceObject(obj, PhaseForIndex(i), 0f);
        }
    }

    private float PhaseForIndex(int i) => (float)i / objectCount * Mathf.PI * 2f;

    private void PlaceObject(GameObject obj, float phase, float elapsed)
    {
        float angle = Mathf.PI * 2f * (elapsed / loopDuration) + phase;
        float x = stretchingByX * radius * Mathf.Cos(angle);
        float y = stretchingByY * radius * Mathf.Sin(angle);
        obj.transform.localPosition = new Vector3(x, y, 0f);
    }

    public override IEnumerator AnimationCoroutine()
    {
        _elapsed = 0f;

        while (_elapsed < loopDuration)
        {
            _elapsed += Time.deltaTime;
            for (int i = 0; i < _spawnedObjects.Count; i++)
                PlaceObject(_spawnedObjects[i], PhaseForIndex(i), _elapsed);
            yield return null;
        }

        yield return base.AnimationCoroutine();
    }

    private void OnDestroy()
    {
        foreach (var obj in _spawnedObjects)
            if (obj != null) Tween.StopAll(obj);
    }

    public override void ReturnToInitialState()
    {
        _elapsed = 0f;
        for (int i = 0; i < _spawnedObjects.Count; i++)
            PlaceObject(_spawnedObjects[i], PhaseForIndex(i), 0f);
    }
}
