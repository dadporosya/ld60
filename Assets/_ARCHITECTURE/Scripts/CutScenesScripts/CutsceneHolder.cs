using UnityEngine;

public class CutsceneHolder : MonoBehaviour
{
    public CutSceneBase cutscene;
    public string path;
    [SerializeField] private bool loadFromResources=true;

    [SerializeField] private int maxPlayCount = -1;
    private int currentPlayCount = 0;
    
    private void Awake()
    {
        if (!cutscene || loadFromResources)
        {
            CutSceneBase loadedCutscene = Resources.Load<CutSceneBase>(path);
            if (loadedCutscene != null)
            {
                cutscene = loadedCutscene;
            }
        }
    }

    public virtual void PlayCutscene()
    {
        if (!cutscene) return;
        if (maxPlayCount > 0 &&  currentPlayCount >= maxPlayCount) return;
        currentPlayCount++;
        
        CutSceneManager.Instance.RunCutscene(cutscene);
    }
}