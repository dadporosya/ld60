using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>How a node gets from the previous node's background to its own.</summary>
public enum VisualNovelTransition
{
    [Tooltip("Same location: the previous node's background stays exactly as it is.")]
    KeepPrevious,
    [Tooltip("Instant swap to the new background (and any color cover is dropped at once) - " +
             "the screamer cut.")]
    Cut,
    [Tooltip("The old background dissolves into the new one. From a covered screen, the cover " +
             "fades away over the new background instead.")]
    Crossfade,
    [Tooltip("Old background -> fade color -> new background. A screen that is already covered " +
             "skips straight to the fade-in, so this is also the 'fade in from black' of a first node.")]
    FadeThroughColor,
    [Tooltip("Fades to the fade color and STAYS covered (the background sprite is ignored). " +
             "Text on a black screen; the next node reveals a location with FadeThroughColor/Crossfade/Cut.")]
    FadeToColor,
    // Appended, not inserted: enums serialize as ints, so a value in the middle would shift every
    // later transition in existing dialogue assets.
    [Tooltip("Blurs the screen up to Blur Radius (BlurVolumeManager), swaps to the new background " +
             "under the blur, then unblurs to 0. From a covered screen the new background comes " +
             "in fully blurred while the cover fades away and the blur clears.")]
    FadeThroughBlur,
    [Tooltip("Crossfade and blur at the same time: while the old background dissolves into the new " +
             "one, the screen blurs up to Blur Radius (peaking halfway) and clears back to 0. From a " +
             "covered screen the new background comes in blurred while the cover fades away and the " +
             "blur clears.")]
    CrossfadeBlur,
}

/// <summary>What a node does to a looping audio layer (music, ambience).</summary>
public enum VisualNovelAudioChange
{
    [Tooltip("Leave whatever is playing alone.")]
    Keep,
    [Tooltip("Crossfade into this node's clip. Asking for the clip that is already playing never restarts it.")]
    Play,
    [Tooltip("Fade the layer out.")]
    Stop,
}

/// <summary>Which typing voice a node's text plays.</summary>
public enum VisualNovelVoice
{
    [Tooltip("The speaker's own voice: its Talkable's clips, then its DialogueSpeaker asset's, " +
             "then the VisualNovelTalkable's Default Voice Clips, then the SFXManager's default.")]
    Speaker,
    [Tooltip("This node's Voice Clips instead of the speaker's.")]
    Custom,
    [Tooltip("No typing sound on this node.")]
    Silent,
}

/// <summary>What the dialogue window does while a node's transition changes the picture.</summary>
public enum VisualNovelTextBox
{
    [Tooltip("Stays up, emptied, while the transition plays; the node starts talking Text Box " +
             "Delay seconds after it.")]
    Keep,
    [Tooltip("Stays up, emptied, while the transition plays.")]
    Clear,
    [Tooltip("Closes, the transition plays once it's closed, and it reopens when the transition ends.")]
    CloseAndReopen,
}

/// <summary>What happens to the music when a visual-novel dialogue ends.</summary>
public enum VisualNovelMusicOnEnd
{
    [Tooltip("Keep playing the dialogue's last track.")]
    Keep,
    [Tooltip("Fade the music out to silence.")]
    FadeOut,
    [Tooltip("Crossfade back into the BGMManager's default track.")]
    ReturnToDefault,
}

/// <summary>When a node's one-shot sound plays.</summary>
public enum VisualNovelSfxTiming
{
    [Tooltip("With the node's other cues: after the transition, or with its start (see Cues After Transition).")]
    NodeCues,
    [Tooltip("Transition Point of the way through the node's transition (0.5 = its middle), whatever " +
             "Cues After Transition says. Plays at once when the node has no transition.")]
    Transition,
}

[Serializable]
public class VisualNovelSfxCue
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Seconds after the moment Timing picks.")]
    [Min(0f)] public float delay = 0f;
    public VisualNovelSfxTiming timing = VisualNovelSfxTiming.NodeCues;
    [Tooltip("Timing = Transition only: how far through the node's transition the sound plays, as a " +
             "share of its length (Transition Duration, plus Hold On Color for the transitions that hold). " +
             "0 = its start, 0.5 = its middle, 1 = its end.")]
    [Range(0f, 1f)] public float transitionPoint = 0.5f;
}

[Serializable]
public class VisualNovelEffectCue
{
    [Tooltip("Effect asset to run (Create > Dialogues > Visual Novel > Effects).")]
    public VisualNovelEffect effect;
    [Tooltip("Seconds after the node's cues fire (see VisualNovelNodeSettings.cuesAfterTransition).")]
    [Min(0f)] public float delay = 0f;
    [Tooltip("Start effects only: the node's text does not start typing until this effect has " +
             "finished. Ignored for sustained effects, which never finish on their own.")]
    public bool waitForCompletion = false;
}

/// <summary>
/// Visual-novel settings for one dialogue node: its background and foreground images and how it
/// gets there, the music and ambience layers, one-shot SFX and effects at the node's start, and
/// effects that last for the whole node.
/// </summary>
[Serializable]
public class VisualNovelNodeSettings
{
    [Header("Picture")]
    [Tooltip("How the node gets from the previous node's picture (background AND foreground) to its own.")]
    public VisualNovelTransition transition = VisualNovelTransition.KeepPrevious;
    [Tooltip("The node's background. Unused by KeepPrevious / FadeToColor. Empty = keep the " +
             "background shown now (the previous location's, even across a transition or a fade to color).")]
    public Sprite background;
    [Tooltip("Material the background is drawn with (must be a UI-compatible shader). " +
             "Empty = the scene's material when Background is set, else keep the one the background is drawn with now.")]
    public Material backgroundMaterial;
    [Tooltip("Show no background image (whatever is behind the visual novel's canvas shows through). " +
             "Background is ignored; Background Material still applies when set.")]
    public bool clearBackground = false;
    [Tooltip("Rescales background image so it covers the whole screen on any aspect ratio / resolution.")]
    public bool stretchBackground = false;
    [Tooltip("Image drawn over the background, under the fade cover - a character, a window frame, " +
             "... Changes together with the background, through the same transition. Unused by " +
             "KeepPrevious / FadeToColor. Empty = keep the foreground shown now.")]
    public Sprite foreground;
    [Tooltip("Material the foreground is drawn with (must be a UI-compatible shader). " +
             "Empty = the scene's material when Foreground is set, else keep the one the foreground is drawn with now.")]
    public Material foregroundMaterial;
    [Tooltip("Show no foreground (a character leaves, ...). Foreground is ignored; Foreground " +
             "Material still applies when set.")]
    public bool clearForeground = false;
    [Tooltip("Rescales foreground image so it covers the whole screen on any aspect ratio / resolution.")]
    public bool stretchForeground = false;
    [Tooltip("What the dialogue window does when this node's transition changes the picture. " +
             "Nothing happens to it when the transition changes nothing (KeepPrevious, or a " +
             "Cut/Crossfade to the picture already on screen), nor on the dialogue's first node.")]
    public VisualNovelTextBox textBox = VisualNovelTextBox.CloseAndReopen;
    [Tooltip("Whole transition length in seconds. FadeThroughColor / FadeThroughBlur spend half " +
             "fading out and half fading in.")]
    [Min(0f)] public float transitionDuration = 1f;
    [Tooltip("Color used by FadeThroughColor / FadeToColor.")]
    public Color fadeColor = Color.black;
    [Tooltip("FadeThroughColor: seconds the screen stays fully covered before the new background fades in. " +
             "FadeThroughBlur: seconds at full blur, during which the old picture crossfades into the " +
             "new one (0 = instant swap at the peak). CrossfadeBlur: seconds the blur stays at its " +
             "peak, halfway through; the crossfade stretches to cover them.")]
    [Min(0f)] public float holdOnColor = 0f;
    [Tooltip("FadeThroughBlur / CrossfadeBlur only: BlurVol.m_Radius at the peak (0..1).")]
    [Range(0f, 1f)] public float blurRadius = 1f;
    [Tooltip("The node's text does not start typing until the transition has finished.")]
    public bool waitForTransition = true;
    [Tooltip("Seconds the dialogue window waits after the transition before it appears again - " +
             "when Text Box closed it for this node, or on the dialogue's first node when the " +
             "VisualNovelTalkable opens the window after the first transition. With Text Box = Keep " +
             "the window stays up and the node starts talking this many seconds after the transition " +
             "(counted from the node's start when nothing transitions).")]
    [Min(0f)] public float textBoxDelay = 0f;

    [Header("Music (BGMManager)")]
    public VisualNovelAudioChange music = VisualNovelAudioChange.Keep;
    [Tooltip("Track to play. Takes priority over Bgm Clip.")]
    public BGMObject bgm;
    [Tooltip("Plain clip to loop when no BGMObject is set.")]
    public AudioClip bgmClip;
    [Tooltip("Play only: volume of the track, on top of the BGMManager's own volume. Asking for the " +
             "track that is already playing eases it to this volume.")]
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Tooltip("Seconds the music playing before fades out (Play, Stop). Also the new track's " +
             "fade-in while Music Fade In is negative.")]
    [Min(0f)] public float musicFade = 1.5f;
    [Tooltip("Play only: seconds the new track takes to fade in, independent of the old one's fade-out. " +
             "0 = starts at full volume. Negative = same as Music Fade.")]
    public float musicFadeIn = -1f;

    [Header("Ambience (SFX texture under the music)")]
    public VisualNovelAudioChange ambience = VisualNovelAudioChange.Keep;
    [Tooltip("Looping texture, e.g. metro rumble, rain, crowd.")]
    public AudioClip ambienceClip;
    [Range(0f, 1f)] public float ambienceVolume = 1f;
    [Min(0f)] public float ambienceFade = 1.5f;

    [Header("Voice (typing sound)")]
    public VisualNovelVoice voice = VisualNovelVoice.Speaker;
    [Tooltip("Custom only: clips picked at random while the text types. Empty = the speaker's voice.")]
    public List<AudioClip> voiceClips = new List<AudioClip>();
    [Tooltip("Volume of the node's typing voice, multiplied with the DialogueManager's Voice Volume.")]
    [Range(0f, 1f)] public float voiceVolume = 1f;
    [Tooltip("Use Voice Pitch Range on this node instead of the DialogueManager's.")]
    public bool overrideVoicePitch = false;
    [Tooltip("Override Voice Pitch only: each voice clip plays at a random pitch in this range (1 = unchanged).")]
    public Vector2 voicePitchRange = new Vector2(0.98f, 1.02f);

    [Header("Start of node")]
    [Tooltip("Fire the SFX and start effects once the background transition has finished, and start " +
             "the sustained effects as soon as the new location is in place (e.g. the swap at the peak " +
             "of a blur or color). Off: fire them all together with the transition's start (music and " +
             "ambience always change at the transition's start). Blur effects never run during a " +
             "FadeThroughBlur or CrossfadeBlur transition.")]
    public bool cuesAfterTransition = true;
    [Tooltip("One-shot sounds played when the node starts.")]
    public List<VisualNovelSfxCue> sfx = new List<VisualNovelSfxCue>();
    [Tooltip("One-shot effects played when the node starts (shake, blur pulse, ...). Cut short and " +
             "released if the player moves on before they finish.")]
    public List<VisualNovelEffectCue> startEffects = new List<VisualNovelEffectCue>();

    [Header("During the node")]
    [Tooltip("Keep every sustained effect still running from earlier nodes going through this one, " +
             "on top of this node's own Sustained Effects. Off: effects this node doesn't list are released.")]
    public bool keepPreviousSustainedEffects = false;
    [Tooltip("Effects that last from the node's start until the next node starts (or the dialogue " +
             "ends). An effect also listed on the next node keeps running instead of restarting.")]
    public List<VisualNovelEffectCue> sustainedEffects = new List<VisualNovelEffectCue>();

    /// <summary>Seconds the transition takes from start to end: Transition Duration, plus Hold On Color for the ones that hold.</summary>
    public float TransitionLength
    {
        get
        {
            switch (transition)
            {
                case VisualNovelTransition.FadeThroughColor:
                case VisualNovelTransition.FadeThroughBlur:
                case VisualNovelTransition.CrossfadeBlur:
                    return transitionDuration + holdOnColor;
                case VisualNovelTransition.Crossfade:
                case VisualNovelTransition.FadeToColor:
                    return transitionDuration;
                default:
                    return 0f;
            }
        }
    }

    public VisualNovelLook BackgroundLook => new VisualNovelLook(background, backgroundMaterial, clearBackground, stretchBackground);

    public VisualNovelLook ForegroundLook => new VisualNovelLook(
        foreground,
        foregroundMaterial,
        clearForeground,
        stretchForeground || (foreground && foreground.name.IndexOf("blackscreen", StringComparison.OrdinalIgnoreCase) >= 0)
    );
}

/// <summary>
/// A DialogueContainer for visual-novel scenes. On top of the usual nodes (speaker + text) every
/// node carries a <see cref="VisualNovelNodeSettings"/> row - background, transition, music,
/// ambience, SFX and effects - kept parallel to the nodes list the same way
/// DialogueContainerWithArmature keeps its grid.
///
/// The asset only holds data. A <see cref="VisualNovelTalkable"/> in the scene owns the stage
/// (background images, ambience sources, blur volume, shake target) and binds itself to the
/// dialogue before it starts; the DialogueManager then drives the node lifecycle through
/// <see cref="IMultiObjectDialogue"/>, and each call is forwarded to the bound controller.
/// </summary>
[CreateAssetMenu(fileName = "VisualNovelDialogue", menuName = "Dialogues/VisualNovelDialogueContainer")]
public class VisualNovelDialogueContainer : DialogueContainer, IMultiObjectDialogue
{
    [Header("Visual novel")]
    [Tooltip("Per-node visual novel settings. Kept in sync with 'nodes' automatically: index i matches nodes[i].")]
    public List<VisualNovelNodeSettings> nodeSettings = new List<VisualNovelNodeSettings>();

    [Header("Volume")]
    [Tooltip("Volume of the music this dialogue's nodes play, multiplied with each node's Music Volume. " +
             "Music already playing before the dialogue is left alone until a node plays a track.")]
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Tooltip("Volume of the ambience this dialogue's nodes play, multiplied with each node's Ambience Volume.")]
    [Range(0f, 1f)] public float ambienceVolume = 1f;

    [Header("Stretching")]
    [FormerlySerializedAs("additionalScaling")]
    [FormerlySerializedAs("additionalStreching")]
    [Tooltip("Stretches background and foreground images so they fill the screen entirely on any aspect ratio or resolution, eliminating letterbox/pillarbox borders.")]
    public bool additionalStretching = false;
    public bool AdditionalStretching => additionalStretching;
    public bool AdditionalScaling => additionalStretching;

    [Header("Dialogue start")]
    [Tooltip("Cover the screen with Start Color before the first node, so a first node using " +
             "FadeThroughColor or Crossfade fades the scene in from it.")]
    public bool startCovered = true;
    public Color startColor = Color.black;

    [Header("Dialogue end")]
    [Tooltip("Fade the screen to End Color when the dialogue ends, before the stage is hidden.")]
    public bool fadeToColorOnEnd = true;
    public Color endColor = Color.black;
    [Min(0f)] public float endFadeDuration = 1f;
    public VisualNovelMusicOnEnd musicOnEnd = VisualNovelMusicOnEnd.FadeOut;
    [Tooltip("Seconds the music fades over when the dialogue ends. Negative = same as End Fade Duration.")]
    public float endMusicFade = 1.5f;
    public bool stopAmbienceOnEnd = true;
    [Min(0f)] public float endAmbienceFade = 1.5f;
    [Tooltip("Effects still running when the dialogue ends keep going through the end fade (Fade To " +
             "Color On End). Once it's over - or right away without one - on: every effect still " +
             "running (or held, like a zoom) eases back to rest. Off: they stop where they are, so the " +
             "next scene covers a zoomed, shaken, ... stage. The stage resets its zoom and offsets " +
             "when the next dialogue starts; a blur stays until something clears it.")]
    public bool releaseEffectsOnEnd = true;

    /// <summary>The music's fade when the dialogue ends: End Music Fade, or End Fade Duration while it's negative.</summary>
    public float EndMusicFadeDuration => endMusicFade < 0f ? endFadeDuration : endMusicFade;

    /// <summary>The controller playing this dialogue. Runtime only, set by <see cref="Bind"/>.</summary>
    [NonSerialized] private VisualNovelTalkable bound;

    protected override void OnEnable()
    {
        base.OnEnable();
        SyncNodeSettings();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SyncNodeSettings();
    }
#endif

    /// <summary>
    /// Resizes nodeSettings so there is exactly one row per node. Preserves authored values.
    /// </summary>
    public void SyncNodeSettings()
    {
        if (nodeSettings == null) nodeSettings = new List<VisualNovelNodeSettings>();

        int nodeCount = nodes != null ? nodes.Count : 0;
        while (nodeSettings.Count < nodeCount) nodeSettings.Add(new VisualNovelNodeSettings());
        while (nodeSettings.Count > nodeCount) nodeSettings.RemoveAt(nodeSettings.Count - 1);

        for (int i = 0; i < nodeSettings.Count; i++)
            if (nodeSettings[i] == null) nodeSettings[i] = new VisualNovelNodeSettings();
    }

    /// <summary>The settings row for node <paramref name="nodeIdx"/>, or null when there is none.</summary>
    public VisualNovelNodeSettings SettingsFor(int nodeIdx)
    {
        if (nodeSettings == null || nodeIdx < 0 || nodeIdx >= nodeSettings.Count) return null;
        return nodeSettings[nodeIdx];
    }

    /// <summary>Binds the controller that stages this dialogue. Called right before it starts.</summary>
    public void Bind(VisualNovelTalkable controller)
    {
        SyncNodeSettings();
        AssignNodeIds();
        bound = controller;
    }

    // ---------------------------------------------------------------------
    // IMultiObjectDialogue - the DialogueManager's node lifecycle, forwarded to the controller.
    // ---------------------------------------------------------------------

    public void OnDialogueStart()
    {
        if (bound) bound.OnDialogueStart(this);
    }

    /// <summary>
    /// Stages the node: background transition, audio, SFX and effects. The DialogueManager yields on
    /// this, so the node's text waits for whatever the node's settings say to wait for.
    /// </summary>
    public IEnumerator OnNodeStart(DialogueNode node, MonoBehaviour runner)
    {
        if (!bound || node == null) yield break;
        yield return bound.PlayNode(this, node);
    }

    /// <summary>
    /// Nothing to stop here: OnNodeEnd fires when the text finishes typing, but a node's background
    /// and sustained effects last until the NEXT node starts.
    /// </summary>
    public void OnNodeEnd(DialogueNode node) { }

    public void OnDialogueEnd()
    {
        // Drop the binding first so nothing re-enters a dialogue that is shutting down.
        VisualNovelTalkable controller = bound;
        bound = null;
        if (controller) controller.OnDialogueEnd(this);
    }
}
