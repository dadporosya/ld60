using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Frame-swap animations for the hand's UI <see cref="Image"/>, played over its usual sprite animation
/// (e.g. a looping <see cref="UIImageSpriteChangeAnimation"/>):
/// <list type="bullet">
/// <item><b>Damage</b> — cycles <see cref="damageFrames"/> from the moment the player is hit until the
/// <see cref="GameManager"/>'s damage shake (hit shake + tremor) is over. Interrupts any spasm.</item>
/// <item><b>Spasms</b> (idle) — every <see cref="spasmInterval"/> seconds one of <see cref="spasms"/>, picked
/// by weight, shows its frame for its duration, then the usual sprite again; with its twice chance it rests
/// for the same time and spasms once more.</item>
/// </list>
/// Spasms only come while nothing else plays, the game is running and not over; their timer counts down
/// only in that time, so they never run back to back with another animation.
/// While any animation plays, the image's own sprite animations are stopped (and restarted afterwards), and
/// the shown frame is also re-applied every LateUpdate so nothing else can overwrite it.
/// </summary>
public class HandAnimation : MonoBehaviour
{
    public enum State { None, Damage, Spasm }

    /// <summary>One kind of idle spasm.</summary>
    [System.Serializable]
    public class Spasm
    {
        [Tooltip("Only for telling spasms apart in the inspector.")]
        public string name;
        public Sprite frame;
        [Tooltip("Seconds the frame is shown; also the rest between two spasms in a row.")]
        [Min(0.01f)] public float duration = 0.2f;
        [Tooltip("Chance the spasm comes twice in a row.")]
        [Range(0f, 1f)] public float twiceChance = 0.25f;
        [Tooltip("How likely this spasm is picked compared to the others.")]
        [Min(0f)] public float weight = 1f;
    }

    [Header("Target")]
    [Tooltip("Empty: the Image on this object.")]
    [SerializeField] private Image image;
    [Tooltip("Sprite animations that normally drive the image (e.g. a looping UIImageSpriteChangeAnimation); " +
             "stopped while a hand animation plays and restarted after. Empty: every AnimationBase on the " +
             "image's object.")]
    [SerializeField] private List<AnimationBase> spriteAnimations = new List<AnimationBase>();
    [Tooltip("Empty: GameManager.Instance.")]
    [SerializeField] private GameManager game;

    [Header("On damage")]
    [Tooltip("Cycled while the player is hit and the damage shake lasts.")]
    [SerializeField] private List<Sprite> damageFrames = new List<Sprite>();
    [SerializeField, Min(0.01f)] private float damageFrameGap = 0.1f;
    [Tooltip("Shortest time the damage animation plays, e.g. when GameManager has nothing to shake. " +
             "Every frame is always shown at least once.")]
    [SerializeField, Min(0f)] private float damageMinDuration = 0.3f;

    [Header("Idle: spasms")]
    [SerializeField] private List<Spasm> spasms = new List<Spasm>();
    [Tooltip("Random seconds of idling (min, max) between spasms.")]
    [SerializeField] private Vector2 spasmInterval = new Vector2(3f, 8f);

    public State Current { get; private set; }

    private Coroutine _routine;
    /// <summary>The frame forced onto the image every LateUpdate while an animation plays.</summary>
    private Sprite _shownFrame;
    /// <summary>The image's sprite when the animation started; shown again when it ends.</summary>
    private Sprite _restSprite;
    private readonly List<AnimationBase> _stopped = new List<AnimationBase>();
    private float _spasmTimer;

    private void Awake()
    {
        if (!image) image = GetComponent<Image>();
        if (spriteAnimations.Count == 0 && image) image.GetComponents(spriteAnimations);
    }

    private void Start()
    {
        if (!game) game = GameManager.Instance;
        if (game) game.onDamaged.AddListener(HandleDamaged);
        else h.Out("HandAnimation: no GameManager, the damage animation won't play.");

        ResetSpasmTimer();
    }

    private void OnDestroy()
    {
        if (game) game.onDamaged.RemoveListener(HandleDamaged);
    }

    private void OnDisable()
    {
        // Put the image and its sprite animations back now; the coroutine won't get to it.
        if (_routine != null) StopCoroutine(_routine);
        if (Current != State.None) Finish();
    }

    private void Update()
    {
        if (Current != State.None || !CanIdle()) return;

        _spasmTimer -= Time.deltaTime;
        if (_spasmTimer > 0f) return;

        ResetSpasmTimer();
        Spasm spasm = PickSpasm();
        if (spasm != null) Play(State.Spasm, SpasmRoutine(spasm));
    }

    private void LateUpdate()
    {
        // Coroutines (the sprite animations' included) run before LateUpdate, so this frame always wins.
        if (Current != State.None && image && _shownFrame) image.sprite = _shownFrame;
    }

    private bool CanIdle()
    {
        if (game && game.IsOver) return false;
        return !GameFlowManager.Instance || GameFlowManager.Instance.IsGame();
    }

    private void ResetSpasmTimer() => _spasmTimer = Random.Range(spasmInterval.x, spasmInterval.y);

    /// <summary>A weighted-random spasm with a frame, or null if there's none.</summary>
    private Spasm PickSpasm()
    {
        float total = 0f;
        foreach (Spasm spasm in spasms)
            if (spasm != null && spasm.frame) total += spasm.weight;
        if (total <= 0f) return null;

        float roll = Random.value * total;
        Spasm last = null;
        foreach (Spasm spasm in spasms)
        {
            if (spasm == null || !spasm.frame || spasm.weight <= 0f) continue;
            last = spasm;
            roll -= spasm.weight;
            if (roll < 0f) return spasm;
        }
        return last; // float drift at the very top of the roll
    }

    // ---------------------------------------------------------------- animations

    private void HandleDamaged(float damage)
    {
        // A hit during the damage animation just keeps it going: it lasts while the (restarted) shake does.
        if (Current == State.Damage || damageFrames.Count == 0 || !isActiveAndEnabled) return;
        Play(State.Damage, DamageRoutine());
    }

    private IEnumerator DamageRoutine()
    {
        float start = Time.time;
        int shown = 0;
        while (shown < damageFrames.Count || Time.time - start < damageMinDuration || (game && game.IsShaking))
        {
            Show(damageFrames[shown % damageFrames.Count]);
            shown++;
            yield return new WaitForSeconds(damageFrameGap);
        }
        Finish();
    }

    private IEnumerator SpasmRoutine(Spasm spasm)
    {
        Show(spasm.frame);
        yield return new WaitForSeconds(spasm.duration);

        if (Random.value < spasm.twiceChance)
        {
            Show(_restSprite);
            yield return new WaitForSeconds(spasm.duration);
            Show(spasm.frame);
            yield return new WaitForSeconds(spasm.duration);
        }
        Finish();
    }

    // ---------------------------------------------------------------- taking over the image

    /// <summary>Starts <paramref name="routine"/>, cutting off whatever hand animation is playing.</summary>
    private void Play(State state, IEnumerator routine)
    {
        if (!image) return;

        if (_routine != null) StopCoroutine(_routine);
        if (Current == State.None) TakeOver();

        Current = state;
        _routine = StartCoroutine(routine);
    }

    /// <summary>Remembers the current sprite and stops the sprite animations that would overwrite ours.</summary>
    private void TakeOver()
    {
        _restSprite = image.sprite;
        _stopped.Clear();
        foreach (AnimationBase anim in spriteAnimations)
        {
            // One that is between two loop passes reads as not playing and keeps running; LateUpdate still
            // overrides it, and it just carries on afterwards.
            if (!anim || !anim.IsPlaying) continue;
            anim.Stop();
            _stopped.Add(anim);
        }
    }

    private void Show(Sprite frame)
    {
        _shownFrame = frame ? frame : _restSprite;
        if (image) image.sprite = _shownFrame;
    }

    /// <summary>Gives the image back: the sprite from before, and its sprite animations restarted.</summary>
    private void Finish()
    {
        Current = State.None;
        _routine = null;
        _shownFrame = null;
        if (image) image.sprite = _restSprite;

        foreach (AnimationBase anim in _stopped)
            if (anim && anim.isActiveAndEnabled) anim.PlayInstantly();
        _stopped.Clear();
    }
}
