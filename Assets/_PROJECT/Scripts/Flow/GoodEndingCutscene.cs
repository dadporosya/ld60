using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The good ending - the hero calls his father - in four steps:
/// <list type="number">
/// <item>The opening black (<see cref="openOnBlack"/>): the screen fades to black, holds, then the black fades away
/// as the visual novel dialogue starts. The music crossfades into <see cref="bgm"/> as it goes black.</item>
/// <item>The visual novel dialogue (<see cref="vnDialogue"/>), cutting off whatever dialogue was playing.</item>
/// <item>The black: the <see cref="FadeImageController"/> covers the screen, every sound (music, ambients, whatever
/// still plays) fades out (<see cref="blackSoundsFadeOut"/>), and the silence holds (<see cref="blackHold"/>).
/// Then the phone rings (<see cref="ringClip"/>, looping, like before an outcome call) until the call's first
/// line starts typing.</item>
/// <item>The call (<see cref="callDialogue"/>): a plain text box dialogue over the black, its nodes spoken by
/// <see cref="playerTalkable"/> (speaker id <see cref="playerSpeakerId"/>) and <see cref="fatherTalkable"/>
/// (<see cref="fatherSpeakerId"/>). Then everything fades out and the game closes.</item>
/// </list>
///
/// <para>Put it on its OWN GameObject (or a prefab): <see cref="CutSceneBase.Run"/> clones the whole GameObject
/// it sits on. <see cref="FuneralGameManager.GoodEnding"/> plays it through <see cref="Play"/>; with
/// <c>runOnStart</c> it plays by itself instead. Everything runs on unscaled time.</para>
/// </summary>
public class GoodEndingCutscene : CutSceneBase
{
    [Header("Music")]
    [Tooltip("Crossfaded to as the cutscene starts (with the opening black, or with the dialogue without one). " +
             "Empty: the music keeps playing.")]
    [SerializeField] private BGMObject bgm;
    [Tooltip("Seconds the new music crossfades in over.")]
    [SerializeField, Min(0f)] private float bgmFade = 1.5f;
    [Tooltip("The new music plays through once, then falls silent. Off: it loops.")]
    [SerializeField] private bool bgmOnce = true;

    [Header("0. Opening black")]
    [Tooltip("The cutscene opens by fading to black and holding it before the visual novel dialogue. " +
             "Off: the dialogue cuts straight in.")]
    [SerializeField] private bool openOnBlack = true;
    [Tooltip("Seconds the screen fades to black over as the cutscene starts.")]
    [SerializeField, Min(0f)] private float openingFadeDuration = 1.5f;
    [SerializeField] private Ease openingFadeEase = Ease.Linear;
    [Tooltip("Seconds the black holds before the visual novel dialogue starts.")]
    [SerializeField, Min(0f)] private float openingHold = 2f;
    [Tooltip("Seconds the black fades away over as the visual novel dialogue starts. " +
             "Negative: it stays, and the dialogue plays over the black.")]
    [SerializeField] private float openingReveal = 1.5f;
    [SerializeField] private Ease openingRevealEase = Ease.Linear;

    [Header("1. Visual novel dialogue")]
    [SerializeField] private VisualNovelDialogueContainer vnDialogue;

    [Header("2. Black screen")]
    [Tooltip("Seconds the screen fades to black over once the visual novel dialogue is over.")]
    [SerializeField, Min(0f)] private float blackFadeDuration = 1.5f;
    [SerializeField] private Ease blackFadeEase = Ease.Linear;
    [Tooltip("Seconds every sound (music, ambients, anything still playing) fades out over, starting with the black " +
             "once the visual novel dialogue is over.")]
    [SerializeField, Min(0f)] private float blackSoundsFadeOut = 2f;
    [Tooltip("Seconds the black holds in silence before the phone starts ringing.")]
    [SerializeField, Min(0f)] private float blackHold = 4f;

    [Header("3. The call")]
    [Tooltip("Looped from the end of the black hold until the call's first line starts typing (the phone ringing). " +
             "Empty: no ringing.")]
    [SerializeField] private AudioClip ringClip;
    [SerializeField, Range(0f, 1f)] private float ringVolume = 1f;
    [Tooltip("Seconds the phone rings before the call's first line.")]
    [SerializeField, Min(0f)] private float ringHold = 3.33f;
    [Tooltip("Seconds the ringing fades out over once the call's first line starts.")]
    [SerializeField, Min(0f)] private float ringFadeOut = 0.25f;
    [Tooltip("Plain text box dialogue over the black: the call with dad.")]
    [SerializeField] private DialogueContainer callDialogue;
    [Tooltip("The hero: speaks the call's nodes with the Player Speaker Id (name, portrait, voice, text color).")]
    [SerializeField] private Talkable playerTalkable;
    [SerializeField] private string playerSpeakerId = "1";
    [Tooltip("Dad: speaks the call's nodes with the Father Speaker Id.")]
    [SerializeField] private Talkable fatherTalkable;
    [SerializeField] private string fatherSpeakerId = "2";

    [Header("4. The end")]
    [Tooltip("Played once the call's last line is over (the line going dead). Empty: nothing.")]
    [SerializeField] private AudioClip hangUpSfx;
    [SerializeField, Range(0f, 1f)] private float hangUpVolume = 1f;
    [Tooltip("Seconds the black holds after the call before everything fades out.")]
    [SerializeField, Min(0f)] private float endHold = 3f;
    [Tooltip("Seconds the music and every other sound fade out over before the game closes.")]
    [SerializeField, Min(0f)] private float endFadeOut = 3f;
    [Tooltip("Close the game once everything has faded out. Off: the screen just stays black.")]
    [SerializeField] private bool quitAtEnd = true;
    [Tooltip("The last frame before the game closes (Quit At End): the credits on black.")]
    [SerializeField] private CreditsFrame credits = new CreditsFrame();

    private AudioSource ringSource;
    private AudioSource sfxSource;

    /// <summary>
    /// Whether it wants the screen black from the start: it opens on the black (step 0), or has no visual novel
    /// dialogue and goes straight to the black step.
    /// </summary>
    public bool OpensOnBlack => openOnBlack || !vnDialogue;

    /// <summary>Runs the cutscene. It never hands control back: it ends by closing the game (<see cref="quitAtEnd"/>).</summary>
    public void Play() => RunInstance();

    public override void Init()
    {
        base.Init();
        cutsceneSteps.Clear();
        cutsceneSteps.Add(OpeningStep());
        cutsceneSteps.Add(DialogueStep());
        cutsceneSteps.Add(BlackStep());
        cutsceneSteps.Add(CallStep());
        cutsceneSteps.Add(EndStep());
    }

    // ---------------------------------------------------------------------
    // Steps
    // ---------------------------------------------------------------------

    private IEnumerator OpeningStep()
    {
        if (!openOnBlack) yield break;

        // The new music comes in under the fade, not after it.
        PlayBgm();

        FadeImageController fade = FadeImageController.Instance;
        if (!fade) h.Out("GoodEndingCutscene: no FadeImageController, the screen doesn't go black.");
        else if (openingFadeDuration <= 0f) fade.SetAlpha(1f);
        else yield return fade.FadeIn(openingFadeDuration, openingFadeEase, useUnscaledTime: true).ToYieldInstruction();

        if (openingHold > 0f) yield return new WaitForSecondsRealtime(openingHold);
    }

    private IEnumerator DialogueStep()
    {
        if (!openOnBlack) PlayBgm();

        HubDialogueRunner runner = HubDialogueRunner.Instance;
        if (!runner || !vnDialogue) yield break;

        bool done = false;
        // Cuts off whatever is playing; the stage stays up so the black covers it, rather than the hub flashing in between.
        runner.InterruptAndPlay(vnDialogue, () => done = true, hideStage: false);

        // The stage is set up under the opening black by now: let it show.
        FadeImageController fade = FadeImageController.Instance;
        if (openOnBlack && fade && openingReveal >= 0f)
        {
            if (openingReveal == 0f) fade.SetAlpha(0f);
            else fade.Fadeout(openingReveal, openingRevealEase, useUnscaledTime: true);
        }

        while (!done && runner.IsPlaying) yield return null;
        if (!done) h.Out("GoodEndingCutscene: the visual novel dialogue was cut off, the cutscene goes on.");
    }

    private IEnumerator BlackStep()
    {
        FadeImageController fade = FadeImageController.Instance;
        HubDialogueRunner runner = HubDialogueRunner.Instance;

        FadeOutOtherSounds(blackSoundsFadeOut);

        if (!fade) h.Out("GoodEndingCutscene: no FadeImageController, the screen doesn't go black.");
        else if (blackFadeDuration <= 0f) fade.SetAlpha(1f);
        else yield return fade.FadeIn(blackFadeDuration, blackFadeEase, useUnscaledTime: true).ToYieldInstruction();

        if (runner) runner.HideStage();

        if (blackHold > 0f) yield return new WaitForSecondsRealtime(blackHold);
    }

    private IEnumerator CallStep()
    {
        if (ringClip)
        {
            if (!ringSource) ringSource = MakeSource("Ring", true);
            Tween.StopAll(ringSource);
            ringSource.clip = ringClip;
            ringSource.volume = ringVolume;
            ringSource.Play();

            if (ringHold > 0f) yield return new WaitForSecondsRealtime(ringHold);
        }

        if (!callDialogue) h.Out("GoodEndingCutscene: no Call Dialogue, straight to the end.");
        yield return PlayCall(callDialogue);

        // Without a dialogue (or a DialogueManager) nothing has stopped it yet.
        StopRing();
    }

    private IEnumerator EndStep()
    {
        DialogueManager manager = DialogueManager.Instance;
        while (manager && (manager.DialogueActive || manager.IsClosing)) yield return null;

        if (hangUpSfx)
        {
            if (!sfxSource) sfxSource = MakeSource("Hang Up SFX", false);
            sfxSource.PlayOneShot(hangUpSfx, hangUpVolume);
        }

        if (endHold > 0f) yield return new WaitForSecondsRealtime(endHold);

        FadeOutOtherSounds(endFadeOut);
        if (endFadeOut > 0f) yield return new WaitForSecondsRealtime(endFadeOut);

        if (!quitAtEnd) yield break;
        yield return credits.Play(transform);
        QuitGame();
    }

    // ---------------------------------------------------------------------
    // The call
    // ---------------------------------------------------------------------

    /// <summary>
    /// Plays <paramref name="dialogue"/> in the <see cref="DialogueManager"/>'s text box, spoken by the two
    /// talkables, and waits for its end. The ringing stops as its first line starts.
    /// </summary>
    private IEnumerator PlayCall(DialogueContainer dialogue)
    {
        if (!dialogue) yield break;

        DialogueManager manager = DialogueManager.Instance;
        if (!manager)
        {
            h.Out($"GoodEndingCutscene: no DialogueManager, skipping '{dialogue.name}'.");
            yield break;
        }

        while (manager && manager.DialogueActive) yield return null;
        if (!manager) yield break;

        // A runtime copy: the asset can't hold scene objects, and starting the nodes writes speakers into them.
        DialogueContainer instance = Instantiate(dialogue);
        instance.name = dialogue.name;
        SetSpeaker(instance, playerSpeakerId, playerTalkable);
        SetSpeaker(instance, fatherSpeakerId, fatherTalkable);
        instance.AssignNodeIds();
        foreach (DialogueNode node in instance.nodes) node?.Init();

        bool ended = false;
        UnityAction onEnded = () => ended = true;
        UnityAction onFirstNode = StopRing;
        if (manager.onDialogueEnd == null) manager.onDialogueEnd = new UnityEvent();
        if (manager.onStartNode == null) manager.onStartNode = new UnityEvent();
        manager.onDialogueEnd.AddListener(onEnded);
        manager.onStartNode.AddListener(onFirstNode);
        manager.StartDialogue(instance);

        while (!ended && manager) yield return null;

        if (manager)
        {
            manager.onDialogueEnd.RemoveListener(onEnded);
            manager.onStartNode.RemoveListener(onFirstNode);
        }
        Destroy(instance);
    }

    /// <summary>Points the nodes with speaker id <paramref name="id"/> at <paramref name="talkable"/>.</summary>
    private static void SetSpeaker(DialogueContainer dialogue, string id, Talkable talkable)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!talkable)
        {
            h.Out($"GoodEndingCutscene: no talkable for speaker id '{id}' in '{dialogue.name}'.");
            return;
        }

        int index = dialogue.speakerIds.IndexOf(id);
        if (index < 0)
        {
            index = dialogue.speakerIds.Count;
            dialogue.speakerIds.Add(id);
        }

        while (dialogue.speakerPrefabs.Count <= index) dialogue.speakerPrefabs.Add(null);
        dialogue.speakerPrefabs[index] = talkable.gameObject;
        // The lookup the DialogueManager reads was filled when the copy was made, before these were set.
        dialogue.speakers[id] = talkable.gameObject;
    }

    /// <summary>Fades the ringing out (if it's on).</summary>
    private void StopRing()
    {
        if (!ringSource || !ringSource.isPlaying) return;

        Tween.StopAll(ringSource);
        if (ringFadeOut <= 0f) ringSource.Stop();
        else Tween.AudioVolume(ringSource, 0f, ringFadeOut, useUnscaledTime: true).OnComplete(ringSource, s => s.Stop());
    }

    // ---------------------------------------------------------------------
    // Audio
    // ---------------------------------------------------------------------

    /// <summary>
    /// Fades out the music (<see cref="SongManager"/> / <see cref="BGMManager"/>), every <see cref="AmbientNoise"/>,
    /// and any other AudioSource playing right now (stopped once silent, its volume put back for later use).
    /// The cutscene's own sources are left alone.
    /// </summary>
    private void FadeOutOtherSounds(float duration)
    {
        HashSet<AudioSource> handled = new HashSet<AudioSource>();

        FadeOutMusic(duration);
        if (BGMManager.Instance) handled.UnionWith(BGMManager.Instance.MusicSources);

        foreach (AmbientNoise noise in FindObjectsByType<AmbientNoise>(FindObjectsSortMode.None))
        {
            noise.Stop(duration);
            handled.UnionWith(noise.GetComponents<AudioSource>());
        }

        foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (!source.isPlaying || handled.Contains(source) || source.transform.IsChildOf(transform)) continue;

            Tween.StopAll(source);
            float volume = source.volume;
            if (duration <= 0f)
            {
                source.Stop();
                continue;
            }

            Tween.AudioVolume(source, 0f, duration, useUnscaledTime: true).OnComplete(source, s =>
            {
                s.Stop();
                s.volume = volume;
            });
        }
    }

    /// <summary>
    /// Crossfades the music into <see cref="bgm"/> (through the <see cref="SongManager"/> when there is one), once
    /// or looping (<see cref="bgmOnce"/>).
    /// </summary>
    private void PlayBgm()
    {
        if (!bgm) return;

        if (SongManager.Instance)
        {
            if (bgmOnce) SongManager.Instance.PlayOnce(bgm, bgmFade);
            else SongManager.Instance.Play(bgm, bgmFade);
        }
        else if (BGMManager.Instance)
        {
            if (bgmOnce) BGMManager.Instance.PlayBGMOnce(bgm, bgmFade, becomeDefault: false, returnToDefault: false);
            else BGMManager.Instance.PlayBGMAsNewDefault(bgm, bgmFade);
        }
        else h.Out($"GoodEndingCutscene: no SongManager / BGMManager to play {bgm.name}.");
    }

    /// <summary>Fades the music (<see cref="SongManager"/> / <see cref="BGMManager"/>) out over <paramref name="duration"/>.</summary>
    private static void FadeOutMusic(float duration)
    {
        if (SongManager.Instance) SongManager.Instance.Stop(duration);
        else if (BGMManager.Instance) BGMManager.Instance.FadeOutMusic(duration);
    }

    private AudioSource MakeSource(string sourceName, bool loop)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = AudioMixerManager.GetSFXGroup();
        return source;
    }

    // ---------------------------------------------------------------------
    // The end
    // ---------------------------------------------------------------------

    private static void QuitGame()
    {
        if (ShutdownManager.Instance) ShutdownManager.Instance.CloseGameImmediately();
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
