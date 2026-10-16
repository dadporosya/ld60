using System.Collections;
using UnityEngine;

public class PosTracker : MonoBehaviour
{
    [SerializeField] private float timeGap;
    [SerializeField] private bool off=false;
    void Start()
    {
        StartCoroutine(TrackPos());
    }

    IEnumerator TrackPos()
    {
        Transform t = transform;
        while (true)
        {
            yield return new WaitForSeconds(timeGap);
            if (!off) Debug.Log($"{gameObject.name}: {t.position.x} {t.position.y}");
        }
    }
}
