using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFlowTargeting : MonoBehaviour
{
    public Transform target;
    public string targetTag="Player";
    public float smoothSpeed = 10f;
    public Vector3 desiredPosition;

    void Start()
    {
        if (targetTag != "" && !target)
        {
            GameObject t = GameObject.FindGameObjectWithTag(targetTag);
            if (t)
            {
                target = t.transform;
            }
        };
    }

    private void LateUpdate()
    {
        // return;
        if (!target) return;

        desiredPosition = new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z
        );

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            smoothSpeed * Time.deltaTime
        );
    }

}
