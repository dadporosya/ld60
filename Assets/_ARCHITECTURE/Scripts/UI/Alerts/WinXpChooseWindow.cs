using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// A <see cref="WinXPMessageAlert"/> that asks a question instead of only stating something: it carries
/// a list of choice buttons and a parallel list of events, and pressing the i-th button invokes the
/// i-th event.
///
/// <code>
/// // from code, after spawning:
/// window.SetChoices(
///     ("Yes", () => Accept()),
///     ("No",  () => Refuse()));
/// </code>
///
/// <b>Filling the button list.</b> Leave <see cref="choiceButtons"/> empty and set
/// <see cref="buttonParent"/> instead — every <see cref="Button"/> under that transform is collected in
/// sibling order on Start, so adding a choice to the prefab is just adding a button to the row. Buttons
/// listed explicitly in <see cref="choiceButtons"/> are kept and children that are already in the list
/// are not added twice, so the two can be mixed.
///
/// <b>Pairing.</b> <see cref="choiceEvents"/> is matched to <see cref="choiceButtons"/> by index. A
/// button with no event at its index still closes the window (unless <see cref="closeOnChoice"/> is
/// off) — it is a plain "dismiss" choice.
///
/// <b>Detours.</b> <see cref="closeOnChoice"/> is all-or-nothing; <see cref="keepOpenChoices"/> is the
/// exception list. A choice on it runs its event and leaves the window where it is, for a button that
/// opens something else the player is meant to come back from — and, since it is not an answer, it does
/// not use up the one press <see cref="singleChoice"/> allows.
/// </summary>
public class WinXpChooseWindow : WinXPMessageAlert
{
    [Header("Choices")]
    [Tooltip("The choice buttons, in order. Buttons found under 'buttonParent' are appended to this " +
             "list on Start, so it can be left empty when the parent holds them all.")]
    public List<Button> choiceButtons = new List<Button>();

    [Tooltip("Every Button under this transform is added to 'choiceButtons' on Start, in hierarchy " +
             "order. Optional — leave empty to only use the buttons listed above.")]
    public Transform buttonParent;

    [Tooltip("Invoked when the button with the same index is pressed. Shorter than the button list is " +
             "fine: those buttons just close the window.")]
    public List<UnityEvent> choiceEvents = new List<UnityEvent>();

    [Header("Behaviour")]
    [Tooltip("Close (and destroy) the window after a choice is made. Turn off for windows that stay " +
             "open while their buttons do something.")]
    public bool closeOnChoice = true;

    [Tooltip("Only let one choice be made. Presses after the first are ignored, which matters when the " +
             "window is kept open. A choice listed in 'keepOpenChoices' does not count as that one " +
             "choice — it is not an answer, so it never locks the window.")]
    public bool singleChoice = true;

    [Tooltip("Indices of choices that leave the window standing even with 'closeOnChoice' on — a button " +
             "that opens something ELSE (a browser, a sub-window) and is meant to be come back from. " +
             "Per choice, so the same window can mix a final answer with a detour.")]
    public List<int> keepOpenChoices = new List<int>();

    /// <summary>Index of the choice that was made, or -1 while none has been.</summary>
    public int ChosenIndex { get; private set; } = -1;

    /// <summary>Raised with the index of the pressed button, whatever that button's own event does.</summary>
    public event System.Action<int> OnChoice;

    // The "the window went away" event lives on WinXPMessageAlert (OnClosed) — a listener reads
    // ChosenIndex off the window it is handed to tell a choice apart from a plain dismissal (-1).

    // Kept so the listeners added in Start can be taken off again in Close/OnDestroy.
    private readonly List<UnityAction> handlers = new List<UnityAction>();

    protected override void Start()
    {
        CollectButtons();

        // After the base Start: accept/close listeners and the alert sound belong to the base window.
        base.Start();

        WireButtons();
    }

    // ================================================================ Setup

    /// <summary>
    /// Appends every <see cref="Button"/> under <see cref="buttonParent"/> to <see cref="choiceButtons"/>,
    /// skipping ones already listed. Safe to call again after adding buttons at runtime.
    /// </summary>
    public void CollectButtons()
    {
        if (!buttonParent) return;

        // Includes inactive children so a hidden-then-shown choice still gets wired.
        Button[] found = buttonParent.GetComponentsInChildren<Button>(true);

        foreach (Button button in found)
        {
            if (!button) continue;
            if (button == acceptBtn || button == closeBtn) continue; // those are the base window's
            if (choiceButtons.Contains(button)) continue;

            choiceButtons.Add(button);
        }
    }

    // One listener per button, capturing its index so the press knows which choice it was.
    private void WireButtons()
    {
        UnwireButtons();

        for (int i = 0; i < choiceButtons.Count; i++)
        {
            Button button = choiceButtons[i];
            if (!button)
            {
                handlers.Add(null);
                continue;
            }

            int index = i; // captured per iteration, not shared
            UnityAction handler = () => Choose(index);

            handlers.Add(handler);
            button.onClick.AddListener(handler);
        }
    }

    private void UnwireButtons()
    {
        for (int i = 0; i < handlers.Count && i < choiceButtons.Count; i++)
        {
            if (choiceButtons[i] && handlers[i] != null) choiceButtons[i].onClick.RemoveListener(handlers[i]);
        }

        handlers.Clear();
    }

    // ================================================================ Choosing

    /// <summary>
    /// Makes the i-th choice: invokes <see cref="choiceEvents"/>[i] (when there is one), raises
    /// <see cref="OnChoice"/> and closes the window if <see cref="closeOnChoice"/> is on. Public so a
    /// choice can also be triggered from code or from a button wired in the inspector.
    /// </summary>
    public void Choose(int index)
    {
        if (closing) return;

        // A keep-open choice is a detour, not an answer: it leaves the window usable, so it must not be
        // the one choice 'singleChoice' allows either. Only a choice that would have closed the window
        // locks it.
        if (singleChoice && ChosenIndex >= 0 && !KeepsOpen(ChosenIndex)) return;

        if (index < 0 || index >= choiceButtons.Count)
        {
            h.Out($"WinXpChooseWindow: choice {index} is out of range (0..{choiceButtons.Count - 1})");
            return;
        }

        ChosenIndex = index;

        // The event may destroy this window, so everything that must happen is done before invoking it.
        UnityEvent chosen = (index < choiceEvents.Count) ? choiceEvents[index] : null;

        OnChoice?.Invoke(index);
        chosen?.Invoke();

        if (closeOnChoice && !KeepsOpen(index) && this) Close();
    }

    /// <summary>True when the i-th choice is one that leaves the window open — see <see cref="keepOpenChoices"/>.</summary>
    public bool KeepsOpen(int index) => keepOpenChoices != null && keepOpenChoices.Contains(index);

    /// <summary>
    /// Marks the i-th choice as one that does (not) close the window, the code-side twin of
    /// <see cref="keepOpenChoices"/>. Safe before the buttons exist: it only edits the index list.
    /// </summary>
    public void SetChoiceKeepsOpen(int index, bool keepOpen)
    {
        if (index < 0) return;

        keepOpenChoices ??= new List<int>();

        if (keepOpen)
        {
            if (!keepOpenChoices.Contains(index)) keepOpenChoices.Add(index);
        }
        else
        {
            keepOpenChoices.Remove(index);
        }
    }

    // ================================================================ Runtime setup helpers

    /// <summary>
    /// Sets the callback for the i-th button, replacing whatever was there. The button must already
    /// exist — this fills the event list, it does not create buttons.
    /// </summary>
    public void SetChoiceEvent(int index, UnityAction action)
    {
        if (index < 0)
        {
            h.Out($"WinXpChooseWindow: negative choice index {index}");
            return;
        }

        while (choiceEvents.Count <= index) choiceEvents.Add(new UnityEvent());

        choiceEvents[index] = new UnityEvent();
        if (action != null) choiceEvents[index].AddListener(action);
    }

    /// <summary>Adds a callback to the i-th button's event, keeping any already on it.</summary>
    public void AddChoiceListener(int index, UnityAction action)
    {
        if (action == null || index < 0) return;

        while (choiceEvents.Count <= index) choiceEvents.Add(new UnityEvent());
        if (choiceEvents[index] == null) choiceEvents[index] = new UnityEvent();

        choiceEvents[index].AddListener(action);
    }

    /// <summary>
    /// Labels the existing buttons and hooks their callbacks in one call — the usual way to configure a
    /// spawned window from code. Extra buttons beyond the given choices are hidden; passing a null or
    /// empty label leaves the button's own text alone.
    /// </summary>
    public void SetChoices(params (string label, UnityAction action)[] choices)
    {
        CollectButtons();

        for (int i = 0; i < choiceButtons.Count; i++)
        {
            Button button = choiceButtons[i];
            if (!button) continue;

            bool used = choices != null && i < choices.Length;
            button.gameObject.SetActive(used);
            if (!used) continue;

            if (!string.IsNullOrEmpty(choices[i].label)) SetButtonLabel(button, choices[i].label);
            SetChoiceEvent(i, choices[i].action);
        }

        // Called before Start when the window was just instantiated; wiring then happens in Start.
        if (handlers.Count > 0) WireButtons();
    }

    /// <summary>Writes <paramref name="label"/> into the button's TMP or legacy Text, whichever it has.</summary>
    public static void SetButtonLabel(Button button, string label)
    {
        if (!button) return;

        TMPro.TMP_Text tmp = button.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (tmp)
        {
            tmp.text = label;
            return;
        }

        Text legacy = button.GetComponentInChildren<Text>(true);
        if (legacy) legacy.text = label;
    }

    // ================================================================ Teardown

    public override void Close()
    {
        if (closing) return;

        UnwireButtons();

        // The base raises OnClosed before it destroys the object, so listeners still get a live
        // reference — and can read ChosenIndex off it.
        base.Close();
    }

    protected override void OnDestroy()
    {
        UnwireButtons();

        // The base raises OnClosed here when the window never went through Close().
        base.OnDestroy();
    }
}
