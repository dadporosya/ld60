using System.Collections;
using UnityEngine;

public class EatingMatController : MonoBehaviour
{
    [SerializeField] private int frameCount;

    private Material _mat;

    private void Awake()
    {
        var rend = GetComponent<Renderer>();
        if (rend != null)
        {
            //TODO
            // _mat = new Material(R.Materials.EatingMat);
            rend.material = _mat;
        }
    }

    public void TakeBite(bool inverse = false)
    {
        if (_mat == null) return;
        int frame = Mathf.RoundToInt(_mat.GetFloat("_frame"));
        frame += inverse ? -1 : 1;
        frame = Mathf.Clamp(frame, 0, frameCount);
        _mat.SetFloat("_frame", frame);
    }

    public IEnumerator EatingAnimation(float duration, bool inverse)
    {
        if (_mat == null) yield break;

        float gap = frameCount > 0 ? duration / frameCount : duration;
        int target = inverse ? 0 : frameCount;
        int step = inverse ? -1 : 1;

        int current = Mathf.RoundToInt(_mat.GetFloat("_frame"));
        while (current != target)
        {
            current = Mathf.Clamp(current + step, 0, frameCount);
            _mat.SetFloat("_frame", current);
            yield return new WaitForSeconds(gap);
        }
    }
}
