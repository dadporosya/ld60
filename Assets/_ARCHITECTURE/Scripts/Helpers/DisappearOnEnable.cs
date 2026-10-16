using UnityEngine;

public class DisappearOnEnable : MonoBehaviour
{
    void Start()
    {
        GetComponent<SpriteRenderer>().enabled = false;
        // h.FadeOut(gameObject, 0, this);
    }
}
