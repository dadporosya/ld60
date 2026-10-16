using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Fires the two "win" confetti cannons (left / right) in a repeating pattern for a given duration,
/// playing a panned SFX for every shot.
///
/// The particle systems are expected to be NON-LOOPING one-shot bursts: the manager re-triggers them
/// with a gap in between (shot → gap → shot → gap → ...).
///
/// Usage: <c>WinConfettiManager.Instance.PlayLoop(5f);</c>
/// </summary>
public class WinConfettiManager : MonoBehaviour
{
    public static WinConfettiManager Instance; // Instance

    // ──────────────────────────────────────────────
    // Particles
    // ──────────────────────────────────────────────

    [Header("Particles")]
    [Tooltip("Cannon on the RIGHT side of the screen. Should be a non-looping (one-shot) system.")]
    public ParticleSystem rightParticle;

    [Tooltip("Cannon on the LEFT side of the screen. Should be a non-looping (one-shot) system.")]
    public ParticleSystem leftParticle;

    [Tooltip("If the system is still playing when the next shot comes, restart it so the burst re-emits. " +
             "Existing particles are kept alive.")]
    public bool restartParticleIfPlaying = true;

    // ──────────────────────────────────────────────
    // SFX
    // ──────────────────────────────────────────────

    [Header("SFX")]
    [Tooltip("Clip played on every shot. Leave empty for silent confetti.")]
    public AudioClip sfx;

    [Tooltip("Pan the SFX towards the ear matching the cannon that fired.")]
    public bool panSfx = true;

    [Tooltip("How hard the SFX is panned. 0 = center, 1 = fully in one ear.")]
    [Range(0f, 1f)] public float panningPercentage = 0.67f;

    [Tooltip("Volume of the SFX.")]
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Tooltip("Randomize the SFX pitch on every shot so repeats don't sound machine-gunned.")]
    public bool useRandomPitch = true;

    [Tooltip("Random pitch range (x = min, y = max). Only used when useRandomPitch is on.")]
    public Vector2 randomPitchRange = new Vector2(0.93f, 1.07f);

    // ──────────────────────────────────────────────
    // Pattern
    // ──────────────────────────────────────────────

    [Header("Pattern")]
    [Tooltip("Играть по очереди — alternate sides (right, left, right, left...). " +
             "If off, see 'Random Side When Not Alternating'.")]
    public bool alternate = true;

    [Tooltip("Only used when 'alternate' is off. On: pick a random side each shot. Off: fire BOTH cannons together.")]
    public bool randomSideWhenNotAlternating = false;

    [Tooltip("Which side goes first when alternating.")]
    public bool startWithRight = true;

    // ──────────────────────────────────────────────
    // Frequency
    // ──────────────────────────────────────────────

    [Header("Frequency")]
    [Tooltip("Seconds between shots.")]
    [Min(0.01f)] public float interval = 0.35f;

    [Tooltip("Randomize the gap between shots.")]
    public bool useRandomInterval = false;

    [Tooltip("Random gap range in seconds (x = min, y = max). Only used when useRandomInterval is on.")]
    public Vector2 randomIntervalRange = new Vector2(0.2f, 0.5f);

    [Tooltip("Delay before the very first shot.")]
    [Min(0f)] public float startDelay = 0f;

    [Tooltip("Fire immediately when the loop starts (after startDelay). If off, waits one gap first.")]
    public bool playImmediately = true;

    [Tooltip("Hard cap on the number of shots per loop. 0 = unlimited (duration decides).")]
    [Min(0)] public int maxShots = 0;

    [Tooltip("Default duration used by the parameterless PlayLoop().")]
    [Min(0f)] public float defaultDuration = 3f;

    [Tooltip("Make the parameterless PlayLoop() run forever, ignoring 'Default Duration'. " +
             "Stop it with StopLoop() (or let 'Max Shots' cap it).")]
    public bool infiniteByDefault = false;

    // ──────────────────────────────────────────────
    // Misc
    // ──────────────────────────────────────────────

    [Header("Misc")]
    [Tooltip("Use unscaled time, so the confetti keeps firing while the game is time-scaled / paused.")]
    public bool useUnscaledTime = true;

    [Tooltip("Stall the loop while GameFlowManager reports a paused state.")]
    public bool stallWhilePaused = false;

    [Tooltip("Fired when a PlayLoop finishes normally (not when stopped early).")]
    public UnityEvent onLoopFinished;

    /// <summary>True while a PlayLoop is running.</summary>
    public bool IsPlaying => loopRoutine != null;

    private Coroutine loopRoutine;
    private bool nextIsRight;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, false);
    }

    private void OnDisable()
    {
        StopLoop();
    }

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <summary>Pass this as the duration to <see cref="PlayLoop(float)"/> to loop forever.</summary>
    public const float Infinite = -1f;

    /// <summary>
    /// For <paramref name="duration"/> seconds, keeps firing the confetti cannons with a gap in between
    /// (shot → gap → shot → gap → ...), playing the panned SFX on every shot.
    /// Restarts the loop if one is already running.
    /// </summary>
    /// <param name="duration">
    /// How long to keep firing, in seconds. Pass <see cref="Infinite"/> (or any value &lt;= 0, or
    /// <see cref="float.PositiveInfinity"/>) to loop forever until <see cref="StopLoop"/> is called.
    /// </param>
    public Coroutine PlayLoop(float duration)
    {
        StopLoop();
        loopRoutine = StartCoroutine(PlayLoopCoroutine(duration));
        return loopRoutine;
    }

    /// <summary>
    /// Plays the loop for <see cref="defaultDuration"/> seconds —
    /// or forever, when <see cref="infiniteByDefault"/> is on.
    /// </summary>
    public Coroutine PlayLoop()
    {
        return PlayLoop(infiniteByDefault ? Infinite : defaultDuration);
    }

    /// <summary>
    /// Keeps firing forever, until <see cref="StopLoop"/> is called (or <see cref="maxShots"/> caps it).
    /// </summary>
    public Coroutine PlayLoopInfinite()
    {
        return PlayLoop(Infinite);
    }

    /// <summary>Stops a running loop. Already-spawned particles / sounds are left to finish naturally.</summary>
    /// <param name="invokeFinishedEvent">Also fire <see cref="onLoopFinished"/>, as if the loop had ended on its own.</param>
    public void StopLoop(bool invokeFinishedEvent = false)
    {
        if (loopRoutine == null) return;

        StopCoroutine(loopRoutine);
        loopRoutine = null;
        IsInfinite = false;

        if (invokeFinishedEvent) onLoopFinished?.Invoke();
    }

    /// <summary>True when the running loop has no end time and will keep firing until stopped.</summary>
    public bool IsInfinite { get; private set; }

    private static bool IsInfiniteDuration(float duration)
    {
        return duration <= 0f || float.IsInfinity(duration) || float.IsNaN(duration);
    }

    /// <summary>Fires a single shot following the current pattern settings.</summary>
    public void PlayOnce()
    {
        FireShot();
    }

    /// <summary>Fires one specific side (true = right cannon, false = left cannon).</summary>
    public void PlaySide(bool right)
    {
        PlayParticle(right ? rightParticle : leftParticle);
        PlaySfx(right ? panningPercentage : -panningPercentage);
    }

    /// <summary>Fires both cannons at once, with a single centered SFX.</summary>
    public void PlayBoth()
    {
        PlayParticle(rightParticle);
        PlayParticle(leftParticle);
        PlaySfx(0f);
    }

    // ──────────────────────────────────────────────
    // Loop
    // ──────────────────────────────────────────────

    private IEnumerator PlayLoopCoroutine(float duration)
    {
        nextIsRight = startWithRight;
        IsInfinite = IsInfiniteDuration(duration);

        float elapsed = 0f;
        int shots = 0;

        if (startDelay > 0f)
        {
            yield return Wait(startDelay);
            elapsed += startDelay;
        }

        if (!playImmediately)
        {
            float firstGap = NextGap();
            yield return Wait(firstGap);
            elapsed += firstGap;
        }

        while (IsInfinite || elapsed < duration)
        {
            // Long-running loops stall while the game is paused (see DirectorBase.TokenIncome).
            while (stallWhilePaused && GameFlowManager.Instance != null && GameFlowManager.Instance.IsPaused())
                yield return null;

            FireShot();
            shots++;

            if (maxShots > 0 && shots >= maxShots) break;

            float gap = NextGap();
            yield return Wait(gap);
            if (!IsInfinite) elapsed += gap; // no point accumulating (and overflowing) an endless timer
        }

        loopRoutine = null;
        IsInfinite = false;
        onLoopFinished?.Invoke();
    }

    private void FireShot()
    {
        if (alternate)
        {
            PlaySide(nextIsRight);
            nextIsRight = !nextIsRight;
            return;
        }

        if (randomSideWhenNotAlternating)
        {
            PlaySide(Random.value < 0.5f);
            return;
        }

        PlayBoth();
    }

    private float NextGap()
    {
        float gap = useRandomInterval
            ? Random.Range(randomIntervalRange.x, randomIntervalRange.y)
            : interval;

        return Mathf.Max(gap, 0.01f);
    }

    private object Wait(float seconds)
    {
        return useUnscaledTime
            ? (object)new WaitForSecondsRealtime(seconds)
            : new WaitForSeconds(seconds);
    }

    // ──────────────────────────────────────────────
    // Playback
    // ──────────────────────────────────────────────

    private void PlayParticle(ParticleSystem ps)
    {
        if (!ps) return;

        // A non-looping system that is still alive ignores Play(), so restart it to re-emit the burst.
        // StopEmitting keeps the particles already in the air.
        if (restartParticleIfPlaying && ps.isPlaying)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        ps.Play(true);
    }

    /// <param name="pan">-1 = fully left ear, 0 = center, 1 = fully right ear.</param>
    private void PlaySfx(float pan)
    {
        if (!sfx) return;

        if (SFXManager.Instance == null)
        {
            h.Out("WinConfettiManager: no SFXManager in scene, confetti SFX skipped.");
            return;
        }

        AudioSource source = SFXManager.Instance.PlayClipIndependently(
            sfx,
            volumeIn: sfxVolume,
            randomPitchRange: useRandomPitch ? randomPitchRange : (Vector2?)null);

        if (source && panSfx)
            source.panStereo = Mathf.Clamp(pan, -1f, 1f);
    }

    // ──────────────────────────────────────────────
    // Editor helpers
    // ──────────────────────────────────────────────

    [ContextMenu("Test / Play Loop (default duration)")]
    private void TestPlayLoop()
    {
        PlayLoop();
    }

    [ContextMenu("Test / Play Loop (infinite)")]
    private void TestPlayLoopInfinite()
    {
        PlayLoopInfinite();
    }

    [ContextMenu("Test / Stop Loop")]
    private void TestStopLoop()
    {
        StopLoop();
    }
}
