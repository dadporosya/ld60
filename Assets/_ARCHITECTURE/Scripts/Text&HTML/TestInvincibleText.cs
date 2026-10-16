using TMPro;
using UnityEngine;

public class InvisibleText : MonoBehaviour
{
    public TextMeshProUGUI textComp;

    public void Start()
    {
        textComp = GetComponent<TextMeshProUGUI>();
        HideAllCharacters(textComp);
    }

    // ─────────────────────────────────────────────────────────────
    //  Hide all
    // ─────────────────────────────────────────────────────────────

    public static void HideAllCharacters(TextMeshProUGUI tmp)
    {
        var anim = tmp.GetComponent<TextAnimation>();
        if (anim != null)
        {
            tmp.ForceMeshUpdate();
            anim.ClearAllAlphaOverrides();
            for (int i = 0; i < tmp.textInfo.characterCount; i++)
                anim.SetCharacterAlpha(i, 0);
            return;
        }

        // Fallback: no TextAnimation present, write directly.
        tmp.ForceMeshUpdate();
        HideAllCharactersProcess(tmp.textInfo);
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    public static void HideAllCharacters(TMP_Text tmp)
    {
        var anim = tmp.GetComponent<TextAnimation>();
        if (anim != null)
        {
            tmp.ForceMeshUpdate();
            anim.ClearAllAlphaOverrides();
            for (int i = 0; i < tmp.textInfo.characterCount; i++)
                anim.SetCharacterAlpha(i, 0);
            return;
        }

        // Fallback: no TextAnimation present, write directly.
        tmp.ForceMeshUpdate();
        HideAllCharactersProcess(tmp.textInfo);
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    // ─────────────────────────────────────────────────────────────
    //  Show all
    // ─────────────────────────────────────────────────────────────

    public static void ShowAllCharacters(TextMeshProUGUI tmp)
    {
        var anim = tmp.GetComponent<TextAnimation>();
        if (anim != null)
        {
            anim.ClearAllAlphaOverrides();
            return;
        }

        tmp.ForceMeshUpdate();
        ShowAllCharactersProcess(tmp.textInfo);
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    public static void ShowAllCharacters(TMP_Text tmp)
    {
        var anim = tmp.GetComponent<TextAnimation>();
        if (anim != null)
        {
            anim.ClearAllAlphaOverrides();
            return;
        }

        tmp.ForceMeshUpdate();
        ShowAllCharactersProcess(tmp.textInfo);
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    // ─────────────────────────────────────────────────────────────
    //  Show / hide by character index
    // ─────────────────────────────────────────────────────────────

    public static void ShowCharacterById(TMP_Text tmp, int id)
    {
        var anim = tmp.GetComponent<TextAnimation>();
        if (anim != null)
        {
            anim.ClearCharacterAlpha(id); // removes override → animation (or base) controls alpha
            return;
        }

        SetCharacterAlpha(tmp.textInfo, id, 255);
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    public static void HideCharacterById(TMP_Text tmp, int id)
    {
        var anim = tmp.GetComponent<TextAnimation>();
        if (anim != null)
        {
            anim.SetCharacterAlpha(id, 0);
            return;
        }

        SetCharacterAlpha(tmp.textInfo, id, 0);
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    public static void ShowCharacterById(TextMeshProUGUI tmp, int id)
    {
        var anim = tmp.GetComponent<TextAnimation>();
        if (anim != null)
        {
            anim.ClearCharacterAlpha(id);
            return;
        }

        SetCharacterAlpha(tmp.textInfo, id, 255);
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    public static void HideCharacterById(TextMeshProUGUI tmp, int id)
    {
        var anim = tmp.GetComponent<TextAnimation>();
        if (anim != null)
        {
            anim.SetCharacterAlpha(id, 0);
            return;
        }

        SetCharacterAlpha(tmp.textInfo, id, 0);
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    // ─────────────────────────────────────────────────────────────
    //  Direct mesh helpers (used when no TextAnimation is present)
    // ─────────────────────────────────────────────────────────────

    public static void HideAllCharactersProcess(TMP_TextInfo info)
    {
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo c = info.characterInfo[i];
            if (!c.isVisible) continue;

            Color32[] colors = info.meshInfo[c.materialReferenceIndex].colors32;
            int vi = c.vertexIndex;
            colors[vi].a = colors[vi + 1].a = colors[vi + 2].a = colors[vi + 3].a = 0;
        }
    }

    public static void ShowAllCharactersProcess(TMP_TextInfo info)
    {
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo c = info.characterInfo[i];
            if (!c.isVisible) continue;

            Color32[] colors = info.meshInfo[c.materialReferenceIndex].colors32;
            int vi = c.vertexIndex;
            colors[vi].a = colors[vi + 1].a = colors[vi + 2].a = colors[vi + 3].a = 255;
        }
    }

    private static void SetCharacterAlpha(TMP_TextInfo info, int id, byte alpha)
    {
        if (id < 0 || id >= info.characterCount) return;

        TMP_CharacterInfo c = info.characterInfo[id];
        if (!c.isVisible) return;

        Color32[] colors = info.meshInfo[c.materialReferenceIndex].colors32;
        int vi = c.vertexIndex;
        colors[vi].a = colors[vi + 1].a = colors[vi + 2].a = colors[vi + 3].a = alpha;
    }
}