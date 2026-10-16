using UnityEngine;
using UnityEngine.Events;

public class SmartCollider : MonoBehaviour
{
    public UnityEvent<Collider2D> onTriggerEnterEvent = new();
    public UnityEvent<Collider2D> onTriggerStayEvent = new();
    public UnityEvent<Collider2D> onTriggerExitEvent = new();

    public Collider2D col;//
    public float colliderScale = 1f;
    private Vector3 originalScale;

    private void Awake()
    {
        originalScale = transform.localScale;
    }

    private void Start()
    {
        SetScale(colliderScale);
        
        col = GetComponent<Collider2D>();
        if (col) col.isTrigger = true;

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        onTriggerEnterEvent?.Invoke(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        onTriggerStayEvent?.Invoke(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        onTriggerExitEvent?.Invoke(other);
    }
    
    public void SetScale(float scale)
    {
        transform.localScale = originalScale * scale;
    }
}
