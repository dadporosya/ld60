using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DisableRaycasts : MonoBehaviour
{
    void Awake()
    {
        foreach (var g in GetComponentsInChildren<Graphic>(true))
        {
            g.raycastTarget = false;
        }

        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
        {
            t.raycastTarget = false;
        }
    }
}