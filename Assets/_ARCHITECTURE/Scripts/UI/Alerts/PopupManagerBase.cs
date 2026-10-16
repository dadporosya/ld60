using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Generic base for anything that spawns popups on the UI canvas — Win XP alerts, adware windows,
/// toasts, tooltips. Subclass it with the concrete popup component and you get placement, a
/// continuous spawn loop, difficulty scaling, spawn sounds and hard safety caps for free:
///
/// <code>
/// public class MyPopupManager : PopupManagerBase&lt;MyPopup&gt; { }
///
/// MyPopupManager.Instance.SpawnPopup();          // random spot, random prefab
/// MyPopupManager.Instance.StartSpawning();       // continuous storm
/// </code>
///
/// <b>One-off spawns.</b> <see cref="SpawnPopup(Vector2?, AudioClip, Action{TPopup})"/> picks a random
/// prefab from <see cref="pfbs"/>, parents it under <see cref="SpawnParent"/> and places it at the
/// given screen position (or at a random point on screen when none is given). Subclasses fill the
/// popup in by overriding <see cref="ConfigurePopup"/>; callers can pass a per-spawn configure
/// callback on top of that.
///
/// <b>Continuous spawning.</b> With <see cref="continuousSpawning"/> on (or after a
/// <see cref="StartSpawning()"/> call) the manager pays out popups on its own. The gap between two of
/// them is <see cref="CurrentSpawnRate"/> seconds multiplied by a random factor drawn from
/// [<see cref="minDistributionSpawnRate"/>, <see cref="maxDistributionSpawnRate"/>], so they never
/// feel metronomic. <see cref="SetSpawnRate"/> / <see cref="RampSpawnRate"/> change the rate while the
/// loop runs — ramping it down is what turns a drip into a storm.
///
/// <b>Difficulty.</b> With <see cref="dependsOnDifficulty"/> on, <see cref="spawnRate"/> is ignored and
/// the gap is read from <see cref="DifficultyManager"/> instead: <see cref="spawnRateAtMinDifficulty"/>
/// at difficulty 0, <see cref="spawnRateAtMaxDifficulty"/> at 100, linearly in between. The running
/// loop re-reads it before every gap, so a difficulty change takes effect immediately.
///
/// <b>Spawn safety.</b> Everything is hard-capped: <see cref="maxTotalPopups"/> for a whole run,
/// <see cref="maxSimultaneousPopups"/> on screen at once (the oldest is trimmed away) and
/// <see cref="maxPopupsPerFrame"/> per frame. Spawn sounds are throttled by
/// <see cref="minSpawnSoundGap"/>, so a 50-per-second storm still spawns visually but stays listenable.
/// </summary>
public abstract class PopupManagerBase<TPopup> : MonoBehaviour where TPopup : Component
{
    // ================================================================ Content

    [Header("Content")]
    [Tooltip("Popup prefabs. One is picked at random per spawn.")]
    public List<TPopup> pfbs = new List<TPopup>();

    [Tooltip("Sounds played when a popup spawns. Empty = no spawn sound from the manager (a popup that " +
             "plays its own sound still does).")]
    [FormerlySerializedAs("defaultSfx")]
    public List<AudioClip> spawnSfx = new List<AudioClip>();

    // ================================================================ Continuous spawning

    [Header("Continuous spawning")]
    [Tooltip("Start paying out popups on their own as soon as the manager wakes up. Off = popups only " +
             "appear when something calls SpawnPopup() or StartSpawning().")]
    public bool continuousSpawning = false;

    [Tooltip("Seconds between two popups. This is the average — every actual gap is this value times a " +
             "random factor from the distribution below. Ignored while 'depends on difficulty' is on.")]
    public float spawnRate = 2f;

    [Tooltip("Lower end of the random factor the gap is multiplied by. 1 = no variation, 0.5 = a gap " +
             "can come out twice as fast as the spawn rate.")]
    public float minDistributionSpawnRate = 0.85f;

    [Tooltip("Upper end of the random factor the gap is multiplied by.")]
    public float maxDistributionSpawnRate = 1.15f;

    [Tooltip("Hard floor on the gap between two popups, in seconds. Keep it small — the real throttle " +
             "is max popups per frame, not this.")]
    public float minSpawnRate = 0.02f;

    [Tooltip("THE speed limit. Once the gap drops below one frame the loop spawns several popups in " +
             "the same frame, and this caps how many. 3 at 60fps = up to 180 popups/second — the total " +
             "budget below is what actually ends a storm.")]
    [FormerlySerializedAs("maxAlertsPerFrame")]
    public int maxPopupsPerFrame = 3;

    // ================================================================ Difficulty

    [Header("Difficulty scaling")]
    [Tooltip("Read the spawn rate from DifficultyManager instead of the fixed 'spawn rate' above. " +
             "The two rates below are the ends of the scale; the loop re-reads the difficulty before " +
             "every gap, so changing it mid-run takes effect immediately.")]
    public bool dependsOnDifficulty = false;

    [Tooltip("Seconds between popups at difficulty 0. Normally the SLOW end (a bigger number).")]
    public float spawnRateAtMinDifficulty = 4f;

    [Tooltip("Seconds between popups at difficulty 100. Normally the FAST end (a smaller number).")]
    public float spawnRateAtMaxDifficulty = 0.5f;

    // ================================================================ Limits

    [Header("Limits")]
    [Tooltip("HARD CAP on how many popups may ever be created between two ResetBudget() calls. Once " +
             "reached the spawn loop shuts down for good. 0 = unlimited.")]
    [FormerlySerializedAs("maxTotalAlerts")]
    public int maxTotalPopups = 100;

    [Tooltip("How many popups may be on screen at once. The oldest is removed when exceeded. " +
             "0 = unlimited.")]
    [FormerlySerializedAs("maxSimultaneousAlerts")]
    public int maxSimultaneousPopups = 40;

    [Tooltip("Remove overflowing popups silently instead of closing them properly. At high rates the " +
             "close sounds would otherwise become one long noise wall.")]
    public bool silentTrim = true;

    [Tooltip("Minimum seconds between two popup spawn SOUNDS. Popups spawning closer together than " +
             "this are muted. 0 = every popup plays its sound.")]
    [FormerlySerializedAs("minAlertSoundGap")]
    public float minSpawnSoundGap = 0.12f;

    // ================================================================ Placement

    [Header("Placement")]
    [Tooltip("Padding (in pixels) kept between a spawned popup and the screen edges. Measured against " +
             "the popup's VISIBLE bounds, not its pivot, so a prefab whose window is offset from its " +
             "root transform is still fully on screen.")]
    public float screenMargin = 120f;

    [Tooltip("Keep the whole popup inside the screen. This measures what the popup actually draws " +
             "(every child, not just the root rect) and places it so none of it hangs off an edge. " +
             "Turn OFF only for a popup that is MEANT to be half off screen — with it off, placement " +
             "goes back to putting the popup's raw pivot at the chosen point, which is off by however " +
             "far the prefab's window sits from its root.")]
    public bool keepPopupsOnScreen = true;

    [Tooltip("Tag of the container spawned popups go inside. A scene object with this tag wins over " +
             "the parent field below. Leave empty to skip the lookup.")]
    [FormerlySerializedAs("alertParentTag")]
    public string popupParentTag = "AlertParent";

    [Tooltip("Fallback container, used only when nothing carries the tag above. Auto-resolved to the " +
             "canvas root if left empty.")]
    [FormerlySerializedAs("alertParent")]
    public RectTransform popupParent;

    [Tooltip("Keep this manager alive across scene loads. Off by default: the manager is bound to a " +
             "canvas, and a canvas does not survive a scene change.")]
    [SerializeField] protected bool dontDestroyOnLoad = false;

    // ================================================================ Runtime state

    private readonly List<TPopup> _spawnedPopups = new List<TPopup>();

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private Camera _uiCamera;
    private bool _canvasResolved;

    private Coroutine _spawnRoutine;
    private bool _spawning;

    /// <summary>Popups created since the last <see cref="ResetBudget"/>. This is the popup budget.</summary>
    private int _totalPopupsSpawned;

    /// <summary>Unscaled time the last spawn sound was allowed through.</summary>
    private float _lastSpawnSoundTime = -999f;

    /// <summary>Stops the inner burst loop from ever becoming infinite on a zero interval.</summary>
    protected const float AbsoluteMinGap = 0.0005f;

    /// <summary>
    /// Unity's "is this alive" check for a generic popup. Going through <see cref="Component"/> matters:
    /// a plain `popup != null` on a type parameter uses reference comparison and would call a destroyed
    /// object alive.
    /// </summary>
    protected static bool Alive(TPopup popup)
    {
        Component component = popup;
        return component;
    }

    // ================================================================ Public read-only access

    /// <summary>The canvas popups are spawned on. Resolved lazily on the first spawn.</summary>
    public Canvas PopupCanvas { get { ResolveCanvas(); return _canvas; } }

    /// <summary>Rect of the root canvas — what screen points are converted against.</summary>
    public RectTransform CanvasRect { get { ResolveCanvas(); return _canvasRect; } }

    /// <summary>Camera the canvas renders through, or null in Screen Space - Overlay.</summary>
    public Camera UiCamera { get { ResolveCanvas(); return _uiCamera; } }

    /// <summary>The container every spawned popup goes inside. Falls back to the canvas root.</summary>
    public RectTransform SpawnParent
    {
        get
        {
            ResolveCanvas();
            return popupParent ? popupParent : _canvasRect;
        }
    }

    /// <summary>Popups currently alive (destroyed ones are dropped on read).</summary>
    public IReadOnlyList<TPopup> SpawnedPopups
    {
        get
        {
            _spawnedPopups.RemoveAll(p => !Alive(p));
            return _spawnedPopups;
        }
    }

    /// <summary>True while the continuous loop is paying out popups.</summary>
    public bool IsSpawning => _spawning;

    /// <summary>Popups created since the last <see cref="ResetBudget"/>.</summary>
    public int TotalPopupsSpawned => _totalPopupsSpawned;

    /// <summary>
    /// The gap the loop actually uses right now: <see cref="spawnRate"/>, or — with
    /// <see cref="dependsOnDifficulty"/> on and a <see cref="DifficultyManager"/> present — a value
    /// interpolated between <see cref="spawnRateAtMinDifficulty"/> and
    /// <see cref="spawnRateAtMaxDifficulty"/> by the current difficulty. Never below
    /// <see cref="minSpawnRate"/>.
    /// </summary>
    public float CurrentSpawnRate
    {
        get
        {
            float rate = spawnRate;

            if (dependsOnDifficulty)
            {
                if (DifficultyManager.Instance)
                {
                    rate = DifficultyManager.Instance.Scale(spawnRateAtMinDifficulty, spawnRateAtMaxDifficulty);
                }
                else
                {
                    // No manager in the scene yet — the min-difficulty end is the safe assumption
                    // (slowest), rather than silently falling back to an unrelated fixed rate.
                    rate = spawnRateAtMinDifficulty;
                }
            }

            return Mathf.Max(rate, Mathf.Max(minSpawnRate, AbsoluteMinGap));
        }
    }

    // ================================================================ Setup

    protected virtual void Awake()
    {
        RegisterInstance();
    }

    protected virtual void Start()
    {
        if (continuousSpawning) StartSpawning();
    }

    protected virtual void OnDestroy()
    {
        _spawning = false;
        UnregisterInstance();
    }

    /// <summary>Subclasses hook their static singleton up here (h.CreateStaticInstance).</summary>
    protected abstract void RegisterInstance();

    /// <summary>Subclasses clear their static singleton here.</summary>
    protected abstract void UnregisterInstance();

    // ================================================================ Spawning

    /// <summary>
    /// Spawns one popup.
    /// </summary>
    /// <param name="screenPosition">
    /// Where to put it, in screen pixels. Null = a random point on screen, kept
    /// <see cref="screenMargin"/> pixels away from the edges.
    /// </param>
    /// <param name="sfx">Spawn sound override. Null = a random clip from <see cref="spawnSfx"/>.</param>
    /// <param name="configure">
    /// Per-spawn setup, run after <see cref="ConfigurePopup"/> and before the popup is placed — i.e.
    /// still before its own Start(), so it opens already carrying its content.
    /// </param>
    /// <returns>The spawned popup, or null when nothing was spawned (no prefabs, or budget spent).</returns>
    public virtual TPopup SpawnPopup(Vector2? screenPosition = null, AudioClip sfx = null,
        Action<TPopup> configure = null)
    {
        if (BudgetSpent())
        {
            h.Out($"{GetType().Name}: popup budget ({maxTotalPopups}) reached, no more popups.");
            return null;
        }

        TPopup prefab = ChoosePrefab();
        if (!Alive(prefab))
        {
            h.Out($"{GetType().Name}: no popup prefabs assigned, nothing to spawn.");
            return null;
        }

        ResolveCanvas();

        _totalPopupsSpawned++;

        TPopup popup = Instantiate(prefab, SpawnParent);

        // Everything below runs before the popup's Start(), so it opens already fully set up.
        ConfigurePopup(popup, prefab);
        configure?.Invoke(popup);
        ApplySpawnSfx(popup, sfx);

        Place(popup.transform as RectTransform, screenPosition);

        _spawnedPopups.Add(popup);
        TrimOldestPopups();

        OnPopupSpawned(popup);

        return popup;
    }

    /// <summary>Which prefab a spawn uses. Random by default; override for weighted/sequenced picks.</summary>
    protected virtual TPopup ChoosePrefab()
    {
        if (pfbs == null || pfbs.Count == 0) return null;
        return h.RandChoice(pfbs);
    }

    /// <summary>
    /// Fills the fresh instance in — texts, colours, callbacks. Runs before the popup's own Start().
    /// Does nothing in the base class.
    /// </summary>
    protected virtual void ConfigurePopup(TPopup popup, TPopup prefab) { }

    /// <summary>Called once the popup is placed and registered. Does nothing in the base class.</summary>
    protected virtual void OnPopupSpawned(TPopup popup) { }

    /// <summary>
    /// Plays the spawn sound through <see cref="SFXManager"/>, subject to the sound throttle. Override
    /// when the popup plays its own sound and the manager only has to hand it the clips (see
    /// <see cref="WinXpAlertManager"/>).
    /// </summary>
    protected virtual void ApplySpawnSfx(TPopup popup, AudioClip sfx)
    {
        if (!MayPlaySpawnSound()) return;

        AudioClip clip = sfx ? sfx : (spawnSfx != null && spawnSfx.Count > 0 ? h.RandChoice(spawnSfx) : null);
        if (!clip) return;

        if (!SFXManager.Instance)
        {
            h.Out($"{GetType().Name}: no SFXManager instance, skipping spawn sound.");
            return;
        }

        SFXManager.Instance.PlayClip(clip, is3D: false, playIndependently: true);
    }

    /// <summary>
    /// True when enough time has passed since the last spawn sound. At high spawn rates most popups
    /// come up muted, which is what keeps a storm from turning into undifferentiated noise.
    /// </summary>
    protected bool MayPlaySpawnSound()
    {
        if (minSpawnSoundGap <= 0f) return true;
        if (Time.unscaledTime - _lastSpawnSoundTime < minSpawnSoundGap) return false;

        _lastSpawnSoundTime = Time.unscaledTime;
        return true;
    }

    /// <summary>
    /// Puts a freshly spawned popup on screen.
    ///
    /// <para><b>The popup is placed by what it DRAWS, not by its pivot.</b> A popup prefab's root rect
    /// and the window the player actually sees are routinely not the same thing — the Win XP alerts, for
    /// one, are a 100x100 root with a 371x151 window hanging ~135px to the right of it. Placing the raw
    /// pivot at a point inside the margin therefore says nothing about where the window ends up: near an
    /// edge, most of it is off screen, which reads as "the popup never spawned". So the visible bounds
    /// are measured (<see cref="MeasureCanvasBounds"/>, every child included) and it is the BOUNDS that
    /// are kept <see cref="screenMargin"/> away from the edges.</para>
    ///
    /// <para>The random case picks a point for the bounds directly rather than clamping a point picked
    /// for the pivot: clamping would pile every overhanging popup onto the same line along the edge.</para>
    /// </summary>
    /// <param name="rt">The popup's root rect. Its children must already be in place — this runs after
    /// ConfigurePopup for exactly that reason.</param>
    /// <param name="screenPosition">Where the caller wants it, in screen pixels. Null = a random spot.</param>
    protected virtual void Place(RectTransform rt, Vector2? screenPosition)
    {
        if (!rt || !_canvasRect) return;

        // Opted out, or nothing to measure against: the old pivot-at-the-point behaviour.
        if (!keepPopupsOnScreen)
        {
            Vector2 rawPoint = screenPosition ?? RandomScreenPoint();

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(_canvasRect, rawPoint, _uiCamera,
                                                                       out Vector3 rawWorld))
                rt.position = rawWorld;
            return;
        }

        // Everything below is in the canvas's own local units (the reference resolution), NOT screen
        // pixels, so it is unaffected by the CanvasScaler's current scale factor.
        Bounds bounds = MeasureCanvasBounds(rt);
        Vector3 pivotLocal = _canvasRect.InverseTransformPoint(rt.position);

        // How far the drawn window sits from the transform that is actually being moved. This is the
        // whole point of the exercise.
        Vector2 pivotToBounds = (Vector2)bounds.center - (Vector2)pivotLocal;

        Rect allowed = AllowedBoundsCentreArea(bounds.size);

        Vector2 boundsCentre;
        if (screenPosition.HasValue)
        {
            // An explicit point still means "put the popup here" — it is only pulled back in when the
            // window would hang off an edge.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPosition.Value,
                                                                        _uiCamera, out Vector2 wanted))
                return;

            boundsCentre = new Vector2(
                Mathf.Clamp(wanted.x + pivotToBounds.x, allowed.xMin, allowed.xMax),
                Mathf.Clamp(wanted.y + pivotToBounds.y, allowed.yMin, allowed.yMax));
        }
        else
        {
            boundsCentre = new Vector2(
                UnityEngine.Random.Range(allowed.xMin, allowed.xMax),
                UnityEngine.Random.Range(allowed.yMin, allowed.yMax));
        }

        Vector2 targetPivot = boundsCentre - pivotToBounds;
        rt.position = _canvasRect.TransformPoint(new Vector3(targetPivot.x, targetPivot.y, pivotLocal.z));
    }

    /// <summary>
    /// The popup's real extent in canvas-local units — every child rect it draws, not just its root.
    /// This is what makes the margin mean something for a prefab whose window is offset from, or larger
    /// than, its root rect.
    /// </summary>
    protected Bounds MeasureCanvasBounds(RectTransform rt)
    {
        // Children parented this frame have their rects from the prefab already, so this is valid
        // immediately after Instantiate — no layout rebuild has to have run.
        return RectTransformUtility.CalculateRelativeRectTransformBounds(_canvasRect, rt);
    }

    /// <summary>
    /// Where the CENTRE of a popup of <paramref name="size"/> may sit, in canvas-local units, for the
    /// whole of it to stay <see cref="screenMargin"/> away from every edge. A popup too big to fit on an
    /// axis is centred on that axis instead of being pushed to an edge.
    /// </summary>
    protected Rect AllowedBoundsCentreArea(Vector3 size)
    {
        Rect canvas = _canvasRect.rect;
        float margin = LocalScreenMargin(canvas);

        return MakeMinMaxRect(
            AllowedRange(canvas.xMin + margin, canvas.xMax - margin, size.x),
            AllowedRange(canvas.yMin + margin, canvas.yMax - margin, size.y));
    }

    /// <summary><see cref="screenMargin"/> converted from screen pixels to canvas-local units, and
    /// capped so a margin bigger than the screen can never leave no room at all.</summary>
    private float LocalScreenMargin(Rect canvas)
    {
        float scale = _canvas ? Mathf.Abs(_canvas.scaleFactor) : 1f;
        if (scale < 0.0001f) scale = 1f;

        float margin = screenMargin / scale;
        return Mathf.Min(margin, Mathf.Min(canvas.width, canvas.height) * 0.4f);
    }

    /// <summary>The [min..max] a centre may take on one axis; a collapsed range at the midpoint when the
    /// popup is wider than the space.</summary>
    private static Vector2 AllowedRange(float min, float max, float size)
    {
        float half = Mathf.Abs(size) * 0.5f;
        float lo = min + half;
        float hi = max - half;

        if (lo > hi)
        {
            float mid = (min + max) * 0.5f;
            return new Vector2(mid, mid);
        }

        return new Vector2(lo, hi);
    }

    private static Rect MakeMinMaxRect(Vector2 xRange, Vector2 yRange)
    {
        return Rect.MinMaxRect(xRange.x, yRange.x, xRange.y, yRange.y);
    }

    /// <summary>
    /// A random point on screen, never closer to an edge than <see cref="screenMargin"/>.
    ///
    /// <para>This is about the POINT, not about a popup: it knows nothing of how big the thing put there
    /// will be, so a popup placed at one of these can still hang off an edge. <see cref="Place"/> no
    /// longer goes through it for that reason — it is kept for callers that want a plausible screen
    /// position for something else (see <c>DesktopPetyaAppearance</c>).</para>
    /// </summary>
    public Vector2 RandomScreenPoint()
    {
        float margin = Mathf.Min(screenMargin, Mathf.Min(Screen.width, Screen.height) * 0.4f);
        return new Vector2(
            UnityEngine.Random.Range(margin, Screen.width - margin),
            UnityEngine.Random.Range(margin, Screen.height - margin));
    }

    // ================================================================ Continuous loop

    /// <summary>Starts the continuous spawn loop, making sure only ever one is running.</summary>
    public void StartSpawning()
    {
        StopSpawning();

        // Budget already spent — don't even start a loop that would immediately die.
        if (BudgetSpent()) return;

        ResolveCanvas();

        // Coroutines can't run on a disabled object, and a manager that sits in the scene switched off
        // until it is needed is a perfectly reasonable setup.
        if (!gameObject.activeInHierarchy) gameObject.SetActive(true);

        continuousSpawning = true;
        _spawning = true;
        _spawnRoutine = StartCoroutine(SpawnLoop());
    }

    /// <summary>Starts the loop at a given rate. Shorthand for SetSpawnRate + StartSpawning.</summary>
    public void StartSpawning(float rate)
    {
        SetSpawnRate(rate);
        StartSpawning();
    }

    public void StopSpawning()
    {
        continuousSpawning = false;
        _spawning = false;

        if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
        _spawnRoutine = null;
    }

    /// <summary>
    /// Sets the gap between popups, never letting it drop below <see cref="minSpawnRate"/>. Has no
    /// effect while <see cref="dependsOnDifficulty"/> is on — the difficulty owns the rate then.
    /// </summary>
    public void SetSpawnRate(float seconds)
    {
        spawnRate = Mathf.Max(seconds, Mathf.Max(minSpawnRate, AbsoluteMinGap));

        if (dependsOnDifficulty)
        {
            h.Out($"{GetType().Name}: SetSpawnRate ignored — the rate is driven by DifficultyManager " +
                  "(dependsOnDifficulty is on).");
        }
    }

    /// <summary>
    /// Moves the spawn gap from <paramref name="startRate"/> towards <paramref name="endRate"/> along
    /// <paramref name="curve"/>. The running loop picks the new value up on its next wait, so speeding
    /// up never retroactively shortens a gap that is already in progress.
    /// </summary>
    /// <param name="t">0 = the start rate, 1 = the end rate.</param>
    public void RampSpawnRate(float startRate, float endRate, float t, AnimationCurve curve = null)
    {
        float eased = curve != null ? curve.Evaluate(Mathf.Clamp01(t)) : Mathf.Clamp01(t);
        SetSpawnRate(Mathf.Lerp(startRate, endRate, eased));
    }

    /// <summary>The next gap to wait out: the current rate, times a random factor from the distribution.</summary>
    protected virtual float NextGap()
    {
        float min = Mathf.Min(minDistributionSpawnRate, maxDistributionSpawnRate);
        float max = Mathf.Max(minDistributionSpawnRate, maxDistributionSpawnRate);

        return Mathf.Max(CurrentSpawnRate * UnityEngine.Random.Range(min, max), AbsoluteMinGap);
    }

    protected bool BudgetSpent()
    {
        return maxTotalPopups > 0 && _totalPopupsSpawned >= maxTotalPopups;
    }

    /// <summary>Lets a new run start from a full popup budget.</summary>
    public void ResetBudget()
    {
        _totalPopupsSpawned = 0;
    }

    /// <summary>
    /// Accumulates unscaled time and pays out one popup per elapsed gap. Because it works off an
    /// accumulator rather than a wait-per-popup, a gap shorter than a frame is not silently rounded up
    /// to one-per-frame: the frame simply spawns several, up to <see cref="maxPopupsPerFrame"/>.
    /// That per-frame ceiling (plus <see cref="maxTotalPopups"/>) is what keeps an extreme rate from
    /// stalling the main thread.
    /// </summary>
    private IEnumerator SpawnLoop()
    {
        float accumulator = 0f;
        float gap = NextGap();

        while (_spawning)
        {
            yield return null;
            if (!_spawning) yield break;

            accumulator += Time.unscaledDeltaTime;

            int spawnedThisFrame = 0;
            int frameLimit = Mathf.Max(maxPopupsPerFrame, 1);

            while (accumulator >= gap && spawnedThisFrame < frameLimit)
            {
                accumulator -= gap;

                // Budget exhausted -> this loop is done until something resets it.
                if (BudgetSpent())
                {
                    h.Out($"{GetType().Name}: popup budget ({maxTotalPopups}) reached, spawn loop stopped.");
                    _spawning = false;
                    continuousSpawning = false;
                    _spawnRoutine = null;
                    yield break;
                }

                SpawnPopup();

                spawnedThisFrame++;

                // Re-drawn every time, so a difficulty change lands on the very next gap.
                gap = NextGap();
            }

            // If the requested rate is higher than the frame ceiling allows, throw the backlog away
            // instead of letting it pile up and dump a burst the moment the rate eases off.
            if (accumulator > gap) accumulator = gap;
        }
    }

    // ================================================================ Housekeeping

    /// <summary>Removes the oldest popups so the screen never drowns in windows.</summary>
    protected virtual void TrimOldestPopups()
    {
        // Drop the entries the player already closed by hand.
        _spawnedPopups.RemoveAll(p => !p);

        if (maxSimultaneousPopups <= 0) return;

        while (_spawnedPopups.Count > maxSimultaneousPopups)
        {
            TPopup oldest = _spawnedPopups[0];
            _spawnedPopups.RemoveAt(0);
            if (!Alive(oldest)) continue;

            // Closing normally plays a sound; at storm rates that is one noise per spawn, so overflow
            // is normally removed silently instead.
            if (silentTrim) Destroy(oldest.gameObject);
            else ClosePopup(oldest);
        }
    }

    /// <summary>
    /// Closes one popup "properly" (animation, sound). Plain destruction by default — override when the
    /// popup type has a Close() of its own.
    /// </summary>
    protected virtual void ClosePopup(TPopup popup)
    {
        if (Alive(popup)) Destroy(popup.gameObject);
    }

    /// <summary>
    /// Stops the loop and tears every popup down.
    /// </summary>
    /// <param name="includeStrays">
    /// Also destroy popups this manager never spawned (hand-placed windows, leftovers from an earlier
    /// run). Costs a scene-wide component sweep.
    /// </param>
    public virtual void ClearPopups(bool includeStrays = false)
    {
        StopSpawning();

        foreach (TPopup popup in _spawnedPopups)
        {
            if (Alive(popup)) Destroy(popup.gameObject);
        }
        _spawnedPopups.Clear();

        if (!includeStrays) return;

        TPopup[] strays = FindObjectsByType<TPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (TPopup popup in strays)
        {
            if (Alive(popup)) Destroy(popup.gameObject);
        }
    }

    // ================================================================ Canvas

    /// <summary>
    /// Finds the container and the canvas popups are spawned on. Runs once; call
    /// <see cref="InvalidateCanvas"/> after a scene change or a canvas swap to force a new lookup.
    /// </summary>
    public void ResolveCanvas()
    {
        if (_canvasResolved && _canvasRect) return;

        // A scene object tagged popupParentTag wins over whatever is wired into the field.
        RectTransform tagged = FindTaggedRectTransform(popupParentTag);
        if (tagged)
        {
            popupParent = tagged;
        }
        else if (!string.IsNullOrEmpty(popupParentTag) && !popupParent)
        {
            h.Out($"{GetType().Name}: nothing tagged '{popupParentTag}' in the scene — " +
                  "spawning under the canvas root instead.");
        }

        _canvas = popupParent
            ? popupParent.GetComponentInParent<Canvas>()
            : GetComponentInParent<Canvas>();

        if (!_canvas) _canvas = FindFirstObjectByType<Canvas>();

        if (_canvas)
        {
            _canvas = _canvas.rootCanvas ? _canvas.rootCanvas : _canvas;
            _canvasRect = _canvas.transform as RectTransform;
            _uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        }
        else
        {
            h.Out($"{GetType().Name}: no Canvas found — popups can't be placed.");
        }

        if (!popupParent) popupParent = _canvasRect;

        _canvasResolved = true;
    }

    /// <summary>Forces the next spawn to look the canvas up again.</summary>
    public void InvalidateCanvas()
    {
        _canvasResolved = false;
        _canvas = null;
        _canvasRect = null;
        _uiCamera = null;
    }

    /// <summary>
    /// First UI object carrying <paramref name="tag"/>. Inactive objects are included, since the
    /// container may well start disabled.
    /// </summary>
    private RectTransform FindTaggedRectTransform(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;

        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Transform t in all)
        {
            if (!t || !t.gameObject.scene.IsValid()) continue;
            if (!t.CompareTag(tag)) continue;

            RectTransform rt = t as RectTransform;
            if (rt) return rt;

            h.Out($"{GetType().Name}: the object tagged '{tag}' is not a UI RectTransform, skipping it.");
        }

        return null;
    }
}
