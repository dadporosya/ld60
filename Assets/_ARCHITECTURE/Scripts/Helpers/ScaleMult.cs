using UnityEngine;

public class ScaleMult : MonoBehaviour
{
    [SerializeField] private Vector3 mult = new Vector3(1, 1, 1);

    void Start()
    {
        transform.localScale = Vector3.Scale(transform.localScale, mult);
    }
}
