using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// The hero's list of people to call, written into one text: names already called are struck
/// through. Refreshes itself whenever the <see cref="FuneralGameManager"/> reports a call.
/// </summary>
public class ContactListView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [Tooltip("{0} = name.")]
    [SerializeField, Localized] private string uncalledFormat = "{0}";
    [Tooltip("{0} = name.")]
    [SerializeField, Localized] private string calledFormat = "<s>{0}</s>";
    [Tooltip("{0} = name. Used for calls where the dice reached the target, when not empty.")]
    [SerializeField, Localized] private string calledGoodFormat = "";

    private void Reset() => text = GetComponent<TextMeshProUGUI>();

    private void OnEnable()
    {
        if (FuneralGameManager.Instance) FuneralGameManager.Instance.onContactsChanged.AddListener(Refresh);
        Refresh();
    }

    private void OnDisable()
    {
        if (FuneralGameManager.Instance) FuneralGameManager.Instance.onContactsChanged.RemoveListener(Refresh);
    }

    private void Start()
    {
        // The manager may wake after this view on the first frame.
        if (FuneralGameManager.Instance)
        {
            FuneralGameManager.Instance.onContactsChanged.RemoveListener(Refresh);
            FuneralGameManager.Instance.onContactsChanged.AddListener(Refresh);
        }
        Refresh();
    }

    public void Refresh()
    {
        FuneralGameManager game = FuneralGameManager.Instance;
        if (!text || !game) return;

        StringBuilder sb = new StringBuilder();
        foreach (ContactData contact in game.Contacts)
        {
            if (!contact) continue;

            string format = uncalledFormat;
            if (game.HasCalled(contact))
            {
                bool good = game.WasCallGood(contact) && !string.IsNullOrEmpty(calledGoodFormat);
                format = good ? calledGoodFormat : calledFormat;
            }

            sb.AppendLine(Loc.F(format, Loc.T(contact.DisplayName)));
        }

        text.text = sb.ToString();
    }
}
