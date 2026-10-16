using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Easter egg component attached to a unit.
/// When the camera is zoomed in very closely, the unit becomes interactive/clickable and shows the click cursor on hover.
/// Clicking the unit triggers its <see cref="AnimationPreferences.Type.OnClick"/> animation controller and plays a per-click SFX (Windows XP Balloon).
/// Clicking the unit <see cref="clicksPerSfx"/> times plays the next sound from <see cref="sfxSequence"/>.
/// When the very last sound in the sequence is played, an exposed achievement is granted.
/// If the player clicks again after the last sound, the audio sequence starts over from the beginning.
/// </summary>
[AddComponentMenu("MyFriendPetya/Units/Unit Easter Egg")]
public class UnitEasterEgg : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Zoom Requirements")]
    [Tooltip("Maximum camera distance in world units for the unit to be considered zoomed in closely (Perspective camera).")]
    public float maxZoomDistance = 25f;

    [Tooltip("Maximum orthographic camera size for the unit to be considered zoomed in closely (Orthographic camera).")]
    public float maxOrthographicSize = 5f;

    [Tooltip("Explicit camera to use for zoom check and cursor raycasting. Defaults to Camera.main.")]
    public Camera targetCamera;

    [Header("Per-Click SFX")]
    [Tooltip("Sound played on every single valid click. Auto-loads Windows XP Balloon if unassigned.")]
    public AudioClip perClickSfx;

    [Header("Click Sequence & SFX")]
    [Tooltip("Number of clicks required to trigger each consecutive SFX in the sequence.")]
    [Min(1)] public int clicksPerSfx = 5;

    [Tooltip("Ordered list of audio clips played sequentially every N clicks.")]
    public List<AudioClip> sfxSequence = new List<AudioClip>();

    [Tooltip("Volume multiplier for played sound effects.")]
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Tooltip("Whether the SFX should be spatialized as 3D audio at the unit's position.")]
    public bool play3DSound = true;

    [Tooltip("Maximum 3D hearing distance for the audio source when play3DSound is true. If 0 or negative, uses SFXManager default.")]
    public float maxAudioDistance = 0f;

    [Tooltip("If true, after playing the final sound effect, subsequent clicks will restart the sequence from the beginning.")]
    public bool loopSequence = true;

    [Header("Animation")]
    [Tooltip("AnimationController of type OnClick to play each time the unit is clicked. Auto-resolved in children if empty.")]
    public AnimationControllerBase onClickAnimationController;

    [Header("Cursor Hover")]
    [Tooltip("Whether to change the cursor icon to the click/button icon when the unit is hovered in a valid zoomed-in state.")]
    public bool changeCursorOnHover = true;

    [Header("Achievement")]
    [Tooltip("Exposed AchievementData ScriptableObject granted after the final SFX in the sequence is played.")]
    public AchievementData achievement;

    [Tooltip("Optional achievement name lookup in AchievementManager if AchievementData asset is not directly referenced.")]
    public string achievementName;

    [Header("Raycast & Picking Setup")]
    [Tooltip("Layer mask used when raycasting to pick this unit on mouse click or hover.")]
    public LayerMask clickLayerMask = ~0;

    [Tooltip("Maximum raycast distance for clicking and hover detection.")]
    public float maxRayDistance = 1000f;

    [Header("Events & Feedback")]
    [Tooltip("Fired each time the unit is clicked while zoomed in. Passes current clicks towards next SFX (1-indexed).")]
    public UnityEvent<int> onUnitClicked;

    [Tooltip("Fired each time an SFX in the sequence is played. Passes the sfx index (0-based) and the played clip.")]
    public UnityEvent<int, AudioClip> onSfxPlayed;

    [Tooltip("Fired when the final SFX finishes triggering and the achievement is granted.")]
    public UnityEvent onEasterEggCompleted;

    // Runtime state
    private int _currentClickCount = 0;
    private int _currentSfxIndex = 0;
    private bool _isHovered = false;
    private int _lastHandledFrame = -1;

    private Collider[] _attachedColliders;
    private Collider2D[] _attachedColliders2D;
    private Unit _unit;

    /// <summary>True when the active camera is zoomed in closely enough to interact with this unit.</summary>
    public bool IsZoomedClosely => CheckIsZoomedClosely();

    /// <summary>True when the unit is active, zoomed closely, and eligible for clicks and cursor hover.</summary>
    public bool IsInValidState => CheckIsValidState();

    /// <summary>Number of clicks accumulated towards the next SFX in the sequence.</summary>
    public int CurrentClicksInBatch => _currentClickCount;

    /// <summary>Index of the next SFX to be played in <see cref="sfxSequence"/>.</summary>
    public int CurrentSfxIndex => _currentSfxIndex;

    private void Awake()
    {
        _unit = GetComponent<Unit>();
        CacheColliders();
        CacheAnimationController();
        EnsureDefaultClickSfx();
    }

    private void OnValidate()
    {
        if (clicksPerSfx < 1) clicksPerSfx = 1;
    }

    private void OnDisable()
    {
        SetHoverState(false);
    }

    private void OnDestroy()
    {
        SetHoverState(false);
    }

    /// <summary>
    /// Ensures a default per-click SFX (Windows XP Balloon) is loaded if not explicitly assigned.
    /// </summary>
    private void EnsureDefaultClickSfx()
    {
        if (perClickSfx == null)
        {
            perClickSfx = Resources.Load<AudioClip>("PROJECT/Audio/WinXP/Windows XP Balloon");
        }
    }

    /// <summary>
    /// Caches all 3D and 2D colliders attached to this object or its children.
    /// </summary>
    public void CacheColliders()
    {
        _attachedColliders = GetComponentsInChildren<Collider>(true);
        _attachedColliders2D = GetComponentsInChildren<Collider2D>(true);
    }

    /// <summary>
    /// Searches for an <see cref="AnimationControllerBase"/> of type <see cref="AnimationPreferences.Type.OnClick"/>
    /// on this object or any of its children.
    /// </summary>
    public void CacheAnimationController()
    {
        if (onClickAnimationController) return;

        foreach (AnimationControllerBase controller in GetComponentsInChildren<AnimationControllerBase>(true))
        {
            if (controller == null) continue;
            if (controller.type == AnimationPreferences.Type.OnClick ||
                (controller.targetTypes != null && controller.targetTypes.Contains(AnimationPreferences.Type.OnClick)))
            {
                onClickAnimationController = controller;
                break;
            }
        }
    }

    private void Update()
    {
        // 1. Maintain hover state and cursor icon
        if (changeCursorOnHover)
        {
            UpdateHoverState();
        }

        // 2. Check for click input
        if (!WasLeftMouseButtonPressed()) return;

        // Ignore clicks over UI
        if (IsPointerOverUI()) return;

        // Check valid state (zoomed in + alive)
        if (!CheckIsValidState()) return;

        // Check if cursor ray hits any collider belonging to this unit
        if (RaycastHitsThisUnit())
        {
            HandleClick();
        }
    }

    /// <summary>
    /// Updates the hover state and syncs with <see cref="CursorIconManager"/>.
    /// </summary>
    private void UpdateHoverState()
    {
        bool shouldHover = CheckIsValidState() && !IsPointerOverUI() && RaycastHitsThisUnit();
        SetHoverState(shouldHover);
    }

    /// <summary>
    /// Updates the hover registration with <see cref="CursorIconManager"/>.
    /// </summary>
    private void SetHoverState(bool hovering)
    {
        if (_isHovered == hovering) return;
        _isHovered = hovering;

        if (CursorIconManager.Instance != null)
        {
            CursorIconManager.Instance.SetButtonHover(hovering);
        }
    }

    /// <summary>
    /// Checks whether the unit is currently in a valid interactive state (active and zoomed in).
    /// </summary>
    public bool CheckIsValidState()
    {
        if (_unit != null && _unit.state != Unit.State.active) return false;
        if (!gameObject.activeInHierarchy) return false;

        return CheckIsZoomedClosely();
    }

    /// <summary>
    /// Returns true if the pointer is currently over a UI element.
    /// </summary>
    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>
    /// Returns true if the left mouse button was pressed this frame across Input System or legacy Input.
    /// </summary>
    private bool WasLeftMouseButtonPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
#endif
        return Input.GetMouseButtonDown(0);
    }

    /// <summary>
    /// Resolves the camera to use for distance checking and cursor raycasting.
    /// </summary>
    public Camera ResolveCamera()
    {
        if (targetCamera && targetCamera.isActiveAndEnabled) return targetCamera;
        if (CameraController.Instance && CameraController.Instance.cam && CameraController.Instance.cam.isActiveAndEnabled)
            return CameraController.Instance.cam;
        if (Camera.main && Camera.main.isActiveAndEnabled)
            return Camera.main;

        return targetCamera;
    }

    /// <summary>
    /// Checks whether the camera is close enough to the unit to satisfy the zoom requirement.
    /// </summary>
    public bool CheckIsZoomedClosely()
    {
        Camera cam = ResolveCamera();
        if (!cam) return false;

        if (cam.orthographic)
        {
            return cam.orthographicSize <= maxOrthographicSize;
        }

        float distance = Vector3.Distance(cam.transform.position, transform.position);
        return distance <= maxZoomDistance;
    }

    /// <summary>
    /// Casts a ray from the cursor position and checks if any hit collider belongs to this unit.
    /// </summary>
    private bool RaycastHitsThisUnit()
    {
        Camera cam = ResolveCamera();
        if (!cam) return false;

        Vector2 mousePos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) mousePos = Mouse.current.position.ReadValue();
        else mousePos = Input.mousePosition;
#else
        mousePos = Input.mousePosition;
#endif

        Ray ray = cam.ScreenPointToRay(mousePos);

        // 1. 3D physics raycast (explicitly include trigger colliders)
        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance, clickLayerMask, QueryTriggerInteraction.Collide);
        if (hits != null && hits.Length > 0)
        {
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (!hit.collider) continue;
                if (IsOwnCollider(hit.collider)) return true;
            }
        }

        // 2. 2D physics raycast fallback
        RaycastHit2D[] hits2D = Physics2D.GetRayIntersectionAll(ray, maxRayDistance, clickLayerMask);
        if (hits2D != null && hits2D.Length > 0)
        {
            foreach (RaycastHit2D hit2D in hits2D)
            {
                if (!hit2D.collider) continue;
                if (IsOwnCollider2D(hit2D.collider)) return true;
            }
        }

        return false;
    }

    private bool IsOwnCollider(Collider col)
    {
        if (!col) return false;
        if (col.transform.IsChildOf(transform) || col.gameObject == gameObject) return true;

        if (_attachedColliders != null)
        {
            for (int i = 0; i < _attachedColliders.Length; i++)
            {
                if (_attachedColliders[i] == col) return true;
            }
        }

        return false;
    }

    private bool IsOwnCollider2D(Collider2D col)
    {
        if (!col) return false;
        if (col.transform.IsChildOf(transform) || col.gameObject == gameObject) return true;

        if (_attachedColliders2D != null)
        {
            for (int i = 0; i < _attachedColliders2D.Length; i++)
            {
                if (_attachedColliders2D[i] == col) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Processes a valid click on this unit. Triggers OnClick animation, plays per-click SFX,
    /// increments click count, triggers sequence SFX every N clicks, and grants achievement on the last SFX.
    /// </summary>
    public void HandleClick()
    {
        // Avoid handling multiple click detections within the exact same frame
        if (Time.frameCount == _lastHandledFrame) return;
        _lastHandledFrame = Time.frameCount;

        if (!CheckIsValidState()) return;

        // 1. Trigger the OnClick animation controller
        PlayOnClickAnimation();

        // 2. Play per-click SFX (Windows XP Balloon)
        PlayPerClickSfx();

        // 3. Track click progression
        _currentClickCount++;
        onUnitClicked?.Invoke(_currentClickCount);

        if (_currentClickCount >= clicksPerSfx)
        {
            _currentClickCount = 0;
            PlayNextSfxAndAdvance();
        }
    }

    /// <summary>
    /// Plays the per-click SFX (Windows XP Balloon).
    /// </summary>
    private void PlayPerClickSfx()
    {
        if (perClickSfx == null) EnsureDefaultClickSfx();
        if (perClickSfx != null)
        {
            PlaySfxClip(perClickSfx);
        }
    }

    /// <summary>
    /// Plays the <see cref="AnimationPreferences.Type.OnClick"/> animation on the cached animation controller.
    /// </summary>
    public void PlayOnClickAnimation()
    {
        if (!onClickAnimationController) CacheAnimationController();

        if (onClickAnimationController != null)
        {
            onClickAnimationController.StartCoroutine(onClickAnimationController.PlayAnimations());
        }
    }

    /// <summary>
    /// Plays the current SFX in the sequence, advances the index, grants achievement on the last clip,
    /// and resets the index if looping.
    /// </summary>
    private void PlayNextSfxAndAdvance()
    {
        if (sfxSequence == null || sfxSequence.Count == 0)
        {
            h.Out($"[UnitEasterEgg] No SFX assigned in sfxSequence on '{gameObject.name}'. Granting achievement directly.");
            GrantAchievement();
            onEasterEggCompleted?.Invoke();
            return;
        }

        // If the index was at or past the end, wrap around to the beginning
        if (_currentSfxIndex >= sfxSequence.Count)
        {
            _currentSfxIndex = 0;
        }

        AudioClip clipToPlay = sfxSequence[_currentSfxIndex];
        int playedIndex = _currentSfxIndex;

        PlaySfxClip(clipToPlay);
        onSfxPlayed?.Invoke(playedIndex, clipToPlay);

        // Check if we just played the very last clip from the list
        bool wasLastClip = (playedIndex == sfxSequence.Count - 1);
        if (wasLastClip)
        {
            GrantAchievement();
            onEasterEggCompleted?.Invoke();
        }

        // Advance to next index
        _currentSfxIndex++;
        if (_currentSfxIndex >= sfxSequence.Count && loopSequence)
        {
            _currentSfxIndex = 0;
        }
    }

    /// <summary>
    /// Plays an audio clip via <see cref="SFXManager"/> with spatialization and volume settings.
    /// </summary>
    private void PlaySfxClip(AudioClip clip)
    {
        if (clip == null) return;

        float? maxDist = maxAudioDistance > 0f ? (float?)maxAudioDistance : null;

        if (SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayClipIndependently(
                clip,
                volumeIn: sfxVolume,
                is3D: play3DSound,
                atTransform: transform,
                maxDistance: maxDist
            );
        }
        else
        {
            if (play3DSound)
            {
                AudioSource.PlayClipAtPoint(clip, transform.position, sfxVolume);
            }
            else
            {
                // Fallback 2D playback
                GameObject tempGo = new GameObject("TempEasterEggAudio");
                AudioSource src = tempGo.AddComponent<AudioSource>();
                src.clip = clip;
                src.volume = sfxVolume;
                src.spatialBlend = 0f;
                src.Play();
                Destroy(tempGo, clip.length + 0.1f);
            }
        }
    }

    /// <summary>
    /// Grants the exposed achievement via <see cref="AchievementManager"/> or direct asset unlocking.
    /// </summary>
    public void GrantAchievement()
    {
        if (achievement != null)
        {
            if (AchievementManager.Instance != null)
            {
                AchievementManager.Instance.GrantAchievement(achievement);
            }
            else
            {
                achievement.Unlock();
            }
            h.Out($"[UnitEasterEgg] Granted achievement '{achievement.achievementName}' from '{gameObject.name}'!");
        }
        else if (!string.IsNullOrEmpty(achievementName))
        {
            if (AchievementManager.Instance != null)
            {
                AchievementManager.Instance.GrantAchievementByName(achievementName);
            }
            h.Out($"[UnitEasterEgg] Granted achievement by name '{achievementName}' from '{gameObject.name}'!");
        }
        else
        {
            h.Out($"[UnitEasterEgg] Sequence completed on '{gameObject.name}', but no AchievementData or achievementName was assigned.");
        }
    }

    /// <summary>
    /// Resets the click and SFX sequence progression.
    /// </summary>
    [ContextMenu("Reset Easter Egg Progression")]
    public void ResetProgression()
    {
        _currentClickCount = 0;
        _currentSfxIndex = 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  EventSystem / Pointer Click, Enter & Exit Integration
    // ─────────────────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
        if (!CheckIsValidState()) return;

        HandleClick();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (changeCursorOnHover && CheckIsValidState())
        {
            SetHoverState(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (changeCursorOnHover)
        {
            SetHoverState(false);
        }
    }

    private void OnMouseEnter()
    {
        if (changeCursorOnHover && CheckIsValidState())
        {
            SetHoverState(true);
        }
    }

    private void OnMouseExit()
    {
        if (changeCursorOnHover)
        {
            SetHoverState(false);
        }
    }

    private void OnMouseDown()
    {
        if (!CheckIsValidState()) return;
        HandleClick();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, 1.2f);
    }
#endif
}
