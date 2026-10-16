using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Windows XP style progress bar: instead of a stretched fill image, the bar is filled with
/// a row of discrete objects (green rectangles) that pop in one by one.
/// Call <see cref="Fill"/> with a percentage to animate towards that amount of objects.
/// </summary>
public class FilledWithObjectProgressBar : MonoBehaviour
{
    public enum FillDirection { right, left, up, down }

    [Header("Refs")]
    [Tooltip("Container the fill objects are parented to. Falls back to this transform.")]
    [SerializeField] private RectTransform background;
    [Tooltip("Prefab of a single fill element (the green rectangle).")]
    [SerializeField] private GameObject fillObject;

    [Header("Layout")]
    [Tooltip("If true, the gap is derived from the background size and maxObjectCount instead of using gapBetweenFillObjects.")]
    [SerializeField] private bool calculateGapByObjectCount = false;
    [Tooltip("If true, maxObjectCount is derived from how many objects of (size + gap) fit into the background. Overrides calculateGapByObjectCount.")]
    [SerializeField] private bool calculateCountByGap = false;
    [Tooltip("Ignored when calculateCountByGap is on — read MaxObjectCount for the effective value.")]
    [SerializeField] private int maxObjectCount = 20;
    [Tooltip("Empty space between the edges of two neighbour objects. Step = fill object size + gap. Used when calculateGapByObjectCount is false.")]
    [SerializeField] private float gapBetweenFillObjects = 2f;
    [SerializeField] private Vector3 firstObjectPos = new Vector3(-130f, 0f, 0f);
    [SerializeField] private FillDirection fillDirection = FillDirection.right;

    [Header("Animation")]
    [Tooltip("Objects added (or removed) per second. <= 0 means instant.")]
    [SerializeField] private float fillSpeed = 12f;
    [Tooltip("Objects are destroyed when unfilling. If false they are just hidden and reused.")]
    [SerializeField] private bool destroyOnUnfill = false;

    [Header("Events")]
    public UnityEvent onFullFill;
    public UnityEvent onEmpty;

    private readonly List<GameObject> _objects = new List<GameObject>();
    private Coroutine _fillCoroutine;
    private int _shownCount;
    private float _currentPercentage;
    private float _fillObjectSize = -1f;

    /// <summary>Current fill, 0..1.</summary>
    public float CurrentPercentage => _currentPercentage;
    /// <summary>Number of objects currently visible.</summary>
    public int ShownCount => _shownCount;
    /// <summary>Effective max count — computed from the gap when calculateCountByGap is on.</summary>
    public int MaxObjectCount => calculateCountByGap ? CalculatedCountByGap() : maxObjectCount;

    private RectTransform Root => background ? background : (RectTransform)transform;

    private void Awake()
    {
        if (!background) background = transform as RectTransform;
    }

    /// <summary>
    /// Animates the bar towards the given percentage.
    /// Accepts either 0..1 or 0..100 (values above 1 are treated as 0..100).
    /// </summary>
    public void Fill(float percentage)
    {
        if (percentage > 1f) percentage /= 100f;
        _currentPercentage = Mathf.Clamp01(percentage);

        int targetCount = Mathf.RoundToInt(_currentPercentage * MaxObjectCount);

        if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);

        if (fillSpeed <= 0f || !gameObject.activeInHierarchy)
        {
            SetCount(targetCount);
            RaiseEdgeEvents();
            return;
        }

        _fillCoroutine = StartCoroutine(FillRoutine(targetCount));
    }

    /// <summary>Instantly sets the bar to a percentage without animating.</summary>
    public void SetInstant(float percentage)
    {
        if (percentage > 1f) percentage /= 100f;
        _currentPercentage = Mathf.Clamp01(percentage);
        if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);
        SetCount(Mathf.RoundToInt(_currentPercentage * MaxObjectCount));
        RaiseEdgeEvents();
    }

    /// <summary>Removes every object and resets the bar to 0.</summary>
    public void Clear()
    {
        if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);
        _fillCoroutine = null;
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i]) Destroy(_objects[i]);
        }
        _objects.Clear();
        _shownCount = 0;
        _currentPercentage = 0f;
    }

    private IEnumerator FillRoutine(int targetCount)
    {
        float step = 1f / Mathf.Max(fillSpeed, 0.0001f);

        while (_shownCount != targetCount)
        {
            SetCount(_shownCount + (targetCount > _shownCount ? 1 : -1));
            yield return new WaitForSeconds(step);
        }

        _fillCoroutine = null;
        RaiseEdgeEvents();
    }

    private void RaiseEdgeEvents()
    {
        int max = MaxObjectCount;
        if (_shownCount >= max && max > 0) onFullFill?.Invoke();
        else if (_shownCount <= 0) onEmpty?.Invoke();
    }

    private void SetCount(int count)
    {
        count = Mathf.Clamp(count, 0, MaxObjectCount);

        while (_shownCount < count) ShowObject(_shownCount++);
        while (_shownCount > count) HideObject(--_shownCount);
    }

    private void ShowObject(int index)
    {
        if (!fillObject)
        {
            h.Out("FilledWithObjectProgressBar: fillObject prefab is not set on", name);
            return;
        }

        GameObject go;
        if (index < _objects.Count && _objects[index])
        {
            go = _objects[index];
        }
        else
        {
            go = Instantiate(fillObject, Root);
            go.name = $"{fillObject.name}_{index}";
            while (_objects.Count <= index) _objects.Add(null);
            _objects[index] = go;
        }

        PlaceObject(go, index);
        go.SetActive(true);
    }

    private void HideObject(int index)
    {
        if (index < 0 || index >= _objects.Count) return;
        GameObject go = _objects[index];
        if (!go) return;

        if (destroyOnUnfill)
        {
            Destroy(go);
            _objects[index] = null;
        }
        else go.SetActive(false);
    }

    private void PlaceObject(GameObject go, int index)
    {
        Vector3 pos = firstObjectPos + GetDirection() * (GetStep() * index);

        if (go.transform is RectTransform rt) rt.anchoredPosition3D = pos;
        else go.transform.localPosition = pos;
    }

    private bool IsHorizontal =>
        fillDirection == FillDirection.right || fillDirection == FillDirection.left;

    /// <summary>Distance between the pivots of two neighbour objects: fill object size + gap.</summary>
    private float GetStep()
    {
        if (calculateCountByGap || !calculateGapByObjectCount || maxObjectCount <= 1)
            return GetFillObjectSize() + gapBetweenFillObjects;

        // Last object's far edge should land on the background edge.
        float span = GetAvailableSpan() - GetFillObjectSize();

        return Mathf.Max(span / (maxObjectCount - 1), 0f) + GetFillObjectSize();
    }

    /// <summary>How many objects of (size + gap) fit between the first object and the background edge.</summary>
    private int CalculatedCountByGap()
    {
        float step = GetFillObjectSize() + gapBetweenFillObjects;
        if (step <= 0f) return 0;

        float span = GetAvailableSpan() - GetFillObjectSize();
        if (span < 0f) return 0;

        return Mathf.FloorToInt(span / step) + 1;
    }

    /// <summary>Distance from the first object's pivot to the background edge along the fill direction.</summary>
    private float GetAvailableSpan()
    {
        Rect rect = Root.rect;

        float start = IsHorizontal ? firstObjectPos.x : firstObjectPos.y;
        float edge = IsHorizontal
            ? (fillDirection == FillDirection.right ? rect.xMax : rect.xMin)
            : (fillDirection == FillDirection.up ? rect.yMax : rect.yMin);

        float span = Mathf.Abs(edge - start);
        if (span <= 0f) span = IsHorizontal ? rect.width : rect.height;

        return span;
    }

    /// <summary>Size of the fill object along the fill axis, taken from the prefab's RectTransform (scale included).</summary>
    private float GetFillObjectSize()
    {
        if (_fillObjectSize >= 0f) return _fillObjectSize;
        _fillObjectSize = 0f;

        if (fillObject && fillObject.transform is RectTransform rt)
        {
            Vector3 scale = rt.localScale;
            _fillObjectSize = IsHorizontal
                ? rt.rect.width * Mathf.Abs(scale.x)
                : rt.rect.height * Mathf.Abs(scale.y);
        }

        return _fillObjectSize;
    }

    private Vector3 GetDirection()
    {
        switch (fillDirection)
        {
            case FillDirection.left: return Vector3.left;
            case FillDirection.up: return Vector3.up;
            case FillDirection.down: return Vector3.down;
            default: return Vector3.right;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxObjectCount < 0) maxObjectCount = 0;
        if (fillSpeed < 0f) fillSpeed = 0f;
        _fillObjectSize = -1f; // re-measure after prefab / direction changes

        // Mirror the computed value into the inspector field so it is visible.
        if (calculateCountByGap)
        {
            int computed = CalculatedCountByGap();
            if (computed > 0) maxObjectCount = computed;
        }
    }
#endif
}
