using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Manages product licence verification, UI watermark/activated state toggling,
/// Win XP styled alert dialogs, and persistent JSON save data.
///
/// <para><b>Operating Principle:</b></para>
/// <list type="bullet">
///   <item><description>Player inputs product key text into <see cref="inputField"/> and clicks <see cref="submitBtn"/> (or presses Enter).</description></item>
///   <item><description>If invalid, spawns an error <see cref="WinXPMessageAlert"/> with configured title, text, and SFX.</description></item>
///   <item><description>If valid, spawns a success <see cref="WinXPMessageAlert"/>, enables <see cref="ActivfatedLicenceObject"/>, disables <see cref="LicenceWaterMark"/>, and writes state to JSON and PlayerPrefs.</description></item>
///   <item><description>On scene start / initialization, restores previous state: if activated, activates <see cref="ActivfatedLicenceObject"/> and hides <see cref="LicenceWaterMark"/>.</description></item>
/// </list>
/// </summary>
[AddComponentMenu("MyFriendPetya/UI/Licence Window Manager")]
public class LicenceWindowManager : MonoBehaviour
{
    public static LicenceWindowManager Instance;

    // Static cache for instantaneous access anywhere in the codebase without scene lookup
    private static bool _isCached;
    private static bool _cachedActivated;

    // ─────────────────────────────────────────────────────────────
    //  UI References
    // ─────────────────────────────────────────────────────────────

    [Header("Input & Submit")]
    [Tooltip("TMP input field where the player enters the licence product key.")]
    public TMP_InputField inputField;

    [Tooltip("Button clicked to submit the licence key.")]
    public Button submitBtn;

    [Tooltip("Button clicked to clear the input field text.")]
    public Button clearBtn;

    [Tooltip("Trigger key verification automatically when the player presses Enter/Submit in the input field.")]
    public bool submitOnEndEdit = true;

    [Header("Licence Visual Elements")]
    [Tooltip("GameObject enabled when a valid licence is active (e.g. Activated badge, licensed features container).")]
    public GameObject ActivfatedLicenceObject;

    [Tooltip("GameObject disabled when a valid licence is active (e.g. 'Unlicensed / Trial' watermark).")]
    public GameObject LicenceWaterMark;

    [Tooltip("Optional root window GameObject to close or deactivate upon successful licence activation.")]
    public GameObject windowRoot;

    [Tooltip("Automatically close/deactivate windowRoot upon successful activation.")]
    public bool closeWindowOnSuccess = false;

    // ─────────────────────────────────────────────────────────────
    //  Licence Key Validation Configuration
    // ─────────────────────────────────────────────────────────────

    [Header("Licence Key Validation")]
    [Tooltip("List of valid product keys accepted by this system.")]
    public List<string> validKeys = new List<string>
    {
        "PETYA-2026-KEY",
        "PETYA-LICENCE-OK",
        "1234-5678-9012",
        "DADPOROSYA-PRO"
    };

    [Tooltip("When true, any non-empty input is accepted as a valid licence key.")]
    public bool acceptAnyNonEmptyKey = false;

    [Tooltip("Ignore letter casing when comparing entered key against validKeys.")]
    public bool ignoreCase = true;

    [Tooltip("Trim whitespace from start and end of entered text.")]
    public bool trimWhitespace = true;

    // ─────────────────────────────────────────────────────────────
    //  Alerts Configuration (WinXP Dialogs)
    // ─────────────────────────────────────────────────────────────

    [Header("Alert Manager")]
    [Tooltip("Explicit WinXpAlertManager to spawn popups through. If unassigned, defaults to WinXpAlertManager.Instance or inactive search.")]
    public WinXpAlertManager alertManager;

    [Tooltip("Custom parent container where spawned alerts will be placed under. If unassigned, falls back to the Canvas root.")]
    public RectTransform alertParent;

    [Tooltip("Where to place spawned licence alerts relative to screen centre in pixels.")]
    public Vector2 alertScreenOffset = Vector2.zero;

    [Tooltip("If true, places licence alerts at random points inside screen bounds instead of fixed offset.")]
    public bool randomAlertPosition = false;

    [Header("Error Alert (Invalid Key)")]
    [Tooltip("Title of the WinXP alert shown on incorrect input.")]
    public string errorAlertTitle = "Licence Error";

    [TextArea(2, 5)]
    [Tooltip("Body content of the WinXP alert shown on incorrect input.")]
    public string errorAlertContent = "The licence key entered is invalid.\nPlease verify your key and try again.";

    [Tooltip("Sound played when the error alert is spawned.")]
    public AudioClip errorAlertSfx;

    [Tooltip("Optional standalone prefab fallback if no WinXpAlertManager is found.")]
    public GameObject customErrorAlertPfb;

    [Header("Success Alert (Valid Key)")]
    [Tooltip("Title of the WinXP alert shown on successful activation.")]
    public string successAlertTitle = "Licence Activated";

    [TextArea(2, 5)]
    [Tooltip("Body content of the WinXP alert shown on successful activation.")]
    public string successAlertContent = "Thank you! Your product licence has been successfully activated.\nAll features are now unlocked.";

    [Tooltip("Sound played directly when entering the right licence key.")]
    public AudioClip activationSfx;

    [Tooltip("Sound played when the success alert is spawned.")]
    public AudioClip successAlertSfx;

    [Tooltip("Optional standalone prefab fallback if no WinXpAlertManager is found.")]
    public GameObject customSuccessAlertPfb;

    // Throwaway manager created by ResolveAlertManager when the scene has none.
    private WinXpAlertManager _ownedAlertManager;

    // Track spawned alerts for lifetime management
    private readonly List<WinXPMessageAlert> _spawnedAlerts = new List<WinXPMessageAlert>();

    // ─────────────────────────────────────────────────────────────
    //  Persistence Configuration
    // ─────────────────────────────────────────────────────────────

    [Header("Storage")]
    [Tooltip("File name of the JSON save file stored in Application.persistentDataPath.")]
    public string saveFileName = "licence_data.json";

    [Tooltip("Also mirror activation state in PlayerPrefs for fast persistence.")]
    public bool usePlayerPrefsBackup = true;

    private const string PrefsKey = "Licence_Activated_State";

    // ─────────────────────────────────────────────────────────────
    //  Achievements
    // ─────────────────────────────────────────────────────────────

    [Header("Achievements")]
    [Tooltip("Achievement granted when a valid product licence key is entered.")]
    public AchievementData licenceActivatedAchievement;

    // ─────────────────────────────────────────────────────────────
    //  Events
    // ─────────────────────────────────────────────────────────────

    [Header("Events")]
    [Tooltip("Fires when the licence is successfully activated.")]
    public UnityEvent onLicenceActivated = new UnityEvent();

    [Tooltip("Fires when licence activation fails due to an invalid key.")]
    public UnityEvent onLicenceFailed = new UnityEvent();

    [Tooltip("Fires when the input field is cleared via the clear button.")]
    public UnityEvent onInputCleared = new UnityEvent();

    [Tooltip("Fires on initialization with the loaded activation state (true if active, false otherwise).")]
    public UnityEvent<bool> onLicenceStateInitialized = new UnityEvent<bool>();

    // ─────────────────────────────────────────────────────────────
    //  Data Structures
    // ─────────────────────────────────────────────────────────────

    [Serializable]
    public class LicenceSaveData
    {
        public bool isActivated;
        public string activatedKey = "";
        public string activationTimestamp = "";
    }

    // ─────────────────────────────────────────────────────────────
    //  Properties & Aliases
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Static property to check if the licence is currently activated.
    /// Safe to call anywhere at any time (loads from save if not yet initialized).
    /// </summary>
    public static bool IsLicenceActivated
    {
        get
        {
            if (!_isCached)
            {
                LoadStaticCache();
            }
            return _cachedActivated;
        }
    }

    /// <summary>Alias for <see cref="IsLicenceActivated"/>.</summary>
    public static bool IsLicenseActivated => IsLicenceActivated;

    /// <summary>Alias for <see cref="IsLicenceActivated"/>.</summary>
    public static bool IsLicenceEnabled => IsLicenceActivated;

    /// <summary>Alias for <see cref="IsLicenceActivated"/>.</summary>
    public static bool IsLicenseEnabled => IsLicenceActivated;

    /// <summary>Instance-level property checking activation state.</summary>
    public bool IsActivated => IsLicenceActivated;

    public GameObject ActivatedLicenceObject
    {
        get => ActivfatedLicenceObject;
        set => ActivfatedLicenceObject = value;
    }

    public GameObject activatedLicenceObject
    {
        get => ActivfatedLicenceObject;
        set => ActivfatedLicenceObject = value;
    }

    public GameObject licenceWaterMark
    {
        get => LicenceWaterMark;
        set => LicenceWaterMark = value;
    }

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private bool _settingsEventsHooked;
    private bool _lastWindowRootActive;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, false);

        if (submitBtn)
        {
            submitBtn.onClick.AddListener(SubmitInput);
        }

        if (clearBtn)
        {
            clearBtn.onClick.AddListener(ClearInput);
        }

        if (inputField && submitOnEndEdit)
        {
            inputField.onSubmit.AddListener(HandleInputSubmit);
        }
    }

    private void Start()
    {
        RefreshLicenceState();
        HookSettingsEvents();
        _lastWindowRootActive = windowRoot && windowRoot.activeInHierarchy;
    }

    private void OnEnable()
    {
        HookSettingsEvents();
    }

    private void Update()
    {
        if (windowRoot)
        {
            bool currentActive = windowRoot.activeInHierarchy;
            if (_lastWindowRootActive && !currentActive)
            {
                DestroySpawnedAlerts();
            }
            _lastWindowRootActive = currentActive;
        }
    }

    private void OnDisable()
    {
        UnhookSettingsEvents();
        DestroySpawnedAlerts();
    }

    private void OnDestroy()
    {
        UnhookSettingsEvents();
        DestroySpawnedAlerts();

        if (submitBtn)
        {
            submitBtn.onClick.RemoveListener(SubmitInput);
        }

        if (clearBtn)
        {
            clearBtn.onClick.RemoveListener(ClearInput);
        }

        if (inputField)
        {
            inputField.onSubmit.RemoveListener(HandleInputSubmit);
        }

        if (_ownedAlertManager)
        {
            Destroy(_ownedAlertManager.gameObject);
            _ownedAlertManager = null;
        }

        if (Instance == this) Instance = null;
    }

    private void HookSettingsEvents()
    {
        if (_settingsEventsHooked || !SettingsWindowManager.Instance) return;
        SettingsWindowManager.Instance.OnWindowToggled += HandleSettingsWindowToggled;
        SettingsWindowManager.Instance.OnFolderOpened += HandleFolderOpened;
        _settingsEventsHooked = true;
    }

    private void UnhookSettingsEvents()
    {
        if (!_settingsEventsHooked || !SettingsWindowManager.Instance) return;
        SettingsWindowManager.Instance.OnWindowToggled -= HandleSettingsWindowToggled;
        SettingsWindowManager.Instance.OnFolderOpened -= HandleFolderOpened;
        _settingsEventsHooked = false;
    }

    private void HandleSettingsWindowToggled(bool isOpen)
    {
        if (!isOpen)
        {
            DestroySpawnedAlerts();
        }
    }

    private void HandleFolderOpened(int folderIndex)
    {
        if (windowRoot && !windowRoot.activeInHierarchy)
        {
            DestroySpawnedAlerts();
        }
    }

    /// <summary>
    /// Closes the licence window and destroys all associated spawned licence alerts.
    /// </summary>
    public void CloseWindow()
    {
        if (windowRoot)
        {
            windowRoot.SetActive(false);
        }
        DestroySpawnedAlerts();
    }

    // ─────────────────────────────────────────────────────────────
    //  Activation Logic
    // ─────────────────────────────────────────────────────────────

    private void HandleInputSubmit(string text)
    {
        SubmitInput();
    }

    /// <summary>
    /// Clears the input field text and refocuses it if active.
    /// </summary>
    public void ClearInput()
    {
        if (inputField)
        {
            inputField.text = string.Empty;
            inputField.ActivateInputField();
        }

        onInputCleared?.Invoke();
    }

    /// <summary>
    /// Reads text from <see cref="inputField"/> and validates it.
    /// </summary>
    public void SubmitInput()
    {
        string input = inputField ? inputField.text : string.Empty;
        TryActivate(input);
    }

    /// <summary>
    /// Tests a licence key string. If valid, activates the licence; otherwise spawns an error alert.
    /// </summary>
    /// <param name="key">The key string to test.</param>
    /// <returns>True if activation succeeded, false if invalid.</returns>
    public bool TryActivate(string key)
    {
        if (ValidateKey(key))
        {
            SetLicenceActivated(true, key);
            PlayActivationSfx();
            SpawnSuccessAlert();
            onLicenceActivated?.Invoke();

            if (closeWindowOnSuccess && windowRoot)
            {
                windowRoot.SetActive(false);
            }

            return true;
        }
        else
        {
            SpawnErrorAlert();
            onLicenceFailed?.Invoke();
            return false;
        }
    }

    /// <summary>
    /// Plays the sound effect configured for entering the right licence key.
    /// </summary>
    public void PlayActivationSfx()
    {
        if (!activationSfx) return;

        if (SFXManager.Instance)
        {
            SFXManager.Instance.PlayClip(activationSfx, is3D: false, playIndependently: true);
        }
        else
        {
            AudioSource.PlayClipAtPoint(activationSfx, Camera.main ? Camera.main.transform.position : transform.position);
        }
    }

    /// <summary>
    /// Validates whether a key matches configured valid keys.
    /// </summary>
    public bool ValidateKey(string key)
    {
        if (key == null) return false;

        string testKey = trimWhitespace ? key.Trim() : key;

        if (string.IsNullOrEmpty(testKey)) return false;

        if (acceptAnyNonEmptyKey) return true;

        if (validKeys == null || validKeys.Count == 0) return false;

        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (string validKey in validKeys)
        {
            if (string.IsNullOrEmpty(validKey)) continue;

            string target = trimWhitespace ? validKey.Trim() : validKey;
            if (string.Equals(testKey, target, comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Manually sets activation state, updates UI objects, and writes to persistent storage.
    /// </summary>
    public void SetLicenceActivated(bool activated, string key = "")
    {
        _cachedActivated = activated;
        _isCached = true;

        SaveLicenceState(activated, key);
        ApplyUIState(activated);

        if (activated && licenceActivatedAchievement && AchievementManager.Instance)
        {
            AchievementManager.Instance.GrantAchievement(licenceActivatedAchievement);
            h.Out("[LicenceWindowManager] Granted achievement '", licenceActivatedAchievement.achievementName, "'.");
        }
    }

    /// <summary>
    /// Applies visual GameObject visibility according to licence state:
    /// enabled <see cref="ActivfatedLicenceObject"/> and disabled <see cref="LicenceWaterMark"/> when active.
    /// </summary>
    public void ApplyUIState(bool activated)
    {
        if (ActivfatedLicenceObject)
        {
            ActivfatedLicenceObject.SetActive(activated);
        }

        if (LicenceWaterMark)
        {
            LicenceWaterMark.SetActive(!activated);
        }
    }

    /// <summary>
    /// Refreshes activation state from persistent storage and updates UI objects.
    /// </summary>
    public void RefreshLicenceState()
    {
        bool isAct = LoadLicenceState();
        _cachedActivated = isAct;
        _isCached = true;

        ApplyUIState(isAct);
        onLicenceStateInitialized?.Invoke(isAct);
    }

    // ─────────────────────────────────────────────────────────────
    //  Alert Manager & Canvas Wiring (Mirrors DesktopPetyaAppearance)
    // ─────────────────────────────────────────────────────────────

    /// <summary>The manager as it stands right now — never creates one. Null is a valid answer.</summary>
    private WinXpAlertManager AlertManager
    {
        get
        {
            if (alertManager) return alertManager;
            if (WinXpAlertManager.Instance) return WinXpAlertManager.Instance;

            // Instance is only set in the manager's Awake, so a manager sitting in the scene disabled
            // (or one whose Awake simply hasn't run yet) would otherwise be missed and duplicated.
            return FindFirstObjectByType<WinXpAlertManager>(FindObjectsInactive.Include);
        }
    }

    /// <summary>
    /// Resolves the active WinXpAlertManager. If none is found in the scene, creates a temporary
    /// instance so that alerts can always be properly positioned and managed on the UI canvas.
    /// </summary>
    private WinXpAlertManager ResolveAlertManager()
    {
        WinXpAlertManager manager = AlertManager;

        if (!manager)
        {
            GameObject go = new GameObject("WinXpAlertManager (Licence)");
            manager = go.AddComponent<WinXpAlertManager>();
            _ownedAlertManager = manager;

            h.Out("LicenceWindowManager: no WinXpAlertManager in the scene — created a temporary one.");
        }

        return manager;
    }

    /// <summary>The container every spawned popup goes inside. Falls back to the canvas root.</summary>
    private RectTransform SpawnParent
    {
        get
        {
            if (alertParent) return alertParent;
            WinXpAlertManager manager = ResolveAlertManager();
            return manager ? manager.SpawnParent : null;
        }
    }

    /// <summary>Rect of the root canvas the alert measures its screen points against.</summary>
    private RectTransform CanvasRect
    {
        get
        {
            if (alertParent)
            {
                Canvas parentCanvas = alertParent.GetComponentInParent<Canvas>();
                if (parentCanvas)
                {
                    Canvas root = parentCanvas.rootCanvas ? parentCanvas.rootCanvas : parentCanvas;
                    return root.transform as RectTransform;
                }
            }
            WinXpAlertManager manager = ResolveAlertManager();
            return manager ? manager.CanvasRect : null;
        }
    }

    /// <summary>Camera the canvas renders through, or null in Screen Space - Overlay.</summary>
    private Camera UiCamera
    {
        get
        {
            if (alertParent)
            {
                Canvas parentCanvas = alertParent.GetComponentInParent<Canvas>();
                if (parentCanvas)
                {
                    Canvas root = parentCanvas.rootCanvas ? parentCanvas.rootCanvas : parentCanvas;
                    return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
                }
            }
            WinXpAlertManager manager = ResolveAlertManager();
            return manager ? manager.UiCamera : null;
        }
    }

    /// <summary>
    /// Hands the manager the canvas to spawn on and makes it look it up.
    /// </summary>
    private void ResolveCanvas()
    {
        WinXpAlertManager manager = ResolveAlertManager();
        if (!manager) return;

        manager.popupParentTag = string.Empty;

        if (alertParent)
        {
            manager.popupParent = alertParent;
        }
        else if (!manager.popupParent)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (!canvas) canvas = FindFirstObjectByType<Canvas>();
            if (canvas)
            {
                Canvas root = canvas.rootCanvas ? canvas.rootCanvas : canvas;
                manager.popupParent = root.transform as RectTransform;
            }
        }

        manager.InvalidateCanvas();
        manager.ResolveCanvas();
    }

    /// <summary>Centre of the screen in pixels — what alerts are positioned against.</summary>
    private static Vector2 ScreenCenter => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

    /// <summary>Puts a spawned window at a screen point, the same way the cutscene / alert manager places its own.</summary>
    private void PlaceAtScreenPoint(RectTransform rt, Vector2 screenPoint)
    {
        RectTransform canvasRect = CanvasRect;
        if (!rt || !canvasRect) return;

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                canvasRect, screenPoint, UiCamera, out Vector3 world))
        {
            rt.position = world;
        }
    }

    /// <summary>Resolves the alert prefab to spawn (custom override or alert manager fallback).</summary>
    private GameObject ResolveAlertPrefab(bool isError)
    {
        GameObject custom = isError ? customErrorAlertPfb : customSuccessAlertPfb;
        if (custom) return custom;

        // Fallback to the other custom alert prefab if available
        GameObject fallbackCustom = isError ? customSuccessAlertPfb : customErrorAlertPfb;
        if (fallbackCustom) return fallbackCustom;

        WinXpAlertManager manager = AlertManager;
        if (manager && manager.pfbs != null && manager.pfbs.Count > 0)
        {
            WinXPMessageAlert pfb = h.RandChoice(manager.pfbs);
            if (pfb) return pfb.gameObject;
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────
    //  Alert Popups (WinXP)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns the configured error alert popup.
    /// </summary>
    public WinXPMessageAlert SpawnErrorAlert()
    {
        return SpawnAlert(true, errorAlertTitle, errorAlertContent, errorAlertSfx);
    }

    /// <summary>
    /// Spawns the configured success alert popup.
    /// </summary>
    public WinXPMessageAlert SpawnSuccessAlert()
    {
        return SpawnAlert(false, successAlertTitle, successAlertContent, successAlertSfx);
    }

    /// <summary>
    /// Spawns an alert popup window following the DesktopPetyaAppearance cutscene pattern:
    /// resolves the manager and canvas container, instantiates under SpawnParent, configures texts,
    /// sets screen position, and brings it to front.
    /// </summary>
    public WinXPMessageAlert SpawnAlert(bool isError, string title = null, string content = null, AudioClip sfx = null, Vector2? screenPosition = null)
    {
        string actualTitle = title ?? (isError ? errorAlertTitle : successAlertTitle);
        string actualContent = content ?? (isError ? errorAlertContent : successAlertContent);
        AudioClip actualSfx = sfx ? sfx : (isError ? errorAlertSfx : successAlertSfx);

        GameObject prefab = ResolveAlertPrefab(isError);
        if (!prefab)
        {
            WinXpAlertManager mgr = ResolveAlertManager();
            if (mgr)
            {
                WinXPMessageAlert spawned = mgr.SpawnAlert(screenPosition, actualTitle, actualContent, actualSfx);
                if (spawned)
                {
                    spawned.transform.SetAsLastSibling();
                    _spawnedAlerts.Add(spawned);
                    return spawned;
                }
            }

            h.Out($"[LicenceWindowManager] {(isError ? "Error" : "Success")}: {actualTitle} - {actualContent}");
            return null;
        }

        ResolveCanvas();

        RectTransform parent = SpawnParent;
        if (!parent)
        {
            h.Out("[LicenceWindowManager] No Canvas or SpawnParent found to spawn alert on.");
            return null;
        }

        GameObject instance = Instantiate(prefab, parent);
        WinXPMessageAlert alert = instance.GetComponent<WinXPMessageAlert>();
        if (!alert)
        {
            alert = instance.AddComponent<WinXPMessageAlert>();
        }

        // Configure text before Start() completes display
        alert.SetText(
            string.IsNullOrEmpty(actualTitle) ? alert.titleText : actualTitle,
            string.IsNullOrEmpty(actualContent) ? alert.alertText : actualContent);

        if (actualSfx)
        {
            alert.alertSFX = new List<AudioClip> { actualSfx };
        }

        // Position on screen
        Vector2 targetPoint;
        if (screenPosition.HasValue)
        {
            targetPoint = screenPosition.Value;
        }
        else if (randomAlertPosition)
        {
            WinXpAlertManager manager = ResolveAlertManager();
            targetPoint = manager ? manager.RandomScreenPoint() : ScreenCenter;
        }
        else
        {
            targetPoint = ScreenCenter + alertScreenOffset;
        }

        PlaceAtScreenPoint(alert.transform as RectTransform, targetPoint);
        alert.transform.SetAsLastSibling();

        _spawnedAlerts.Add(alert);
        return alert;
    }

    /// <summary>
    /// Destroys all alerts spawned by this licence manager.
    /// </summary>
    public void DestroySpawnedAlerts()
    {
        for (int i = _spawnedAlerts.Count - 1; i >= 0; i--)
        {
            if (_spawnedAlerts[i] && _spawnedAlerts[i].gameObject)
            {
                Destroy(_spawnedAlerts[i].gameObject);
            }
        }
        _spawnedAlerts.Clear();
    }

    // ─────────────────────────────────────────────────────────────
    //  Persistence (JSON & PlayerPrefs)
    // ─────────────────────────────────────────────────────────────

    private string GetSaveFilePath()
    {
        string filename = string.IsNullOrEmpty(saveFileName) ? "licence_data.json" : saveFileName;
        return Path.Combine(Application.persistentDataPath, filename);
    }

    private void SaveLicenceState(bool activated, string key)
    {
        try
        {
            LicenceSaveData data = new LicenceSaveData
            {
                isActivated = activated,
                activatedKey = key,
                activationTimestamp = DateTime.UtcNow.ToString("o")
            };

            string json = JsonUtility.ToJson(data, true);
            string path = GetSaveFilePath();

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, json, Encoding.UTF8);
            h.Out($"[LicenceWindowManager] Saved licence state to '{path}'. Activated = {activated}");
        }
        catch (Exception ex)
        {
            h.Out($"[LicenceWindowManager] Failed to write JSON save file: {ex.Message}");
        }

        if (usePlayerPrefsBackup)
        {
            PlayerPrefs.SetInt(PrefsKey, activated ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private bool LoadLicenceState()
    {
        string path = GetSaveFilePath();

        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                LicenceSaveData data = JsonUtility.FromJson<LicenceSaveData>(json);
                if (data != null)
                {
                    return data.isActivated;
                }
            }
            catch (Exception ex)
            {
                h.Out($"[LicenceWindowManager] Failed to read licence JSON at '{path}': {ex.Message}");
            }
        }

        if (usePlayerPrefsBackup && PlayerPrefs.HasKey(PrefsKey))
        {
            return PlayerPrefs.GetInt(PrefsKey, 0) == 1;
        }

        return false;
    }

    private static void LoadStaticCache()
    {
        string path = Path.Combine(Application.persistentDataPath, "licence_data.json");
        bool activated = false;

        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                LicenceSaveData data = JsonUtility.FromJson<LicenceSaveData>(json);
                if (data != null)
                {
                    activated = data.isActivated;
                }
            }
            catch
            {
                // Fallback to PlayerPrefs below
            }
        }
        else if (PlayerPrefs.HasKey(PrefsKey))
        {
            activated = PlayerPrefs.GetInt(PrefsKey, 0) == 1;
        }

        _cachedActivated = activated;
        _isCached = true;
    }

    // ─────────────────────────────────────────────────────────────
    //  Context Menu & Debug Tools
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Static helper to reset licence status everywhere:
    /// wipes the JSON save file, removes the PlayerPrefs entry, clears the static cache,
    /// and refreshes any active scene LicenceWindowManager instance.
    /// </summary>
    public static void ResetLicenceStatus()
    {
        _cachedActivated = false;
        _isCached = true;

        if (PlayerPrefs.HasKey(PrefsKey))
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        string path = Path.Combine(Application.persistentDataPath, "licence_data.json");
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
                h.Out($"[LicenceWindowManager] Deleted licence save file at '{path}'.");
            }
            catch (Exception ex)
            {
                h.Out($"[LicenceWindowManager] Could not delete licence file at '{path}': {ex.Message}");
            }
        }

        if (Instance)
        {
            Instance.ApplyUIState(false);
            Instance.onLicenceStateInitialized?.Invoke(false);
        }
    }

    [ContextMenu("Activate Licence (Debug)")]
    public void DebugActivateLicence()
    {
        SetLicenceActivated(true, "DEBUG-ACTIVATION-KEY");
        h.Out("[LicenceWindowManager] Licence activated via Debug context menu.");
    }

    [ContextMenu("Reset Licence (Debug)")]
    public void DebugResetLicence()
    {
        ResetLicenceStatus();
        h.Out("[LicenceWindowManager] Licence reset via Debug context menu.");
    }
}
