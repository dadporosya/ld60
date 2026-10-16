using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Random = UnityEngine.Random;

/// <summary>
/// A Talkable whose on-screen body is a single rigged armature driven by AnimationClips, instead
/// of a set of swappable models (see TalkableWithModels).
///
/// Every dialogue node may pick one clip from <see cref="animations"/>; it starts playing when the
/// node starts. A looping node clip keeps looping until the next node starts. A non-looping one
/// plays once and then hands over to a random clip from <see cref="defaultIdle"/>, which loops.
/// A node that picks no clip leaves the armature idling.
/// </summary>
public class TalkableWithArmature : Talkable
{
    [Header("Armature Talkable")]
    [Tooltip("Animator on the armature the clips play on. Found in children when left empty.")]
    [SerializeField] private Animator animator;

    [Tooltip("Idle clips. After a non-looping node clip finishes, one of these is picked at " +
             "random and looped until the next node starts.")]
    public List<AnimationClip> defaultIdle = new List<AnimationClip>();

    [Tooltip("Clips dialogue nodes can choose from (ArmatureNodeSettings.animationId indexes this list).")]
    public List<AnimationClip> animations = new List<AnimationClip>();

    public List<ArmatureNodeSettings> nodesAdditionalSettings = new List<ArmatureNodeSettings>();

    [Tooltip("Blend time between clips, in seconds. 0 = hard cut.")]
    [SerializeField, Min(0f)] private float crossFadeDuration = 0.2f;

    [Tooltip("Start looping a random idle as soon as the object is enabled, so the armature is " +
             "never left in its bind pose while waiting for a dialogue.")]
    [SerializeField] private bool playIdleOnEnable = true;

    [Tooltip("Recompute the skinned meshes' bounds from the animated bones every frame. Needed " +
             "when a clip carries the body far from its bind pose (e.g. falling in from the sky): " +
             "the imported bounds would sit off screen, the renderer would be culled, and the " +
             "character would stay invisible until something else (the Scene view) looked at it.")]
    [SerializeField] private bool updateBoundsWhenOffscreen = true;

    [Header("SFX")]
    [SerializeField] private AudioClip beforeDialogueStartSFX;
    [SerializeField] private AudioClip onNodeStartTalkingSFX;
    [SerializeField] private AudioClip onDialogueEndSFX;

    [Header("Dialogue Animations")]
    [SerializeField] private AnimationBase beforeDialogueStartAnimation;
    [SerializeField] private AnimationBase onDialogueStartAnimation;
    [SerializeField] private AnimationBase onDialogueEndAnimation;
    [SerializeField] private AnimationBase onNodeStartAnimation;

    [Header("Dialogue Armature Clips")]
    [Tooltip("Played once on the armature before the dialogue window opens, alongside " +
             "beforeDialogueStartAnimation - the window waits for both. Idle loops afterwards.")]
    [SerializeField] private AnimationClip beforeDialogueStartAnimationClip;
    [Tooltip("Played once on the armature when the dialogue ends, alongside onDialogueEndAnimation. " +
             "Holds its last frame until the object is reset.")]
    [SerializeField] private AnimationClip onDialogueEndAnimationClip;

    private ArmatureClipPlayer player;

    /// <summary>Created on first use: an object that starts inactive can be driven before it awakes.</summary>
    private ArmatureClipPlayer Player
    {
        get
        {
            if (player == null)
            {
                if (!animator) animator = GetComponentInChildren<Animator>(true);
                player = new ArmatureClipPlayer(animator, $"{name} (TalkableWithArmature)", PickIdleClip);
            }
            return player;
        }
    }

    // Guards the end-cutscene coroutine against being started twice for the same object
    // (e.g. a mid-dialogue disable followed immediately by the dialogue closing).
    private bool endCutscenePlaying;

    // Handle to the running end-cutscene coroutine, so a re-enable on the very next node can
    // cancel a still-playing end cutscene before it deactivates the object.
    private Coroutine endCutsceneCo;

    /// <summary>
    /// True while the object is leaving the screen: its end animation is still playing, so the
    /// GameObject is active but the object is on its way out and will deactivate itself.
    /// </summary>
    public bool IsPlayingEndCutscene => endCutscenePlaying;

    // Captured on Awake so the object can be returned to its initial state when a dialogue ends.
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Vector3 initialLocalScale;

    // False until Awake has run. An object that starts (or is left) inactive never awakes, and
    // restoring an uncaptured "initial" transform would snap it to local zero.
    private bool initialTransformCaptured;

    public override void Awake()
    {
        base.Awake();

        // Remember the starting transform so dialogues can be reset to it later.
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        initialLocalScale = transform.localScale;
        initialTransformCaptured = true;

        if (!Player.HasAnimator) h.Out($"TalkableWithArmature: no Animator found on {name}.");

        if (updateBoundsWhenOffscreen)
            foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.updateWhenOffscreen = true;
    }

    private void OnEnable()
    {
        if (playIdleOnEnable && !Player.HasClip) PlayIdle();
    }

    private void Update()
    {
        player?.Tick(Time.deltaTime);
    }

    private void OnDestroy()
    {
        player?.Stop();
    }

    /// <summary>
    /// Before the dialogue window opens: play the intro animation and intro clip to completion,
    /// then fire the base "before dialogue" events. Talkable.TalkRoutine yields on this, so the
    /// window only opens once it finishes.
    /// </summary>
    protected override IEnumerator BeforeDialogueStartRoutine()
    {
        PlaySFX(beforeDialogueStartSFX);

        yield return PlayIntro();

        base.BeforeDialogueStart();
    }

    /// <summary>
    /// When the dialogue actually starts: play the on-start animation, then fire the
    /// base "on dialogue start" events.
    /// </summary>
    public override void OnDialogueStart()
    {
        StartCoroutine(OnDialogueStartRoutine());
    }

    private IEnumerator OnDialogueStartRoutine()
    {
        if (onDialogueStartAnimation) yield return onDialogueStartAnimation.Play();

        base.OnDialogueStart();
    }

    /// <summary>
    /// When the dialogue ends: play the end animation and end clip first and fire the base
    /// "on dialogue end" events, then return the object to its initial transform and idle.
    /// </summary>
    public override void OnDialogueEnd()
    {
        StartCoroutine(OnDialogueEndRoutine(false));
    }

    /// <summary>
    /// Container-driven dialogue end: plays the end animation and clip and fires the end events,
    /// then restores the transform. When deactivateAfter is true the object is also deactivated
    /// once the end animation has finished.
    /// </summary>
    public void PlayOnDialogueEnd(bool deactivateAfter)
    {
        if (!isActiveAndEnabled) return;
        if (endCutscenePlaying) return;
        endCutsceneCo = StartCoroutine(OnDialogueEndRoutine(deactivateAfter));
    }

    private IEnumerator OnDialogueEndRoutine(bool deactivateAfter = false)
    {
        if (endCutscenePlaying) yield break;
        endCutscenePlaying = true;

        PlaySFX(onDialogueEndSFX);

        yield return PlayWithClip(onDialogueEndAnimation, onDialogueEndAnimationClip,
            ArmatureClipPlayer.AfterClip.Hold);

        base.OnDialogueEnd();

        ResetTransform();

        // Clear the guard/handle before any deactivation - deactivating the GameObject stops
        // this coroutine, so anything after SetActive(false) would never run.
        endCutscenePlaying = false;
        endCutsceneCo = null;

        if (deactivateAfter)
        {
            Player.Stop();
            gameObject.SetActive(false);
        }
        else
        {
            // Still on screen - don't leave it frozen on the end clip's last frame.
            PlayIdle();
        }
    }

    // ---------------------------------------------------------------------
    // SFX
    // ---------------------------------------------------------------------

    /// <summary>
    /// Plays a dialogue SFX through the SFXManager. No-op when the clip is unset or the
    /// manager doesn't exist yet (e.g. a dialogue triggered before managers finished Awake).
    /// </summary>
    private static void PlaySFX(AudioClip clip, Vector2? randomPitchRange = null)
    {
        if (!clip || SFXManager.Instance == null) return;
        SFXManager.Instance.PlayClip(clip, randomPitchRange: randomPitchRange);
    }

    /// <summary>Per-node talking SFX, with a slight pitch variation so repeats don't sound identical.</summary>
    private void PlayNodeTalkingSFX()
    {
        PlaySFX(onNodeStartTalkingSFX, new Vector2(0.99f, 1.01f));
    }

    // ---------------------------------------------------------------------
    // Armature clips
    // ---------------------------------------------------------------------

    /// <summary>
    /// Intro: the before-dialogue animation and clip together, idling afterwards. Without an
    /// intro clip the armature just makes sure it is idling.
    /// </summary>
    private IEnumerator PlayIntro()
    {
        if (!beforeDialogueStartAnimationClip) EnsureIdle();

        // Hard cut into the intro clip: no crossfade from whatever pose (usually the on-enable
        // idle) the armature was in.
        yield return PlayWithClip(beforeDialogueStartAnimation, beforeDialogueStartAnimationClip,
            ArmatureClipPlayer.AfterClip.Idle, fade: 0f);
    }

    /// <summary>
    /// Plays <paramref name="animation"/> and <paramref name="clip"/> at the same time and
    /// finishes once both have. The animation is run nested (not as its own coroutine) so
    /// cancelling the caller cancels it too.
    /// </summary>
    private IEnumerator PlayWithClip(AnimationBase animation, AnimationClip clip, ArmatureClipPlayer.AfterClip after,
        float? fade = null)
    {
        float clipEnd = Time.time;
        if (clip && Player.Play(clip, after, fade ?? crossFadeDuration, idleFade: crossFadeDuration))
            clipEnd += clip.length;

        if (animation) yield return animation.Play();

        while (Time.time < clipEnd) yield return null;
    }

    /// <summary>
    /// Starts a node's clip: looped until the next node when <paramref name="loop"/> is set,
    /// otherwise once and then a random idle. An unset or out-of-range id keeps the armature idling.
    /// </summary>
    private void PlayNodeAnimation(int animationId, bool loop)
    {
        AnimationClip clip = animationId >= 0 && animationId < animations.Count ? animations[animationId] : null;
        if (!clip)
        {
            EnsureIdle();
            return;
        }

        Player.Play(clip,
            loop ? ArmatureClipPlayer.AfterClip.Loop : ArmatureClipPlayer.AfterClip.Idle,
            crossFadeDuration);
    }

    /// <summary>Loops a random idle, unless the armature is already idling or on its way there.</summary>
    private void EnsureIdle()
    {
        if (Player.IsIdling) return;
        if (Player.HasClip && Player.CurrentMode == ArmatureClipPlayer.AfterClip.Idle) return;
        PlayIdle();
    }

    /// <summary>Crossfades into a random clip from defaultIdle and loops it.</summary>
    private void PlayIdle()
    {
        AnimationClip idle = PickIdleClip();
        if (idle) Player.Play(idle, ArmatureClipPlayer.AfterClip.Loop, crossFadeDuration, isIdle: true);
    }

    /// <summary>A random non-null clip from defaultIdle, or null when there is none.</summary>
    private AnimationClip PickIdleClip()
    {
        int count = 0;
        for (int i = 0; i < defaultIdle.Count; i++)
            if (defaultIdle[i]) count++;
        if (count == 0) return null;

        int pick = Random.Range(0, count);
        for (int i = 0; i < defaultIdle.Count; i++)
        {
            if (!defaultIdle[i]) continue;
            if (pick-- == 0) return defaultIdle[i];
        }
        return null;
    }

    /// <summary>
    /// Restores the transform captured on Awake (position, rotation, scale).
    /// </summary>
    private void ResetTransform()
    {
        // Nothing captured yet (the object never awoke) - its transform is still the authored
        // one, so leave it alone instead of snapping it to zero.
        if (!initialTransformCaptured) return;

        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;
        transform.localScale = initialLocalScale;

        // We just snapped the transform back to its resting pose. Let the dialogue
        // animations re-sync their internal offset bookkeeping to it, otherwise an
        // animation with returnToTheInittialState = false keeps stale state and won't
        // replay on the next dialogue.
        if (beforeDialogueStartAnimation) beforeDialogueStartAnimation.SyncToRestingState();
        if (onDialogueStartAnimation) onDialogueStartAnimation.SyncToRestingState();
        if (onDialogueEndAnimation) onDialogueEndAnimation.SyncToRestingState();
    }

    public override void OnNodeStart(DialogueNode node = null)
    {
        ArmatureNodeSettings settings = null;
        if (node != null
            && node.nodeId >= 0
            && node.nodeId < nodesAdditionalSettings.Count)
            settings = nodesAdditionalSettings[node.nodeId];

        PlayNodeAnimation(settings != null ? settings.animationId : -1, settings != null && settings.loop);

        // Silent node: the clip still plays, but no talking SFX and no per-node animation.
        if (settings != null && !settings.talk) return;

        PlayNodeTalkingSFX();

        if (onNodeStartAnimation) onNodeStartAnimation.PlayInstantly();
    }

    // No OnNodeEnd override: a node's clip keeps playing (or has already handed over to idle)
    // until the next node starts.

    // ---------------------------------------------------------------------
    // Container-driven control (used by DialogueContainerWithArmature /
    // TalkableWithArmatureController when this object is one of several objects
    // steered by a single multi-object dialogue). These mirror the per-node logic
    // in OnNodeStart but take their values from an external source instead of
    // this object's own nodesAdditionalSettings.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Applies one dialogue node's settings to this object: toggles the whole object on/off,
    /// and when enabled starts the node's clip (see <see cref="PlayNodeAnimation"/>).
    ///
    /// When <paramref name="talk"/> is true the object also "speaks" the node - it plays the
    /// talking SFX and its onNodeStartAnimation.
    /// </summary>
    public void PlayDialogueNode(bool enableObject, int animationId, bool loop, bool talk = true)
    {
        SetupForNode(enableObject);
        if (!enableObject) return;

        PlayNodeAnimation(animationId, loop);

        if (!talk) return;

        PlayNodeTalkingSFX();

        if (onNodeStartAnimation) onNodeStartAnimation.PlayInstantly();
    }

    /// <summary>
    /// Enable-during-dialogue transition: enables the object and plays its appearing cutscene -
    /// the same intro + on-start animations, clip and events a first-node object gets before the
    /// window opens. Used when a node enables an object that was disabled on the previous node.
    ///
    /// The node itself is NOT started here: the container yields on this routine and only then
    /// runs PlayDialogueNode, so the object has finished appearing before it starts its node clip.
    /// </summary>
    public IEnumerator EnterDuringDialogueRoutine()
    {
        PlaySFX(beforeDialogueStartSFX);

        SetupForNode(true);

        yield return PlayIntro();
        BeforeDialogueStart();

        if (onDialogueStartAnimation) yield return onDialogueStartAnimation.Play();
        base.OnDialogueStart();
    }

    /// <summary>
    /// Disable-during-dialogue transition: plays the object's end cutscene (end animation, end clip,
    /// end events), then restores its transform and deactivates it - the same lifecycle a
    /// last-node object gets when the window closes. The end cutscene runs alongside the node start.
    /// </summary>
    public void DisableDuringDialogue()
    {
        // onDialogueEndSFX is played by OnDialogueEndRoutine, which PlayOnDialogueEnd starts.
        PlayOnDialogueEnd(true);
    }

    /// <summary>
    /// Enables/disables the object without starting any node clip. Used to stage an object
    /// before its intro animation.
    /// </summary>
    public void SetupForNode(bool enableObject)
    {
        if (!enableObject)
        {
            // Not used by this node: go all the way off (armature stopped, transform restored,
            // GameObject deactivated) so the object can't come back mid-pose from the last dialogue.
            ForceDeactivate();
            return;
        }

        gameObject.SetActive(true);

        // A freshly enabled object is not mid-end-cutscene. Cancel any still-running end
        // cutscene (would otherwise deactivate the object we just enabled) and clear the guard.
        if (endCutsceneCo != null) { StopCoroutine(endCutsceneCo); endCutsceneCo = null; }
        endCutscenePlaying = false;
    }

    /// <summary>
    /// Puts the character on screen NOW, idling, without playing any dialogue animation and
    /// without starting the dialogue. A cutscene that wants him present for a beat before he
    /// speaks calls this after enabling him.
    /// </summary>
    public void ShowIdle()
    {
        SetupForNode(true);
        EnsureIdle();
    }

    /// <summary>
    /// Container-driven "before dialogue start": plays this object's intro animation and clip
    /// and fires its beforeDialogueStart events. The container must have already enabled the
    /// object (SetupForNode) so the animation has a target.
    /// </summary>
    public IEnumerator PlayBeforeDialogueStart()
    {
        PlaySFX(beforeDialogueStartSFX);

        yield return PlayIntro();
        BeforeDialogueStart();
    }

    /// <summary>
    /// Returns the object to its resting state when a multi-object dialogue ends:
    /// stops the armature, restores the captured transform, and deactivates the object.
    /// </summary>
    public void ResetAfterDialogue()
    {
        ForceDeactivate();
    }

    /// <summary>
    /// Unconditionally takes the object off screen: cancels any in-flight end cutscene, stops the
    /// armature clips, restores the captured transform and sets the whole GameObject inactive.
    ///
    /// Cancelling the end cutscene here matters: deactivating a GameObject kills its coroutines,
    /// so an end cutscene interrupted from outside would otherwise leave endCutscenePlaying stuck
    /// true and make every later PlayOnDialogueEnd early-out.
    /// </summary>
    public void ForceDeactivate()
    {
        if (endCutsceneCo != null) { StopCoroutine(endCutsceneCo); endCutsceneCo = null; }
        endCutscenePlaying = false;

        Player.Stop();
        ResetTransform();

        gameObject.SetActive(false);
    }
}

[Serializable]
public class ArmatureNodeSettings
{
    [Tooltip("Clip played when the node starts (index into the talkable's animations list). " +
             "-1 = no clip, the armature keeps idling.")]
    public int animationId = -1;

    [Tooltip("Loop the clip until the next node starts. When off it plays once, then a random " +
             "defaultIdle clip loops.")]
    public bool loop = false;

    [Tooltip("Does this object talk on this node? When true it plays its talking SFX and " +
             "onNodeStartAnimation.")]
    public bool talk = true;
}

/// <summary>
/// Plays AnimationClips on an Animator through a small PlayableGraph (a two-input mixer, so
/// consecutive clips crossfade), with no AnimatorController states to author. While the graph
/// exists it overrides the Animator's own controller; <see cref="Stop"/> hands control back.
/// </summary>
public class ArmatureClipPlayer
{
    /// <summary>What happens when a clip reaches its end.</summary>
    public enum AfterClip
    {
        /// <summary>Wrap around and keep playing.</summary>
        Loop,
        /// <summary>Crossfade into an idle clip (from the idle provider) and loop it; hold when there is none.</summary>
        Idle,
        /// <summary>Freeze on the last frame.</summary>
        Hold,
    }

    private const int PREVIOUS_PORT = 0;
    private const int CURRENT_PORT = 1;

    private readonly Animator animator;
    private readonly string graphName;
    private readonly Func<AnimationClip> idleProvider;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable current;
    private AnimationClipPlayable previous;

    private float fadeDuration;
    private float idleFadeDuration;
    private float fadeElapsed;
    private bool fading;

    private AnimatorCullingMode originalCullingMode;
    private bool cullingModeOverridden;

    public ArmatureClipPlayer(Animator animator, string graphName, Func<AnimationClip> idleProvider)
    {
        this.animator = animator;
        this.graphName = graphName;
        this.idleProvider = idleProvider;
    }

    public bool HasAnimator => animator;

    /// <summary>The clip currently fading in / playing, or null when stopped.</summary>
    public AnimationClip CurrentClip { get; private set; }

    public AfterClip CurrentMode { get; private set; }

    /// <summary>True when the current clip was started as an idle.</summary>
    public bool IsIdling { get; private set; }

    public bool HasClip => CurrentClip && graph.IsValid() && current.IsValid();

    /// <summary>
    /// Starts <paramref name="clip"/> from its first frame, crossfading from whatever was playing
    /// over <paramref name="fade"/> seconds. Returns false when there is nothing to play it on.
    /// </summary>
    /// <param name="idleFade">Fade used when an <see cref="AfterClip.Idle"/> clip hands over to idle.
    /// Defaults to <paramref name="fade"/>.</param>
    public bool Play(AnimationClip clip, AfterClip after, float fade, bool isIdle = false, float? idleFade = null)
    {
        idleFadeDuration = idleFade ?? fade;

        if (!clip || !animator) return false;

        EnsureGraph();

        // A fade still in progress is cut short: its outgoing clip is dropped.
        DropPrevious();

        bool blend = fade > 0f && current.IsValid();
        if (current.IsValid())
        {
            graph.Disconnect(mixer, CURRENT_PORT);
            if (blend)
            {
                previous = current;
                graph.Connect(previous, 0, mixer, PREVIOUS_PORT);
                mixer.SetInputWeight(PREVIOUS_PORT, 1f);
            }
            else current.Destroy();
        }

        current = AnimationClipPlayable.Create(graph, clip);
        graph.Connect(current, 0, mixer, CURRENT_PORT);
        mixer.SetInputWeight(CURRENT_PORT, blend ? 0f : 1f);

        fading = blend;
        fadeElapsed = 0f;
        fadeDuration = fade;

        CurrentClip = clip;
        CurrentMode = after;
        IsIdling = isIdle;

        if (!graph.IsPlaying()) graph.Play();

        // Pose the armature now instead of at the Animator's next pass, so the clip's first frame
        // is what renders this frame (not the bind pose or the previous clip).
        graph.Evaluate(0f);
        return true;
    }

    /// <summary>
    /// Advances the crossfade and handles the current clip reaching its end. Call every frame from
    /// Update, i.e. before the Animator evaluates the frame's pose: the end is detected one frame
    /// ahead, so a one-shot never shows its first frame again (clips imported with Loop Time wrap).
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!HasClip) return;

        if (fading)
        {
            fadeElapsed += deltaTime;
            float w = Mathf.Clamp01(fadeElapsed / fadeDuration);
            mixer.SetInputWeight(CURRENT_PORT, w);
            mixer.SetInputWeight(PREVIOUS_PORT, 1f - w);
            if (w >= 1f) DropPrevious();
        }

        float length = CurrentClip.length;
        if (length <= 0f || current.GetPlayState() == PlayState.Paused) return;

        double time = current.GetTime();

        switch (CurrentMode)
        {
            case AfterClip.Loop:
                // Clips imported with Loop Time wrap by themselves; others clamp, so wrap them here.
                if (!CurrentClip.isLooping && time >= length) current.SetTime(time % length);
                break;

            case AfterClip.Idle:
                if (time + deltaTime < length) break;
                HoldCurrent(length);
                AnimationClip idle = idleProvider?.Invoke();
                if (idle) Play(idle, AfterClip.Loop, idleFadeDuration, isIdle: true);
                break;

            case AfterClip.Hold:
                if (time + deltaTime >= length) HoldCurrent(length);
                break;
        }
    }

    /// <summary>Destroys the graph, handing the Animator back to its own controller.</summary>
    public void Stop()
    {
        if (graph.IsValid()) graph.Destroy();
        if (animator && cullingModeOverridden)
        {
            animator.cullingMode = originalCullingMode;
            cullingModeOverridden = false;
        }

        current = default;
        previous = default;
        fading = false;
        CurrentClip = null;
        IsIdling = false;
    }

    private void EnsureGraph()
    {
        if (graph.IsValid()) return;

        // Imported models get an Animator set to Cull Update Transforms: bones are not written
        // while the renderers are invisible. A clip that starts the body off screen then never
        // moves it into view - a deadlock only broken when the Scene view happens to render it
        // (which is why selecting the object in the Editor "fixed" it). Always animate while a
        // clip is driving the armature; Stop hands the original mode back.
        if (!cullingModeOverridden)
        {
            originalCullingMode = animator.cullingMode;
            cullingModeOverridden = true;
        }
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        graph = PlayableGraph.Create(graphName);
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Armature", animator);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        output.SetSourcePlayable(mixer);
    }

    /// <summary>Freezes the current clip on its last frame.</summary>
    private void HoldCurrent(float length)
    {
        current.SetTime(length);
        current.Pause();
    }

    private void DropPrevious()
    {
        fading = false;
        if (!previous.IsValid()) return;

        graph.Disconnect(mixer, PREVIOUS_PORT);
        previous.Destroy();
        previous = default;
        mixer.SetInputWeight(PREVIOUS_PORT, 0f);
        mixer.SetInputWeight(CURRENT_PORT, 1f);
    }
}
