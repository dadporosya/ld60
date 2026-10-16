using System.Collections.Generic;
using UnityEngine;

public class GameFlowManager : MonoBehaviour
{
    public static GameFlowManager Instance; // Instance

    public enum State
    {
        Intro,
        Game,
        Pause,
        Loss,
        Finale
    }
    
    private List<State> onPauseStates = new List<State>()
    {
        State.Pause, State.Loss, State.Intro
    };

    private List<State> onGameStates = new List<State>()
    {
        State.Game
    };
    
    public State state = State.Game;

    // The state the game was in when it was last put on pause, so Resume() can hand it back instead of
    // forcing Game — a pause opened on top of an Intro/cutscene must not end up starting the game.
    private State stateBeforePause = State.Game;
    private bool hasStateBeforePause;

    public void SetOnPause()
    {
        // Only the FIRST pause records where we came from: a second pause on top of a pause would
        // otherwise remember Pause itself and Resume would have nothing to go back to.
        if (state != State.Pause)
        {
            stateBeforePause = state;
            hasStateBeforePause = true;
        }

        state = State.Pause;
        h.Out("STATE PAUSE");
    }

    public void SetOnGame()
    {
        state = State.Game;
        hasStateBeforePause = false;
        h.Out("STATE GAME");
    }

    /// <summary>
    /// Undoes the last <see cref="SetOnPause"/>, putting the game back into whatever state it was in
    /// before it (Game in the normal case, but Intro while a cutscene is running).
    ///
    /// <para>Does nothing when the game is no longer paused — if something moved it on to Loss or Finale
    /// while a window was open, closing that window must not drag it back.</para>
    /// </summary>
    public void Resume()
    {
        if (state != State.Pause) return;

        state = hasStateBeforePause ? stateBeforePause : State.Game;
        hasStateBeforePause = false;

        h.Out($"STATE RESUME → {state}");
    }

    /// <summary>Reads as the pair of <see cref="Resume"/> at a call site; same thing as <see cref="SetOnPause"/>.</summary>
    public void Pause() => SetOnPause();

    public bool IsGame()
    {
        return onGameStates.Contains(state);
    }
    public bool IsPaused()
    {
        return onPauseStates.Contains(state);
    }
    
    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }
    
    
    
}
