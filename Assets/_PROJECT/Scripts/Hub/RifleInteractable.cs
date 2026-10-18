using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rifle, shown up close like any showcase object. Any time but right after a lost round it plays its
/// <see cref="showcaseDialogue"/>. Right after a lost round (<see cref="LevelManager.LastRoundLost"/>, back in the
/// hub) it plays the <see cref="beforeDeathDialogue"/> instead, and then the picture stays up with a fork under it,
/// looking like the death cutscene's: Yes switches the music to <see cref="deathBgm"/> and plays the bad ending
/// (the death cutscene), No closes the showcase.
/// </summary>
public class RifleInteractable : ShowcaseInteractableBase
{
    [Header("Rifle")]
    [Tooltip("Played over the showcase when the rifle is looked at any time but right after a lost round.")]
    [SerializeField] private DialogueContainer showcaseDialogue;

    [Header("Right after a lost round")]
    [Tooltip("Played over the showcase before the fork. Empty: the fork comes up as soon as the picture is in.")]
    [SerializeField] private DialogueContainer beforeDeathDialogue;
    [Tooltip("The fork's line that plays the death cutscene.")]
    [SerializeField] private string yesTitle = "Yes";
    [Tooltip("The fork's line that closes the showcase.")]
    [SerializeField] private string noTitle = "No";
    [Tooltip("The fork looks like this death cutscene's (its Fork look). Empty: the FuneralGameManager's Death Cutscene.")]
    [SerializeField] private DeathCutscene forkLookFrom;

    [Header("Music")]
    [Tooltip("Crossfaded to when Yes is picked, for the death cutscene. Empty: the music carries on as it is.")]
    [SerializeField] private BGMObject deathBgm;
    [SerializeField, Min(0f)] private float deathBgmFade = 1.5f;

    private ChoiceFork fork;

    protected override DialogueContainer CurrentDialogue => showcaseDialogue;

    /// <summary>A click now leads to the death fork: the last round was lost, and the player is back in the hub.</summary>
    private static bool DeathNow =>
        LevelManager.Instance && LevelManager.Instance.LastRoundLost &&
        FuneralGameManager.Instance && FuneralGameManager.Instance.CurrentPhase == FuneralGameManager.Phase.Hub;

    protected override bool IsAvailable() => DeathNow ? !ShowcaseBusy : base.IsAvailable();

    protected override void Interact()
    {
        if (!DeathNow)
        {
            base.Interact();
            return;
        }

        ShowcaseDialogueManager showcase = FindShowcase();
        if (!showcase) return;

        GetPicture(out List<Sprite> frames, out List<float> durations, out bool loop);
        showcase.ShowAndHold(frames, durations, loop, beforeDeathDialogue, AskToDie, Speaker);
    }

    private void OnDestroy()
    {
        if (fork) Destroy(fork.gameObject);
    }

    /// <summary>The dialogue is over, the picture still up: Yes or No.</summary>
    private void AskToDie()
    {
        if (!fork) fork = ChoiceFork.Create(ForkLook());
        fork.Show(new[] { yesTitle, noTitle }, index =>
        {
            if (index == 0) Die();
            else if (ShowcaseDialogueManager.Instance) ShowcaseDialogueManager.Instance.Close();
        });
    }

    private void Die()
    {
        FuneralGameManager game = FuneralGameManager.Instance;
        if (!game)
        {
            h.Out($"RifleInteractable '{name}': no FuneralGameManager to play the bad ending.");
            if (ShowcaseDialogueManager.Instance) ShowcaseDialogueManager.Instance.Close();
            return;
        }

        if (deathBgm && SongManager.Instance) SongManager.Instance.Play(deathBgm, deathBgmFade);
        // Closes the showcase itself.
        game.BadEnding();
    }

    private ChoiceForkLook ForkLook()
    {
        DeathCutscene source = forkLookFrom;
        if (!source && FuneralGameManager.Instance) source = FuneralGameManager.Instance.DeathCutscene;
        if (!source) return new ChoiceForkLook();

        ChoiceForkLook look = source.ForkLook;
        // A parent inside a prefab asset (a cutscene that is a prefab) can't hold the lines.
        if (look.parent && !look.parent.gameObject.scene.IsValid()) look.parent = null;
        return look;
    }
}
