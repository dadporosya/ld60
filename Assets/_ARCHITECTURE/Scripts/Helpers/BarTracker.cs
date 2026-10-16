using UnityEngine;

public class BarTracker : MonoBehaviour, IOnMove
{
    [SerializeField] private GameObject tracer;

    private void Start()
    {
        EndMove();
    }
    
    public void StartMove()
    {
        tracer.SetActive(true);
    }

    public void OnMove(){}

    public void EndMove()
    {
        tracer.SetActive(false);
    }
}
