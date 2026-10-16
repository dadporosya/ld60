using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ButtonCutscene : MonoBehaviour
{
    private Button button;
    public CutSceneBase cutscene;

    [Tooltip("How many times the button can start the cutscene before the listener is removed. 0 or less = unlimited.")]
    public int clicksBeforeRemove = 1;

    private int clickCount;
    private UnityAction listener;

    private void Start()
    {
        if (!button) button = GetComponent<Button>();
        if (!button) return;

        listener = OnClick;
        button.onClick.AddListener(listener);
    }

    private void OnClick()
    {
        CutSceneManager.Instance.RunCutscene(cutscene);

        if (clicksBeforeRemove <= 0) return;

        clickCount++;
        if (clickCount >= clicksBeforeRemove) RemoveListener();
    }

    public void RemoveListener()
    {
        if (!button || listener == null) return;

        button.onClick.RemoveListener(listener);
        listener = null;
    }
}
