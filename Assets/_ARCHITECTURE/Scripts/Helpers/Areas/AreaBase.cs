using System;
using UnityEngine;

public class AreaBase : MonoBehaviour
{
    [SerializeField] private float radiusMult=1;

    public virtual void Init()
    {
        transform.localScale *= radiusMult;
    }
    private void Start()
    {
        Init();
    }
}