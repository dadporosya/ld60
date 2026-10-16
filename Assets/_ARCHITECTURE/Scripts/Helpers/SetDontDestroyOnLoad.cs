using UnityEngine;

/// <summary>
/// Marks THIS GameObject as persistent across scene loads.
///
/// Unity's DontDestroyOnLoad only accepts ROOT GameObjects. An earlier version passed
/// <c>transform.root.gameObject</c> to silence the "only works for root GameObjects" warning —
/// but every object carrying this component sits under <c>----MANAGERS-------</c>, so that
/// promoted the WHOLE manager hierarchy to DontDestroyOnLoad and every manager survived scene
/// loads, duplicating on the next scene's own managers. Never walk up to the root here: only the
/// objects that are roots themselves are meant to persist.
///
/// The parent check keeps the old behaviour (a child simply does not persist) without the
/// warning spam that motivated the change — warnings cost a managed stack trace each.
/// </summary>
public class SetDontDestroyOnLoad : MonoBehaviour
{
    private void Awake()
    {
        if (transform.parent) return;

        DontDestroyOnLoad(gameObject);
    }
}
