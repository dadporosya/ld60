using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// Manages spawning of random TextMeshPro text alerts across the screen within a UI Canvas parent.
/// Replaces the text on each spawned alert with a random message from a configured pool,
/// enforces an on-screen simultaneous alert limit, and automatically cleans up alerts after a randomized duration.
/// </summary>
[AddComponentMenu("MyFriendPetya/UI/Random Text Alert Manager")]
public class RandomTextAlertManager : MonoBehaviour
{
    public static RandomTextAlertManager Instance;

    // ================================================================ Prefab & Messages

    [Header("Alert Prefab & Content")]
    [Tooltip("The TextMeshPro UI prefab instantiated for each text alert.")]
    [FormerlySerializedAs("tmpPfb")]
    [SerializeField] private TMP_Text tmppfb;

    [Tooltip("Optional pool of TextMeshPro prefabs. If assigned, a random prefab from this list is chosen on each spawn.")]
    [SerializeField] private List<TMP_Text> tmppfbs = new List<TMP_Text>();

    [Tooltip("List of random text messages to display on the spawned alerts.")]
    [SerializeField] private List<string> messages = new List<string>()
    {
        "SYSTEM ERROR",
        "CORRUPTION DETECTED",
        "CRITICAL FAULT",
        "UNAUTHORIZED ACCESS",
        "DATA OVERFLOW",
        "SIGNAL LOST",
        "SECURITY BREACH",
        "MEMORY LEAK"
    };

    // ================================================================ Container & Placement

    [Header("Container & Placement")]
    [Tooltip("Parent RectTransform under which alerts will be instantiated. If unassigned, automatically finds or creates a Canvas.")]
    [SerializeField] private RectTransform uiParent;

    [Tooltip("Tag used to find UI parent container if uiParent is unassigned.")]
    [SerializeField] private string uiParentTag = "AlertParent";

    [Tooltip("Padding in screen pixels kept between spawned alerts and the screen edges.")]
    [SerializeField] private float screenMargin = 60f;

    // ================================================================ Timing & Lifetime

    [Header("Lifetime & Timing")]
    [Tooltip("Lifetime duration range for each spawned alert in seconds (min: X, max: Y). Default: 0.5s to 1.0s.")]
    [SerializeField] private Vector2 alertDurationRange = new Vector2(0.5f, 1.0f);

    [Tooltip("Spawn interval range between alerts in seconds (min: X, max: Y). Default: 0.333s to 0.67s.")]
    [SerializeField] private Vector2 spawnRateRange = new Vector2(0.333f, 0.67f);

    // ================================================================ Scale & Stretching

    [Header("Scale & Stretching")]
    [Tooltip("Whether to randomly scale/extend the alert unevenly on X and/or Y.")]
    [SerializeField] private bool randomScale = true;

    [Tooltip("Random scale range for the X axis (min: X, max: Y). Example: 1.0 to 1.5.")]
    [SerializeField] private Vector2 scaleXRange = new Vector2(1.0f, 1.5f);

    [Tooltip("Random scale range for the Y axis (min: X, max: Y). Example: 0.8 to 1.2.")]
    [SerializeField] private Vector2 scaleYRange = new Vector2(0.8f, 1.2f);

    // ================================================================ Limits & Controls

    [Header("Alert Limits & Controls")]
    [Min(1)]
    [Tooltip("Maximum number of simultaneous alerts allowed on screen at once (default: 5). Oldest alert is trimmed if exceeded.")]
    [SerializeField] private int maxAlertCount = 5;

    [Tooltip("Automatically start spawning on Start.")]
    [SerializeField] private bool startSpawningOnStart = false;

    [Tooltip("Use unscaled time so spawn intervals and alert durations run independently of Time.timeScale.")]
    [SerializeField] private bool useUnscaledTime = false;

    [Tooltip("Keep this manager alive across scene loads.")]
    [SerializeField] private bool dontDestroyOnLoad = false;

    // ================================================================ Audio (Optional)

    [Header("Audio")]
    [Tooltip("Sound clip played every time an alert spawns.")]
    [SerializeField] private AudioClip alertSfx;

    [Tooltip("Optional pool of alert clips. If assigned, a random clip is chosen on each spawn.")]
    [SerializeField] private List<AudioClip> alertSfxClips = new List<AudioClip>();

    [Range(0f, 1f)]
    [Tooltip("Volume of the alert SFX (0..1).")]
    [SerializeField] private float alertSfxVolume = 1f;

    // ================================================================ Events

    [Header("Events")]
    [Tooltip("Fires whenever a text alert is spawned.")]
    public UnityEvent<TMP_Text> onAlertSpawned = new UnityEvent<TMP_Text>();

    [Tooltip("Fires whenever a text alert is destroyed or cleared.")]
    public UnityEvent<TMP_Text> onAlertDestroyed = new UnityEvent<TMP_Text>();

    // ================================================================ Runtime State

    private readonly List<TMP_Text> _activeAlerts = new List<TMP_Text>();
    private Coroutine _spawnRoutine;
    private bool _isSpawning;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private Camera _uiCamera;
    private bool _canvasResolved;

    // ================================================================ Properties & Aliases

    public static RandomTextAlertManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        Instance = FindFirstObjectByType<RandomTextAlertManager>(FindObjectsInactive.Include);
        if (Instance != null) return Instance;

        GameObject obj = new GameObject("RandomTextAlertManager");
        Instance = obj.AddComponent<RandomTextAlertManager>();
        return Instance;
    }

    public TMP_Text TmpPfb
    {
        get => tmppfb;
        set => tmppfb = value;
    }

    public TMP_Text tmppfbProperty
    {
        get => tmppfb;
        set => tmppfb = value;
    }

    public List<TMP_Text> Tmppfbs
    {
        get => tmppfbs;
        set => tmppfbs = value;
    }

    public List<string> Messages
    {
        get => messages;
        set => messages = value;
    }

    public RectTransform UiParent
    {
        get
        {
            ResolveCanvas();
            return uiParent ? uiParent : _canvasRect;
        }
        set => uiParent = value;
    }

    public Vector2 AlertDurationRange
    {
        get => alertDurationRange;
        set => alertDurationRange = value;
    }

    public float MinAlertDuration
    {
        get => Mathf.Min(alertDurationRange.x, alertDurationRange.y);
        set => alertDurationRange.x = value;
    }

    public float MaxAlertDuration
    {
        get => Mathf.Max(alertDurationRange.x, alertDurationRange.y);
        set => alertDurationRange.y = value;
    }

    public Vector2 SpawnRateRange
    {
        get => spawnRateRange;
        set => spawnRateRange = value;
    }

    public float MinSpawnRate
    {
        get => Mathf.Min(spawnRateRange.x, spawnRateRange.y);
        set => spawnRateRange.x = value;
    }

    public float MaxSpawnRate
    {
        get => Mathf.Max(spawnRateRange.x, spawnRateRange.y);
        set => spawnRateRange.y = value;
    }

    public bool RandomScale
    {
        get => randomScale;
        set => randomScale = value;
    }

    public Vector2 ScaleXRange
    {
        get => scaleXRange;
        set => scaleXRange = value;
    }

    public Vector2 ScaleYRange
    {
        get => scaleYRange;
        set => scaleYRange = value;
    }

    public int MaxAlertCount
    {
        get => maxAlertCount;
        set => maxAlertCount = Mathf.Max(1, value);
    }

    public int MaxAlertOnScreenCount
    {
        get => maxAlertCount;
        set => maxAlertCount = Mathf.Max(1, value);
    }

    public bool IsSpawning => _isSpawning;

    public IReadOnlyList<TMP_Text> ActiveAlerts
    {
        get
        {
            CleanDeadAlerts();
            return _activeAlerts;
        }
    }

    public int ActiveAlertCount
    {
        get
        {
            CleanDeadAlerts();
            return _activeAlerts.Count;
        }
    }

    // ================================================================ Unity Lifecycle

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, dontDestroyOnLoad);
        ResolveCanvas();
    }

    private void Start()
    {
        if (startSpawningOnStart)
        {
            StartSpawning();
        }
    }

    private void OnDisable()
    {
        StopSpawning();
    }

    private void OnDestroy()
    {
        StopSpawning();
        ClearAlerts();

        if (Instance == this)
            Instance = null;
    }

    // ================================================================ Spawning Control

    /// <summary>
    /// Starts the periodic spawning routine if not already running.
    /// </summary>
    public void StartSpawning()
    {
        if (_isSpawning) return;

        _isSpawning = true;
        if (_spawnRoutine != null)
            StopCoroutine(_spawnRoutine);

        _spawnRoutine = StartCoroutine(SpawnLoopRoutine());
    }

    /// <summary>
    /// Stops the periodic spawning routine.
    /// </summary>
    /// <param name="clearExistingAlerts">When true, immediately clears and destroys all active alerts.</param>
    public void StopSpawning(bool clearExistingAlerts = false)
    {
        _isSpawning = false;

        if (_spawnRoutine != null)
        {
            StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }

        if (clearExistingAlerts)
        {
            ClearAlerts();
        }
    }

    /// <summary>
    /// Destroys all currently active text alerts and clears tracking.
    /// </summary>
    public void ClearAlerts()
    {
        for (int i = _activeAlerts.Count - 1; i >= 0; i--)
        {
            if (_activeAlerts[i] != null)
            {
                onAlertDestroyed?.Invoke(_activeAlerts[i]);
                Destroy(_activeAlerts[i].gameObject);
            }
        }
        _activeAlerts.Clear();
    }

    private IEnumerator SpawnLoopRoutine()
    {
        while (_isSpawning)
        {
            // Stall while game is paused
            while (GameFlowManager.Instance != null && GameFlowManager.Instance.IsPaused())
            {
                yield return null;
            }

            SpawnAlert();

            float minRate = Mathf.Min(spawnRateRange.x, spawnRateRange.y);
            float maxRate = Mathf.Max(spawnRateRange.x, spawnRateRange.y);
            float interval = UnityEngine.Random.Range(minRate, maxRate);
            interval = Mathf.Max(0.01f, interval);

            float elapsed = 0f;
            while (elapsed < interval && _isSpawning)
            {
                while (GameFlowManager.Instance != null && GameFlowManager.Instance.IsPaused())
                {
                    yield return null;
                }

                float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                elapsed += dt;
                yield return null;
            }
        }

        _spawnRoutine = null;
    }

    // ================================================================ Single Alert Spawning

    /// <summary>
    /// Spawns a single alert on screen, sets its message, and schedules its destruction.
    /// </summary>
    /// <param name="customMessage">Specific message to display. If null, a random message from <see cref="messages"/> is chosen.</param>
    /// <param name="screenPosition">Screen pixel coordinates. If null, a random location on screen is chosen.</param>
    /// <returns>The spawned <see cref="TMP_Text"/> component, or null if no prefab is available.</returns>
    public TMP_Text SpawnAlert(string customMessage = null, Vector2? screenPosition = null)
    {
        TMP_Text prefabToSpawn = ChoosePrefab();
        if (!prefabToSpawn)
        {
            h.Out("[RandomTextAlertManager] Cannot spawn alert — no TMP_Text prefab assigned.");
            return null;
        }

        ResolveCanvas();
        RectTransform parentTransform = uiParent ? uiParent : _canvasRect;

        CleanDeadAlerts();

        // Enforce maximum simultaneous alert count (trim oldest)
        while (_activeAlerts.Count >= maxAlertCount && _activeAlerts.Count > 0)
        {
            TMP_Text oldest = _activeAlerts[0];
            _activeAlerts.RemoveAt(0);
            if (oldest != null)
            {
                onAlertDestroyed?.Invoke(oldest);
                Destroy(oldest.gameObject);
            }
        }

        // Instantiate
        TMP_Text alertInstance = Instantiate(prefabToSpawn, parentTransform);

        // Apply text message
        string textToSet = customMessage;
        if (string.IsNullOrEmpty(textToSet))
        {
            textToSet = ChooseMessage();
        }

        if (!string.IsNullOrEmpty(textToSet))
        {
            alertInstance.text = textToSet;

            // Also search child TMP_Text components if the prefab is a compound GameObject
            TMP_Text[] childTexts = alertInstance.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text ct in childTexts)
            {
                if (ct != null) ct.text = textToSet;
            }
        }

        // Place on screen
        PlaceAlert(alertInstance.rectTransform, screenPosition);

        // Play SFX
        PlayAlertSfx();

        _activeAlerts.Add(alertInstance);

        // Schedule lifetime
        float minDur = Mathf.Min(alertDurationRange.x, alertDurationRange.y);
        float maxDur = Mathf.Max(alertDurationRange.x, alertDurationRange.y);
        float duration = UnityEngine.Random.Range(minDur, maxDur);
        duration = Mathf.Max(0.05f, duration);

        StartCoroutine(AlertLifetimeRoutine(alertInstance, duration));

        onAlertSpawned?.Invoke(alertInstance);

        return alertInstance;
    }

    private IEnumerator AlertLifetimeRoutine(TMP_Text alert, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (alert == null) yield break;

            while (GameFlowManager.Instance != null && GameFlowManager.Instance.IsPaused())
            {
                yield return null;
            }

            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            elapsed += dt;
            yield return null;
        }

        if (alert != null)
        {
            _activeAlerts.Remove(alert);
            onAlertDestroyed?.Invoke(alert);
            Destroy(alert.gameObject);
        }
    }

    // ================================================================ Placement & Helpers

    private TMP_Text ChoosePrefab()
    {
        if (tmppfbs != null && tmppfbs.Count > 0)
        {
            TMP_Text picked = h.RandChoice(tmppfbs);
            if (picked != null) return picked;
        }

        return tmppfb;
    }

    private string ChooseMessage()
    {
        if (messages != null && messages.Count > 0)
        {
            return h.RandChoice(messages);
        }
        return null;
    }

    private void PlaceAlert(RectTransform rt, Vector2? screenPosition)
    {
        if (!rt) return;
        ResolveCanvas();

        RectTransform parentRect = uiParent ? uiParent : _canvasRect;

        // Choose screen point
        Vector2 screenPoint;
        if (screenPosition.HasValue)
        {
            screenPoint = screenPosition.Value;
        }
        else
        {
            float minX = screenMargin;
            float maxX = Mathf.Max(screenMargin + 1f, Screen.width - screenMargin);
            float minY = screenMargin;
            float maxY = Mathf.Max(screenMargin + 1f, Screen.height - screenMargin);

            screenPoint = new Vector2(
                UnityEngine.Random.Range(minX, maxX),
                UnityEngine.Random.Range(minY, maxY)
            );
        }

        if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, _uiCamera, out Vector2 localPoint))
        {
            rt.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);
        }
        else if (parentRect != null)
        {
            Rect r = parentRect.rect;
            float localMarginX = Mathf.Min(screenMargin, r.width * 0.35f);
            float localMarginY = Mathf.Min(screenMargin, r.height * 0.35f);

            float rx = UnityEngine.Random.Range(r.xMin + localMarginX, r.xMax - localMarginX);
            float ry = UnityEngine.Random.Range(r.yMin + localMarginY, r.yMax - localMarginY);

            rt.anchoredPosition = new Vector2(rx, ry);
        }
        else
        {
            rt.position = new Vector3(screenPoint.x, screenPoint.y, 0f);
        }

        if (randomScale)
        {
            float minX = Mathf.Min(scaleXRange.x, scaleXRange.y);
            float maxX = Mathf.Max(scaleXRange.x, scaleXRange.y);
            float minY = Mathf.Min(scaleYRange.x, scaleYRange.y);
            float maxY = Mathf.Max(scaleYRange.x, scaleYRange.y);

            float sx = UnityEngine.Random.Range(minX, maxX);
            float sy = UnityEngine.Random.Range(minY, maxY);

            rt.localScale = new Vector3(sx, sy, 1f);
        }
        else
        {
            rt.localScale = Vector3.one;
        }
    }

    private void PlayAlertSfx()
    {
        AudioClip clip = alertSfx;
        if (alertSfxClips != null && alertSfxClips.Count > 0)
        {
            clip = h.RandChoice(alertSfxClips);
        }

        if (!clip) return;

        if (SFXManager.Instance)
        {
            SFXManager.Instance.PlayClip(clip, volumeIn: alertSfxVolume, is3D: false, playIndependently: true);
        }
    }

    private void CleanDeadAlerts()
    {
        for (int i = _activeAlerts.Count - 1; i >= 0; i--)
        {
            if (_activeAlerts[i] == null)
            {
                _activeAlerts.RemoveAt(i);
            }
        }
    }

    private void ResolveCanvas()
    {
        if (_canvasResolved && _canvas != null && _canvasRect != null)
            return;

        if (uiParent != null)
        {
            _canvas = uiParent.GetComponentInParent<Canvas>();
            if (_canvas != null)
            {
                _canvasRect = _canvas.GetComponent<RectTransform>();
                _uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
                _canvasResolved = true;
                return;
            }
        }

        if (!string.IsNullOrEmpty(uiParentTag))
        {
            GameObject taggedObj = GameObject.FindGameObjectWithTag(uiParentTag);
            if (taggedObj != null)
            {
                uiParent = taggedObj.GetComponent<RectTransform>();
                if (uiParent != null)
                {
                    _canvas = uiParent.GetComponentInParent<Canvas>();
                    if (_canvas != null)
                    {
                        _canvasRect = _canvas.GetComponent<RectTransform>();
                        _uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
                        _canvasResolved = true;
                        return;
                    }
                }
            }
        }

        if (_canvas == null)
        {
            _canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Exclude);
            if (_canvas == null)
                _canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);

            if (_canvas != null)
            {
                _canvasRect = _canvas.GetComponent<RectTransform>();
                _uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
                if (uiParent == null)
                {
                    uiParent = _canvasRect;
                }
            }
        }

        _canvasResolved = _canvas != null;
    }
}
