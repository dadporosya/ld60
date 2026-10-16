using System;
using UnityEngine;

public class RotatingGO : MonoBehaviour
{
    [SerializeField] public float speed = 90f;
    [SerializeField] public bool reverse;
    [SerializeField] public bool randReverse;
    [HideInInspector]public float direction;
    

    private void Start()
    {
        if (randReverse) reverse = h.RandChoice(true, false);
        direction = reverse ? -1 : 1;
    }

    private void Update()
    {
        transform.Rotate(0f, 0f, speed * direction * Time.deltaTime);
    }
}
