using UnityEngine;

public class SetActiveFalseOnEnable : MonoBehaviour
{
    void Start()
    {
        gameObject.SetActive(true);
        gameObject.SetActive(false);
    }
}
