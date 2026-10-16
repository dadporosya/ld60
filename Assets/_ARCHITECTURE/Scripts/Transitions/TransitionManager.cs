using System;
using System.Collections;
using PrimeTween;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Flies the RTS camera from the city map into a level through a layer of clouds.
/// Access via <c>TransitionManager.Instance</c>; start one with <see cref="TransitionToLevel"/>.
///
/// <para><b>Fade in</b> — the camera dives at the target with accelerating easing while cloud
/// emission builds up and the clouds darken from <see cref="cloudStartColor"/> to
/// <see cref="cloudEndColor"/>. For the last <see cref="screenCoverDuration"/> seconds
/// <see cref="FadeImageController"/> covers the screen.</para>
///
/// <para><b>Swap</b> — behind the covered screen <see cref="LevelManager"/> spawns the level and the
/// camera is placed above it, backed off along its view by <see cref="descentDistance"/>.</para>
///
/// <para><b>Fade out</b> — the fade image clears onto thick dark clouds that thin out to nothing while
/// the camera eases down into its normal playing distance. Fade in reads as zooming into the city from
/// space; fade out as dropping out of the sky onto the level.</para>
///
/// <para><b>Flying back out</b> — <see cref="TransitionToLevelSelection"/> is the same three phases
/// in reverse: the camera rises off the level while the clouds build up (the fade out, backwards),
/// the level is unloaded and the chooser brought back behind the cover, and the camera is put right
/// on top of the tile it came from and pulled back out to exactly the rig position the map was left
/// at (the dive, backwards). The map's own camera bounds are saved on the way in and put back on
/// the way out.</para>
///
/// <para>Only the camera rig's POSITION is animated (never its rotation, never the camera's local pose),
/// so the camera's world offset from the rig and its forward are constant for the whole transition and
/// <see cref="RTSCameraController"/> picks up exactly where the transition left it.</para>
/// </summary>
public class TransitionManager : MonoBehaviour
{
    public static TransitionManager Instance;

    /// <summary>How the cloud particle system is built. Applied once, in <see cref="Awake"/>.</summary>
    [Serializable]
    public class CloudParticleSettings
    {
        [Tooltip("Build the particle system's MOTION from the settings below. Turn off to keep a hand-authored " +
                 "system exactly as it is — the transition then only drives its emission rate and colour. " +
                 "The look — material, texture, texture sheet, renderer settings, camera fading — is NEVER " +
                 "touched: author it on the particle system itself.")]
        public bool configureParticleSystem = true;

        [Tooltip("World = particles stay where they spawn, so the camera's own movement adds to the flight. " +
                 "Local = they follow the camera and only their own speed moves them.")]
        public ParticleSystemSimulationSpace simulationSpace = ParticleSystemSimulationSpace.World;

        [Header("Spawn volume (in front of the camera)")]
        [Tooltip("How far in front of the camera the spawn box is centred.")]
        public float spawnDistance = 40f;

        [Tooltip("Size of the spawn box: X/Y across the view, Z along it.")]
        public Vector3 spawnVolume = new Vector3(60f, 40f, 30f);

        [Header("Motion")]
        [Tooltip("Speed the clouds fly at the camera, in units per second.")]
        public float approachSpeed = 20f;

        [Tooltip("Random lifetime range, in seconds. Roughly Spawn Distance / Approach Speed lets a cloud " +
                 "reach the camera before it dies.")]
        public Vector2 lifetime = new Vector2(2f, 2.8f);

        [Tooltip("Max spin speed, degrees per second, random in both directions.")]
        public float rotationSpeed = 12f;

        [Header("Look")]
        [Tooltip("Random start size range, in world units.")]
        public Vector2 startSize = new Vector2(18f, 35f);

        [Tooltip("Size multiplier a cloud reaches at the end of its life (1 = no growth).")]
        public float sizeGrowth = 1.6f;

        [Tooltip("Max live particles. Keep it low: large overlapping transparent quads are expensive (overdraw).")]
        public int maxParticles = 200;

        [Header("Render on top")]
        [Tooltip("Draw the clouds over everything else — the whole point of the transition is that they " +
                 "hide the world, so no piece of scenery may poke through them. Turn off only if you want " +
                 "the clouds to sort against the scene normally.")]
        public bool renderOnTop = true;

        [Tooltip("Sorting order given to the particle renderer when Render On Top is on. High = last.")]
        public int sortingOrder = 32000;
    }

    [Header("References")]
    [Tooltip("The RTS camera. Disabled for the whole transition; its rig is what gets moved. " +
             "Empty = found in the scene.")]
    [SerializeField] private RTSCameraController rtsCamera;

    [Tooltip("Cloud particle system. Empty = one is created as a child of the camera.")]
    [SerializeField] private ParticleSystem clouds;

    [Header("Clouds")]
    [SerializeField] private CloudParticleSettings cloudSettings = new CloudParticleSettings();

    [Tooltip("Emission rate (particles/s) when the fade in starts.")]
    [SerializeField] private float minEmission = 0f;

    [Tooltip("Emission rate (particles/s) at full cover: the end of fade in, the start of fade out.")]
    [SerializeField] private float peakEmission = 60f;

    [Tooltip("Cloud colour when the fade in starts. Alpha is the clouds' opacity.")]
    [SerializeField] private Color cloudStartColor = Color.white;

    [Tooltip("Cloud colour at full cover, and for the whole fade out.")]
    [SerializeField] private Color cloudEndColor = Color.black;

    [Header("Fade in (into the city)")]
    [Tooltip("Length of the dive, in seconds.")]
    [SerializeField] private float fadeInDuration = 2.5f;

    [Tooltip("Camera easing. An In-ease accelerates into the target.")]
    [SerializeField] private Ease fadeInCameraEase = Ease.InCubic;

    [Tooltip("Easing of the cloud build-up (emission and colour).")]
    [SerializeField] private Ease fadeInCloudEase = Ease.InQuad;

    [Tooltip("How far from the target, along the view, the dive ends.")]
    [SerializeField] private float diveEndDistance = 4f;

    [Tooltip("The fade image covers the screen during the LAST this-many seconds of the dive.")]
    [SerializeField] private float screenCoverDuration = 0.6f;

    [SerializeField] private Ease screenCoverEase = Ease.InQuad;

    [Header("Covered screen")]
    [Tooltip("Extra time the screen stays covered after the level is spawned, so it never just flashes.")]
    [SerializeField] private float coveredHoldDuration = 0.3f;

    [Header("Fade out (onto the level)")]
    [Tooltip("Length of the descent, in seconds.")]
    [SerializeField] private float fadeOutDuration = 2f;

    [Tooltip("Camera easing. An Out-ease settles softly into place.")]
    [SerializeField] private Ease fadeOutCameraEase = Ease.OutCubic;

    [Tooltip("Easing of the clouds thinning out.")]
    [SerializeField] private Ease fadeOutCloudEase = Ease.OutQuad;

    [Tooltip("How far back along its view the camera starts above the level before easing down into " +
             "its playing distance — the size of the \"drop out of the sky\" move.")]
    [SerializeField] private float descentDistance = 15f;

    [Tooltip("Time for the fade image to clear at the start of the fade out.")]
    [SerializeField] private float screenRevealDuration = 0.5f;

    [SerializeField] private Ease screenRevealEase = Ease.OutQuad;

    [Tooltip("Playing distance to use when the camera is not looking down at the ground (it cannot be " +
             "measured then).")]
    [SerializeField] private float fallbackViewDistance = 30f;

    [Header("Return to the map (the flight out)")]
    [Tooltip("Length of the rise off the level, in seconds — the reversed descent.")]
    [SerializeField] private float returnRiseDuration = 2f;

    [Tooltip("Easing of the rise. An In-ease accelerates away from the level.")]
    [SerializeField] private Ease returnRiseEase = Ease.InCubic;

    [Tooltip("Length of the pull-back off the tile on the map, in seconds — the reversed dive.")]
    [SerializeField] private float returnPullBackDuration = 2.5f;

    [Tooltip("Easing of the pull-back. An Out-ease settles softly back into the map view.")]
    [SerializeField] private Ease returnPullBackEase = Ease.OutCubic;

    [Tooltip("Emission rate (particles/s) the pull-back on the map starts at, before thinning to " +
             "nothing. Deliberately lower than Peak Emission: the map is being revealed, not hidden, " +
             "so it comes out from behind a few dark wisps rather than a full sky.")]
    [SerializeField] private float returnPullBackEmission = 20f;

    [Header("Camera shake")]
    [Tooltip("Shake the camera for the whole transition. Needs an EZ CameraShaker on the camera.")]
    [SerializeField] private bool shakeCamera = true;

    [Tooltip("Shake magnitude at full cover (the end of the dive / the start of the descent). " +
             "The shake builds up to it over the fade in and fades back out over the fade out.")]
    [SerializeField] private float shakeMagnitude = 1.5f;

    [Tooltip("Shake roughness: low is a smooth rumble, high is jittery.")]
    [SerializeField] private float shakeRoughness = 6f;

    [Tooltip("Fraction of the peak magnitude the shake starts at, before it builds up.")]
    [Range(0f, 1f)]
    [SerializeField] private float shakeStartScale = 0.1f;

    [Tooltip("Time for the shake to die away at the very end of the descent.")]
    [SerializeField] private float shakeFadeOutDuration = 0.6f;

    [Header("Debug")]
    [Tooltip("Target used by the debug key and the \"Test Transition\" context menu.")]
    [SerializeField] private Transform debugTarget;

    [Tooltip("Press in Play mode to run a transition to Debug Target. None = off.")]
    [SerializeField] private Key debugKey = Key.None;

    [Tooltip("Press in Play mode to fly back out to the level chooser. None = off.")]
    [SerializeField] private Key debugReturnKey = Key.None;

    /// <summary>True from <see cref="TransitionToLevel"/> until control is back with the player.</summary>
    public bool IsTransitioning { get; private set; }

    private Sequence _sequence;
    private Tween _screenTween;
    private EZCameraShake.CameraShakeInstance _shake;
    private Material _onTopMaterial;

    // Where the map was left, saved on the way in so the flight out can put it back exactly: the
    // tile that was dived into, the rig position it was dived from, and the camera bounds the map
    // was clamped to before the level replaced them.
    private Transform _mapTarget;
    private Vector3 _mapRigPosition;
    private RTSCameraController.MapBoundsState _mapBounds;
    private bool _hasMapReturn;

    private static readonly int ZTestProperty = Shader.PropertyToID("_ZTest");

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }

    private void Start()
    {
        if (!rtsCamera) rtsCamera = FindFirstObjectByType<RTSCameraController>();
        SetupClouds();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (debugKey != Key.None && Keyboard.current[debugKey].wasPressedThisFrame)
            TransitionToLevel(debugTarget);

        if (debugReturnKey != Key.None && Keyboard.current[debugReturnKey].wasPressedThisFrame)
            TransitionToLevelSelection();
    }

    private void OnDisable()
    {
        // A transition cut off halfway must not leave the player without a camera.
        if (!IsTransitioning) return;

        StopAllCoroutines();
        _sequence.Stop();
        _screenTween.Stop();
        StopShake(0f);
        if (rtsCamera) rtsCamera.enabled = true;
        IsTransitioning = false;
    }

    [ContextMenu("Test Transition")]
    private void TestTransition() => TransitionToLevel(debugTarget);

    [ContextMenu("Test Return Transition")]
    private void TestReturnTransition() => TransitionToLevelSelection();

    /// <summary>
    /// Dives the camera into <paramref name="target"/> (a tile of the city map), spawns the level
    /// behind the cloud cover and drops the camera onto it. Ignored while a transition is running.
    /// </summary>
    public void TransitionToLevel(Transform target)
    {
        if (IsTransitioning)
        {
            h.Out("TransitionManager: a transition is already running.");
            return;
        }

        if (!target)
        {
            h.Out("TransitionManager: no target to fly into.");
            return;
        }

        if (!rtsCamera || !rtsCamera.CameraParent || !rtsCamera.MainCamera)
        {
            h.Out("TransitionManager: no RTSCameraController (with a rig and camera) to move.");
            return;
        }

        if (!LevelManager.Instance)
        {
            h.Out("TransitionManager: no LevelManager in the scene.");
            return;
        }

        StartCoroutine(TransitionRoutine(target));
    }

    private IEnumerator TransitionRoutine(Transform target)
    {
        IsTransitioning = true;
        rtsCamera.enabled = false;

        Transform rig = rtsCamera.CameraParent;
        Transform cam = rtsCamera.MainCamera;
        Vector3 forward = cam.forward;
        Vector3 cameraOffset = cam.position - rig.position;

        // Measured before the dive, so the level is framed at the same zoom the map was being played at.
        float playDistance = ViewDistance(cam.position, forward, target.position.y);

        // Everything the flight out needs to put the map back the way it was found.
        _mapTarget = target;
        _mapRigPosition = rig.position;
        _mapBounds = rtsCamera.GetMapBounds();
        _hasMapReturn = true;

        yield return FadeIn(rig, RigPositionLookingAt(target.position, diveEndDistance, forward, cameraOffset),
                            fadeInDuration, fadeInCameraEase, cloudStartColor, cloudEndColor);

        // ---- Screen is covered: swap the map for the level.
        clouds.Clear();

        Vector3 levelRigPosition = rig.position;
        if (LevelManager.Instance.SpawnLevel())
        {
            // One frame for the level's Awake/Start to run before anything reads it.
            yield return null;

            if (LevelManager.Instance.TryGetLevelBounds(out Bounds levelBounds))
                rtsCamera.SetMapBounds(levelBounds);

            levelRigPosition = RigPositionLookingAt(
                LevelManager.Instance.LevelFocusPoint, playDistance, forward, cameraOffset);
        }

        rig.position = levelRigPosition - forward * descentDistance;
        FillCloudsForDescent(peakEmission);

        if (coveredHoldDuration > 0f) yield return new WaitForSeconds(coveredHoldDuration);

        yield return FadeOut(rig, levelRigPosition, fadeOutDuration, fadeOutCameraEase,
                             cloudEndColor, cloudEndColor, peakEmission);

        rtsCamera.enabled = true;
        IsTransitioning = false;
    }

    /// <summary>
    /// Flies back out of the loaded level to the level chooser: the flight in, reversed. The level
    /// is unloaded by <see cref="LevelManager"/> behind the cloud cover, and the camera comes out of
    /// the clouds on the map exactly where it dived from.
    ///
    /// <para><paramref name="target"/> is the map tile to come out over; left empty it is the one
    /// this manager dived into, which is what you want in every normal case. With neither (the level
    /// was loaded without a transition) the swap still happens, the camera simply does not fly.</para>
    /// </summary>
    public void TransitionToLevelSelection(Transform target = null)
    {
        if (IsTransitioning)
        {
            h.Out("TransitionManager: a transition is already running.");
            return;
        }

        if (!rtsCamera || !rtsCamera.CameraParent || !rtsCamera.MainCamera)
        {
            h.Out("TransitionManager: no RTSCameraController (with a rig and camera) to move.");
            return;
        }

        if (!LevelManager.Instance)
        {
            h.Out("TransitionManager: no LevelManager in the scene.");
            return;
        }

        StartCoroutine(ReturnRoutine(target ? target : _mapTarget));
    }

    private IEnumerator ReturnRoutine(Transform target)
    {
        IsTransitioning = true;
        rtsCamera.enabled = false;

        Transform rig = rtsCamera.CameraParent;
        Transform cam = rtsCamera.MainCamera;
        Vector3 forward = cam.forward;
        Vector3 cameraOffset = cam.position - rig.position;

        // Rise off the level: the descent, backwards — straight back up the view the camera came
        // down, while the clouds build back up and the screen covers. Black from the first particle:
        // this is the descent's cloud layer being re-entered, not the dive's sky darkening.
        yield return FadeIn(rig, rig.position - forward * descentDistance, returnRiseDuration,
                            returnRiseEase, cloudEndColor, cloudEndColor);

        // ---- Screen is covered: swap the level back for the map.
        clouds.Clear();

        LevelManager.Instance.UnloadLevel();

        // One frame for the chooser to come back up before the camera is placed against it.
        yield return null;

        // The level's bounds go with the level; without this the camera would still be clamped to
        // a box around something that no longer exists.
        if (_hasMapReturn) rtsCamera.RestoreMapBounds(_mapBounds);

        Vector3 endPosition = _hasMapReturn ? _mapRigPosition : rig.position;

        if (target)
        {
            // Right on top of the tile, where the dive ended — the pull-back then runs that dive
            // backwards, out to the rig position the map was being played at.
            rig.position = RigPositionLookingAt(target.position, diveEndDistance, forward, cameraOffset);
        }
        else
        {
            h.Out("TransitionManager: no map tile to come out over, the chooser is restored without " +
                  "the flight out.");
            rig.position = endPosition;
        }

        FillCloudsForDescent(returnPullBackEmission);

        if (coveredHoldDuration > 0f) yield return new WaitForSeconds(coveredHoldDuration);

        // The dive, backwards: a thin veil of black clouds that thins out to nothing — no colour
        // ramp, they stay dark for as long as they are there — while the camera pulls back to the
        // exact rig position it dived from.
        yield return FadeOut(rig, endPosition, returnPullBackDuration, returnPullBackEase,
                             cloudEndColor, cloudEndColor, returnPullBackEmission);

        rtsCamera.enabled = true;
        IsTransitioning = false;
        _hasMapReturn = false;
        _mapTarget = null;
    }

    /// <summary>
    /// The covering half of a transition: the camera moves to <paramref name="endPosition"/> while
    /// the clouds build up, and the fade image takes the screen at the end of it.
    ///
    /// <para>The timing AND the colour ramp are the caller's, because the flight out runs this same
    /// phase reversed: the dive builds up from <see cref="cloudStartColor"/> to
    /// <see cref="cloudEndColor"/>, while the rise off a level starts in the dark clouds it is
    /// already under and stays there (<paramref name="fromColor"/> = <paramref name="toColor"/> =
    /// the end colour).</para>
    /// </summary>
    private IEnumerator FadeIn(Transform rig, Vector3 endPosition, float duration, Ease cameraEase,
                               Color fromColor, Color toColor)
    {
        duration = Mathf.Max(duration, 0.01f);

        SetCloudEmission(minEmission);
        SetCloudColor(fromColor);
        clouds.Play();
        StartShake();

        _sequence = Sequence.Create(Tween.Position(rig, endPosition, duration, cameraEase))
            .Group(Tween.Custom(0f, 1f, duration, t =>
            {
                SetCloudEmission(Mathf.LerpUnclamped(minEmission, peakEmission, t));
                SetCloudColor(Color.Lerp(fromColor, toColor, t));
                ScaleShake(Mathf.LerpUnclamped(shakeStartScale, 1f, t));
            }, fadeInCloudEase));

        float coverDuration = Mathf.Clamp(screenCoverDuration, 0f, duration);
        yield return new WaitForSeconds(duration - coverDuration);

        if (FadeImageController.Instance)
            _screenTween = FadeImageController.Instance.FadeIn(coverDuration, screenCoverEase);
        else
            h.Out("TransitionManager: no FadeImageController, the level swap will be visible.");

        yield return _sequence.ToYieldInstruction();
        yield return _screenTween.ToYieldInstruction();
    }

    /// <summary>
    /// The revealing half of a transition: the fade image clears and the camera eases to
    /// <paramref name="endPosition"/> while the clouds thin out to nothing.
    ///
    /// <para>As with <see cref="FadeIn"/>, the timing, the colour and how thick the clouds start are
    /// the caller's. Dropping onto a level comes out of a full sky at <see cref="peakEmission"/>;
    /// coming back up to the map starts at the much thinner <see cref="returnPullBackEmission"/>, so
    /// the map is revealed from behind a few wisps instead of from under cloud cover. Passing the
    /// same colour twice holds it — the ramp is only there for the dive, which darkens as it goes.</para>
    /// </summary>
    private IEnumerator FadeOut(Transform rig, Vector3 endPosition, float duration, Ease cameraEase,
                                Color fromColor, Color toColor, float fromEmission)
    {
        duration = Mathf.Max(duration, 0.01f);
        fromEmission = Mathf.Max(fromEmission, 0f);

        if (FadeImageController.Instance)
            _screenTween = FadeImageController.Instance.Fadeout(screenRevealDuration, screenRevealEase);

        SetCloudColor(fromColor);

        _sequence = Sequence.Create(Tween.Position(rig, endPosition, duration, cameraEase))
            .Group(Tween.Custom(fromEmission, 0f, duration, SetCloudEmission, fadeOutCloudEase))
            .Group(Tween.Custom(1f, shakeStartScale, duration, ScaleShake, fadeOutCloudEase));

        // Only tweened when there is actually a ramp: holding one colour must not keep writing the
        // particle system's start colour every frame for nothing.
        if (fromColor != toColor)
            _sequence.Group(Tween.Custom(0f, 1f, duration,
                                         t => SetCloudColor(Color.Lerp(fromColor, toColor, t)),
                                         fadeOutCloudEase));

        yield return _sequence.ToYieldInstruction();
        yield return _screenTween.ToYieldInstruction();

        StopShake(shakeFadeOutDuration);

        // Emission is already zero; the clouds still in view drift out and die on their own.
        clouds.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>
    /// Starts a reveal with clouds already in the air: runs the system forward by one cloud lifetime
    /// at <paramref name="rate"/>, around the camera's new position, so the fade image clears onto
    /// an existing sky instead of onto clouds that are only just being born. How thick that sky is
    /// is the caller's: the descent onto a level fills it at <see cref="peakEmission"/>, the
    /// pull-back onto the map at the far thinner <see cref="returnPullBackEmission"/>.
    /// </summary>
    private void FillCloudsForDescent(float rate)
    {
        SetCloudColor(cloudEndColor);
        SetCloudEmission(Mathf.Max(rate, 0f));
        clouds.Simulate(Mathf.Max(cloudSettings.lifetime.y, 0f), true, false, true);
        clouds.Play();
    }

    // ---------------------------------------------------------------- Camera shake

    /// <summary>
    /// Starts one sustained shake that lasts the whole transition; its magnitude is then scaled by
    /// <see cref="ScaleShake"/> so the rumble builds up with the clouds and dies away with them.
    /// </summary>
    private void StartShake()
    {
        if (!shakeCamera || shakeMagnitude <= 0f) return;

        if (!EZCameraShake.CameraShaker.Instance)
        {
            h.Out("TransitionManager: no CameraShaker in the scene, the transition will not shake.");
            return;
        }

        _shake = h.StartShake(shakeMagnitude, shakeRoughness, 0f);
        ScaleShake(shakeStartScale);
    }

    private void ScaleShake(float scale)
    {
        if (_shake != null) _shake.ScaleMagnitude = Mathf.Max(scale, 0f);
    }

    private void StopShake(float fadeOutDuration)
    {
        if (_shake == null) return;

        if (EZCameraShake.CameraShaker.Instance) h.EndShake(Mathf.Max(fadeOutDuration, 0f), _shake);
        _shake = null;
    }

    // ---------------------------------------------------------------- Camera framing

    /// <summary>
    /// Rig position that puts the camera <paramref name="distance"/> away from
    /// <paramref name="point"/> along its fixed <paramref name="forward"/>, looking straight at it.
    /// </summary>
    private static Vector3 RigPositionLookingAt(Vector3 point, float distance, Vector3 forward, Vector3 cameraOffset)
    {
        return point - forward * distance - cameraOffset;
    }

    /// <summary>Distance along the view from the camera to the ground plane at <paramref name="groundY"/>.</summary>
    private float ViewDistance(Vector3 cameraPosition, Vector3 forward, float groundY)
    {
        if (forward.y > -0.01f) return fallbackViewDistance;

        float distance = (groundY - cameraPosition.y) / forward.y;
        return distance > 0f ? distance : fallbackViewDistance;
    }

    // ---------------------------------------------------------------- Clouds

    private void SetCloudEmission(float rate)
    {
        ParticleSystem.EmissionModule emission = clouds.emission;
        emission.rateOverTime = Mathf.Max(rate, 0f);
    }

    // Start colour only reaches newly born clouds, so the darkening spreads through the cloud field
    // gradually instead of every cloud switching colour at once.
    private void SetCloudColor(Color color)
    {
        ParticleSystem.MainModule main = clouds.main;
        main.startColor = color;
    }

    private void SetupClouds()
    {
        if (!clouds)
        {
            Transform parent = rtsCamera && rtsCamera.MainCamera ? rtsCamera.MainCamera : transform;

            GameObject go = new GameObject("TransitionClouds");
            go.transform.SetParent(parent, false);
            clouds = go.AddComponent<ParticleSystem>();

            // A freshly added system is Play-On-Awake with default settings; the motion ones are replaced
            // below. Its LOOK is never touched, so an auto-created system renders with Unity's default
            // particle material — assign a real cloud particle system in the inspector.
            cloudSettings.configureParticleSystem = true;
            h.Out("TransitionManager: no cloud particle system assigned, a bare one was created. " +
                  "Assign an authored system (material, texture, renderer settings) instead.");
        }

        clouds.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Never let culling pause the clouds, whatever the system was authored with. Between
        // transitions this one is empty, and an empty WORLD-space system has no particle positions
        // for Unity to build its renderer bounds from - it is judged against a degenerate box that
        // does not follow the camera. Under the default Automatic culling mode a looping system that
        // lands off-screen is PAUSED, so it emits nothing, so it stays empty, so it stays off-screen:
        // panning or zooming the camera before a transition was enough to leave the clouds stuck
        // there and the whole flight played with no clouds at all.
        ParticleSystem.MainModule culling = clouds.main;
        culling.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        if (cloudSettings.configureParticleSystem) ConfigureClouds(clouds, cloudSettings);
        if (cloudSettings.renderOnTop) ApplyRenderOnTop(clouds, cloudSettings.sortingOrder);

        SetCloudEmission(0f);
    }

    /// <summary>
    /// Makes the clouds draw over the whole scene: they are put last in the transparent queue and their
    /// material is given an always-passing depth test, so scene geometry in front of a cloud cannot clip
    /// it. The material is instanced (never the authored asset) and destroyed with this manager.
    /// </summary>
    private void ApplyRenderOnTop(ParticleSystem ps, int sortingOrder)
    {
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (!renderer) return;

        renderer.sortingOrder = sortingOrder;

        Material source = renderer.sharedMaterial;
        if (!source)
        {
            h.Out("TransitionManager: the cloud particle system has no material, it cannot be drawn on top.");
            return;
        }

        _onTopMaterial = new Material(source) { name = source.name + " (On Top)" };
        _onTopMaterial.renderQueue = (int)RenderQueue.Overlay;
        if (_onTopMaterial.HasProperty(ZTestProperty))
            _onTopMaterial.SetInt(ZTestProperty, (int)CompareFunction.Always);
        renderer.material = _onTopMaterial;
    }

    private void OnDestroy()
    {
        if (_onTopMaterial) Destroy(_onTopMaterial);
    }

    private static void ConfigureClouds(ParticleSystem ps, CloudParticleSettings s)
    {
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = s.simulationSpace;
        main.startLifetime = new ParticleSystem.MinMaxCurve(s.lifetime.x, s.lifetime.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(s.startSize.x, s.startSize.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.maxParticles = Mathf.Max(s.maxParticles, 1);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        // A box volume straight ahead of the camera (the system sits on the camera, facing its view).
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = new Vector3(0f, 0f, s.spawnDistance);
        shape.rotation = Vector3.zero;
        shape.scale = s.spawnVolume;

        // Towards the camera, i.e. down the system's local -Z.
        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(-s.approachSpeed);

        float spin = s.rotationSpeed * Mathf.Deg2Rad;
        ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-spin, spin);

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, s.sizeGrowth));

        // Every cloud fades in after birth and out before death, so none pops in or out.
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f)
            });
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = fade;
    }
}
