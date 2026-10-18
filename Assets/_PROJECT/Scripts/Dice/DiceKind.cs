using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a die does. Plain dice are <see cref="Default"/>; the others are won in the dice choice after a card pick
/// (<see cref="CardManager.ChooseDice"/>) and replace a random plain die. Values are serialized: append new ones at
/// the end.
/// </summary>
public enum DiceKind
{
    [Tooltip("A plain die: any face.")]
    Default = 0,
    [Tooltip("Always lands on an odd face.")]
    Blue = 1,
    [Tooltip("Always lands on an even face.")]
    Yellow = 2,
    [Tooltip("Never lands on 1, 2 or 3.")]
    Pink = 3,
    [Tooltip("Lands on the face most of the other dice show (the highest of a tie).")]
    Green = 4,
    [Tooltip("Right click to turn it to 1, whenever the dice are down.")]
    Cyan = 5,
    [Tooltip("Lands on a face no other die shows.")]
    Red = 6,
}

/// <summary>The faces each <see cref="DiceKind"/> may land on.</summary>
public static class DiceKinds
{
    /// <summary>Whether a die of <paramref name="kind"/> may show <paramref name="face"/>.</summary>
    public static bool Allows(DiceKind kind, int face)
    {
        switch (kind)
        {
            case DiceKind.Blue: return face % 2 == 1;
            case DiceKind.Yellow: return face % 2 == 0;
            case DiceKind.Pink: return face >= 4;
            default: return face >= 1 && face <= PokerDiceEvaluator.Faces;
        }
    }

    /// <summary>A random face <paramref name="kind"/> allows.</summary>
    public static int RandomFace(DiceKind kind)
    {
        List<int> faces = new List<int>(PokerDiceEvaluator.Faces);
        for (int face = 1; face <= PokerDiceEvaluator.Faces; face++)
            if (Allows(kind, face)) faces.Add(face);
        return faces.Count > 0 ? faces[UnityEngine.Random.Range(0, faces.Count)] : UnityEngine.Random.Range(1, PokerDiceEvaluator.Faces + 1);
    }

    /// <summary>The face <paramref name="values"/> show most often, the highest of a tie. 0 when there are none.</summary>
    public static int MostCommon(IReadOnlyList<int> values)
    {
        int[] counts = new int[PokerDiceEvaluator.Faces + 1];
        foreach (int v in values)
            if (v >= 1 && v <= PokerDiceEvaluator.Faces) counts[v]++;

        int best = 0;
        for (int face = PokerDiceEvaluator.Faces; face >= 1; face--)
            if (counts[face] > counts[best]) best = face;
        return best;
    }

    /// <summary>A random face none of <paramref name="values"/> show. 0 when they show every face.</summary>
    public static int Missing(IReadOnlyList<int> values)
    {
        List<int> faces = new List<int>(PokerDiceEvaluator.Faces);
        for (int face = 1; face <= PokerDiceEvaluator.Faces; face++)
        {
            bool shown = false;
            foreach (int v in values)
                if (v == face) shown = true;
            if (!shown) faces.Add(face);
        }
        return faces.Count > 0 ? faces[UnityEngine.Random.Range(0, faces.Count)] : 0;
    }
}

/// <summary>A die the dice choice can offer: its kind, its card and the tint it has on the table.</summary>
[Serializable]
public class DiceKindOffer
{
    [Tooltip("Off: never offered.")]
    public bool enabled = true;
    public DiceKind kind;
    public string title;
    [TextArea] public string description;
    [Tooltip("The die's color: on the table (multiplied with the die's own) and on its card's icon.")]
    public Color tint = Color.white;
    [Tooltip("The card's icon: this sprite of the dice sprite asset (\"1\"..\"6\", \"x\", \"y\", \"odd\", \"even\"), tinted.")]
    public string iconSprite = "x";
    [Tooltip("The more, the likelier it is offered.")]
    [Min(1)] public int weight = 1;

    // Unity builds list entries through this one: without it the field initializers (enabled = true) are skipped.
    public DiceKindOffer() { }

    public DiceKindOffer(DiceKind kind, string title, string description, Color tint, string iconSprite)
    {
        this.kind = kind;
        this.title = title;
        this.description = description;
        this.tint = tint;
        this.iconSprite = iconSprite;
    }

    /// <summary>The card it shows as in the choice.</summary>
    public CardFace Face => new CardFace(title, description, DiceIcons.Tinted(iconSprite, tint));

    public static List<DiceKindOffer> Defaults() => new List<DiceKindOffer>
    {
        new DiceKindOffer(DiceKind.Blue, "Blue Die", "Always rolls odd.", new Color(0.35f, 0.55f, 1f, 1f), "odd"),
        new DiceKindOffer(DiceKind.Yellow, "Yellow Die", "Always rolls even.", new Color(1f, 0.88f, 0.3f, 1f), "even"),
        new DiceKindOffer(DiceKind.Pink, "Pink Die", "Never rolls 1, 2 or 3.", new Color(1f, 0.55f, 0.8f, 1f), "6"),
        new DiceKindOffer(DiceKind.Green, "Green Die", "Rolls the value most of your dice show.", new Color(0.4f, 0.9f, 0.4f, 1f), "x"),
        new DiceKindOffer(DiceKind.Cyan, "Cyan Die", "Right click to turn it to 1, any time.", new Color(0.35f, 0.95f, 0.95f, 1f), "1"),
        new DiceKindOffer(DiceKind.Red, "Red Die", "Always rolls a value no other die shows.", new Color(1f, 0.35f, 0.35f, 1f), "y"),
    };
}
