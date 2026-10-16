using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
// HTMl and codes
public static class HTML
{
    public const string ALPHA = "<color=#00000000>";
    public const string DIALOGUE_PAUSE_TAG = "pause";
    public const string DIALOGUE_STOP_TAG = "stop";
    public const string ANIMATION_TAG = "anim:";

    public static readonly List<string> ANIMATION_TYPES = new List<string>()
    {
        "shake", "wave", "bounce", "fade", "rainbow", "none"
    };

    public static List<string> ALL_UNIQUE_TAGS = new List<string>()
    {
        DIALOGUE_PAUSE_TAG,
        DIALOGUE_STOP_TAG
    };

    public static string CreatePauseTag(float pauseDuration)
    {
        return "<" + DIALOGUE_PAUSE_TAG + h.Str(pauseDuration) + ">";
    }

    public static string CreateStopTag()
    {
        return "<" + DIALOGUE_STOP_TAG + ">";
    }

    public static string RemoveAllTags(string input)
    {
        return Regex.Replace(input, @"<[^>]*>", "");
    }
    
    public static string RemoveUniqueTags(string input)
    {
        return RemoveTags(input, ALL_UNIQUE_TAGS);
    }

    public static Dictionary<P.StatusEffects, string> StatEffectIconsTags = new Dictionary<P.StatusEffects, string>()
    {
        { P.StatusEffects.Ignition, "<sprite=\"StatusEffectIcons\" name=\"ignition\">" },
        { P.StatusEffects.Stun, "<sprite=\"StatusEffectIcons\" name=\"stun\">" },
        { P.StatusEffects.Shields, "<sprite=\"StatusEffectIcons\" name=\"shields\">" },
    };

    
    public static string RemoveTags(string input,  List<string> targetTags=null, string targetTag="")
    {
        if (targetTags == null) targetTags = new List<string>();
        if (targetTag != "")  targetTags.Add(targetTag);
        string result = input;
        foreach (string tag in targetTags)
        {
            result = Regex.Replace(result, @"<" + tag + @"[^>]*>", "");
        }
        return result;
    }

    public static bool SpriteExistsInTmpAssets(string spriteName, TMP_SpriteAsset tmpAsset = null)
    {
        if (tmpAsset == null) tmpAsset = TMP_Settings.defaultSpriteAsset;
        
        if (tmpAsset != null && tmpAsset.GetSpriteIndexFromName(spriteName) != -1)
        {
            return true;
        }

        return false;
    }

    public static string GenerateSpriteTag(string spriteName, TMP_SpriteAsset tmpAsset = null)
    {
        if (SpriteExistsInTmpAssets(spriteName, tmpAsset:tmpAsset))
        {
            return $"<sprite name={spriteName}>";
        }

        return "";
    }

    public static string GenerateSpriteWithString(string text, string spriteName="", TMP_SpriteAsset tmpAsset = null)
    {
        if (spriteName == "") spriteName = text;
        return GenerateSpriteTag(spriteName, tmpAsset) + " " +  text;
    }
    
    

}
