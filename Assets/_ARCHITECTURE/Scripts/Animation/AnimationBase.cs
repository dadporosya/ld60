using System.Collections;
using PrimeTween;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

public class AnimationBase : MonoBehaviour
{
    [SerializeField] private bool disable=false;
    public AnimationPreferences.Type type;
    public bool loop = false;
    public bool playOnStart = false;
    [Tooltip("Easing used when the animation returns to its initial state, both at its natural end and when stopped (see ReturnToInitialStateAnimated).")]
    public Ease returningEasing = Ease.Default;
    [Tooltip("Duration of the return-to-initial transition. 0 = snap instantly.")]
    public float returningTime = 0.3f;
    [Tooltip("If true, use the initialState value set in the inspector. If false, the current state is captured as the initial state on Awake.")]
    public bool customInitialState = false;
    [Tooltip("If true, the object eases/snaps back to its initial state when the animation ends or is stopped. If false, it stays at its final animated state.")]
    public bool returnToTheInittialState = true;

    // True once the initial state has been assigned — either from the inspector
    // (customInitialState) or captured once at runtime — so it is never overwritten again.
    [System.NonSerialized] public bool initialStateAssigned;

    // The single pass currently being animated. Started on THIS component so Stop() can always
    // kill it, and never awaited via `yield return <Coroutine>` (see Play) — awaiting a Coroutine
    // handle that someone else stops orphans the waiter forever, which is what used to leak
    // thousands of coroutines per minute here.
    private Coroutine animationCoroutine;
    private Coroutine returnCoroutine;

    // Bumped every time a new Play() starts or Stop() is called. A running Play() loop compares
    // its own token against this and exits cooperatively when it has been superseded, so no
    // coroutine is ever left waiting on something that was hard-stopped.
    [System.NonSerialized] private int runToken;
    [System.NonSerialized] private bool animationRunning;

    /// <summary>True while a Play() loop owns this animation.</summary>
    public bool IsPlaying => animationRunning;

    // Tracks whether Unity's Start() has already run for this component. OnEnable fires
    // before the first Start (and again on every re-enable), so we use this to only kick
    // off the playOnStart animation from OnEnable AFTER the initial Start, avoiding a
    // double-play on first activation while still restarting on subsequent re-enables.
    [System.NonSerialized] private bool hasStarted;
    [Tooltip("Invoked when the animation reaches its natural (non-looping) end.")]
    public UnityEvent onAnimationEnd = new UnityEvent();

    [SerializeField] private float animationSpeed=1f;
    
    public virtual void Awake() { }

    // Subclasses call this in Awake before capturing their initial state. It returns true
    // only the first time and only when the state was not assigned in the inspector, so an
    // already assigned/captured initial state is never overwritten.
    protected bool ShouldCaptureInitialState()
    {
        if (customInitialState || initialStateAssigned) return false;
        initialStateAssigned = true;
        return true;
    }

    public virtual void Start()
    {
        hasStarted = true;
        if (playOnStart) StartCoroutine(Play());
    }

    // Restart the playOnStart animation whenever the object is re-enabled. Disabling a
    // GameObject stops its running coroutines and Unity does NOT call Start() again on
    // re-enable, so looping idle animations would otherwise never resume. We skip the
    // first OnEnable (before Start) because Start() itself handles the initial play.
    public virtual void OnEnable()
    {
        if (hasStarted && playOnStart) StartCoroutine(Play());
    }

    // Unity kills coroutines started ON this component when it is disabled, but a Play() loop
    // that a controller nested inside ITS coroutine keeps running on the controller. Without
    // releasing it here, that loop would poll animationRunning forever on a component that will
    // never finish its pass. Bumping the token makes it exit on its next frame.
    public virtual void OnDisable()
    {
        runToken++;
        animationRunning = false;
        animationCoroutine = null;
        returnCoroutine = null;
    }

    public virtual void PlayInstantly()
    {
        StartCoroutine(Play());
    }
    /// <summary>
    /// Runs the animation, repeating it while <see cref="loop"/> is set. Looping is driven from
    /// HERE rather than by AnimationCoroutine restarting itself: the old code called
    /// StartCoroutine(Play()) from inside the running AnimationCoroutine, and the fresh Play()
    /// immediately did StopCoroutine(animationCoroutine) on the coroutine still on the stack.
    /// That killed the routine the previous Play() was awaiting, so that Play() never resumed and
    /// stayed registered with Unity forever — one leaked coroutine per loop iteration, per
    /// animation, per object.
    /// </summary>
    public virtual IEnumerator Play()
    {
        if (disable) yield break;

        if (returnCoroutine != null) { StopCoroutine(returnCoroutine); returnCoroutine = null; }
        if (animationCoroutine != null) { StopCoroutine(animationCoroutine); animationCoroutine = null; }

        int token = ++runToken;

        while (true)
        {
            int startFrame = Time.frameCount;

            animationRunning = true;
            animationCoroutine = StartCoroutine(RunOnce(token));

            // Poll a flag instead of `yield return animationCoroutine`. Stop() hard-stops
            // animationCoroutine so the animation halts on the exact frame it is told to; waiting
            // on the handle would leave this loop stranded, waiting on a corpse.
            while (animationRunning && token == runToken) yield return null;

            if (token != runToken) yield break;   // superseded by a newer Play(), or stopped
            if (!loop || disable) yield break;    // one-shot animation finished

            // A pass that consumed no frames (empty keyframe list, zero durations) would other-
            // wise spin this loop forever inside a single frame and hang the editor.
            if (Time.frameCount == startFrame) yield return null;
        }
    }

    // One pass of the animation. Kept separate so Play() holds a handle it can hard-stop.
    private IEnumerator RunOnce(int token)
    {
        yield return AnimationCoroutine();
        if (token == runToken) animationRunning = false;
    }

    public virtual void Stop()
    {
        runToken++;               // tells any running Play() loop to exit on its next frame
        animationRunning = false;

        if (animationCoroutine != null) { StopCoroutine(animationCoroutine); animationCoroutine = null; }
        if (returnCoroutine != null) { StopCoroutine(returnCoroutine); returnCoroutine = null; }

        // StartCoroutine throws on an inactive GameObject, and Stop() is legitimately called while
        // things are being torn down. Fall back to the instant reset in that case.
        if (!isActiveAndEnabled)
        {
            if (returnToTheInittialState) ReturnToInitialState();
            return;
        }

        returnCoroutine = StartCoroutine(StopRoutine());
    }

    // Smoothly returns to the initial state when the animation is stopped, using the
    // subclass's eased ReturnToInitialStateAnimated (which honours returningEasing).
    // The final ReturnToInitialState guarantees the exact state and also serves as the
    // instant fallback for animations that don't implement an animated return.
    private IEnumerator StopRoutine()
    {
        if (returnToTheInittialState)
        {
            yield return ReturnToInitialStateAnimated();
            ReturnToInitialState();
        }
        returnCoroutine = null;
    }

    public virtual void ReturnToInitialState() { }

    // Re-syncs any internal "current offset / pose" bookkeeping to the object's resting
    // transform WITHOUT moving it. Call this after externally snapping the transform back
    // to its initial state (e.g. TalkableWithModels.ResetTransform) so the next Play()
    // starts from the correct place. Without this, an animation with
    // returnToTheInittialState = false leaves stale offset state and its next play becomes
    // a no-op (from == target).
    public virtual void SyncToRestingState() { }

    // Animated counterpart of ReturnToInitialState, played when the animation reaches its
    // natural (non-looping) end and when it is stopped, so the object eases back to its
    // resting pose instead of snapping. Subclasses (Floating, Rotating) override this to
    // ease over their own default duration/gap using returningEasing. Base default is a
    // no-op; Stop() then falls back to an instant ReturnToInitialState for such animations.
    public virtual IEnumerator ReturnToInitialStateAnimated()
    {
        yield break;
    }

    // Evaluates returningEasing at t. Easing.Evaluate can't resolve Ease.Default (a placeholder
    // for "the configured default", not a real curve) or Ease.Custom, so map those: Default falls
    // back to a concrete eased curve (OutQuad) so the return still eases, and Custom stays linear.
    protected float EvaluateReturningEasing(float t)
    {
        Ease ease = returningEasing;
        if (ease == Ease.Custom) return t;
        if (ease == Ease.Default) ease = Ease.OutQuad;
        return Easing.Evaluate(t, ease);
    }

    // Subclasses call this at the end of their own AnimationCoroutine. It no longer restarts the
    // animation when looping — Play() owns the loop now (see the comment there). On a looping
    // animation this still deliberately skips the return-to-rest and onAnimationEnd, exactly as
    // before, so a loop runs straight into its next pass.
    public virtual IEnumerator AnimationCoroutine()
    {
        // it suppoused to be in the end of Animation
        if (disable) yield break;

        yield return null;
        if (!loop)
        {
            if (returnToTheInittialState) yield return ReturnToInitialStateAnimated();
            onAnimationEnd?.Invoke();
        }
        yield return null;
    }
}