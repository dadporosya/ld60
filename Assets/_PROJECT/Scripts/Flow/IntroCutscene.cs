using System;
using System.Collections;
using PrimeTween;
using UnityEngine;

/// <summary>
/// The game's opening: the intro visual novel dialogue, then the <see cref="FadeImageController"/> covers
/// the stage with black and fades away again, revealing the hub.
///
/// <para>Put it on its OWN GameObject (or a prefab): <see cref="CutSceneBase.Run"/> clones the whole
/// GameObject it sits on. <see cref="FuneralGameManager"/> plays it through <see cref="Play"/>; with
/// <c>runOnStart</c> it plays by itself instead.</para>
///
/// <para>Cutting the dialogue off (<see cref="HubDialogueRunner.Interrupt"/>, e.g. a debug round) ends the
/// cutscene on the spot, without the fades and without the callback - like the plain intro dialogue.</para>
/// </summary>
public class IntroCutscene : CutSceneBase
{
    [Header("Dialogue")]
    [SerializeField] private VisualNovelDialogueContainer dialogue;

    [Header("Fade")]
    [Tooltip("Seconds the dialogue stage fades to black over once the dialogue is over.")]
    [SerializeField, Min(0f)] private float fadeInDuration = 1f;
    [Tooltip("Seconds the screen stays black (the stage is hidden behind it).")]
    [SerializeField, Min(0f)] private float blackHoldDuration = 0.5f;
    [Tooltip("Seconds the black fades away over, revealing the hub.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 1.5f;
    [SerializeField] private Ease fadeInEase = Ease.Linear;
    [SerializeField] private Ease fadeOutEase = Ease.Linear;

    // Set on the running clone by Play; called once the hub is revealed.
    private Action onFinished;

    /// <summary>Runs the cutscene, then <paramref name="finished"/> once the hub is back on screen.</summary>
    public void Play(Action finished)
    {
        IntroCutscene instance = RunInstance() as IntroCutscene;
        if (instance) instance.onFinished = finished;
        else finished?.Invoke();
    }

    public override void Init()
    {
        base.Init();
        cutsceneSteps.Clear();
        cutsceneSteps.Add(DialogueStep());
        cutsceneSteps.Add(FadeStep());
    }

    private IEnumerator DialogueStep()
    {
        HubDialogueRunner runner = HubDialogueRunner.Instance;
        if (!runner || !dialogue) yield break;

        bool done = false;
        // The stage stays up so the black covers it, rather than the hub flashing in between.
        runner.Play(dialogue, () => done = true, hideStage: false);

        // The stage's own end fade (Fade To Color On End) still runs after the dialogue is over: only a dropped
        // callback means it was cut off.
        while (!done && (runner.IsPlaying || runner.IsFinishing)) yield return null;

        if (!done)
        {
            // Interrupted: its callback was dropped, so nothing else of the intro plays.
            h.Out("IntroCutscene: dialogue was cut off, cutscene ends.");
            DestroyCutscene();
            StopAllCoroutines();
        }
    }

    private IEnumerator FadeStep()
    {
        FadeImageController fade = FadeImageController.Instance;
        HubDialogueRunner runner = HubDialogueRunner.Instance;

        if (!fade)
        {
            if (runner) runner.HideStage();
            Finish();
            yield break;
        }

        yield return Fade(fade, 1f, fadeInDuration, fadeInEase);
        if (runner) runner.HideStage();

        if (blackHoldDuration > 0f) yield return new WaitForSecondsRealtime(blackHoldDuration);

        yield return Fade(fade, 0f, fadeOutDuration, fadeOutEase);
        Finish();
    }

    private static IEnumerator Fade(FadeImageController fade, float alpha, float duration, Ease ease)
    {
        if (duration <= 0f)
        {
            fade.SetAlpha(alpha);
            yield break;
        }

        Tween tween = alpha > 0f
            ? fade.FadeIn(duration, ease, useUnscaledTime: true)
            : fade.Fadeout(duration, ease, useUnscaledTime: true);
        yield return tween.ToYieldInstruction();
    }

    private void Finish()
    {
        Action callback = onFinished;
        onFinished = null;
        callback?.Invoke();
    }
}
