using PrimeTween;
using UnityEngine;

public class MoveWithTarget : MonoBehaviour
{
    public Transform target;
    public float smoothness = 0.005f;

    private Vector3 targetPreviousPosition;

    public void Init(Transform targetIn=null)
    {
        if (targetIn) target =  targetIn;
        if (target)
            targetPreviousPosition = target.position;
    }
    
    void Start()
    {
        Init();
    }

    void Update()
    {
        if (!target) return;

        Vector3 v = target.position - targetPreviousPosition;
        
        // transform.position += v;

        Tween.Position(
            transform,
            endValue:transform.position + v,
            duration: smoothness,
            ease:Ease.OutQuad
            );
        
        // Vector3 desiredPosition = transform.position + v;
        //
        // transform.position = Vector3.Lerp(
        //     transform.position,
        //     desiredPosition,
        //     smoothness * Time.deltaTime
        // );

        targetPreviousPosition = target.position;
    }
}