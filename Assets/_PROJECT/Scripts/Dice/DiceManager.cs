using System;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>How a die is thrown. Shared by all dice; lives on the <see cref="DiceManager"/>.</summary>
[Serializable]
public class DiceRollAnimation
{
    [Min(0.05f)] public float duration = 0.6f;
    [Tooltip("Seconds between one die starting to roll and the next, so they don't move as one.")]
    [Min(0f)] public float stagger = 0.06f;

    [Tooltip("Scale at the top of the throw, relative to rest.")]
    public float liftScale = 1.35f;
    [Tooltip("How far the die rises at the top of the throw (UI units of the canvas). 0: scale only.")]
    public float liftHeight = 0f;
    [Tooltip("Height over the roll (time 0..1): 0 = on the table, 1 = top of the throw.")]
    public AnimationCurve liftCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.4f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3.5f, 0f));

    [Tooltip("Full turns per roll.")]
    [Min(0)] public int spins = 2;
    [Tooltip("Spin progress over the roll (time 0..1 -> 0..1).")]
    public AnimationCurve spinCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));
    [Tooltip("The die lands tilted up to this many degrees either way.")]
    [Min(0f)] public float restTilt = 8f;

    [Tooltip("Seconds per sprite while the die tumbles.")]
    [Min(0.01f)] public float frameInterval = 0.06f;

    [Tooltip("Scale punch on landing. 0: none.")]
    [Min(0f)] public float landPunch = 0.12f;
    [Min(0f)] public float landPunchDuration = 0.2f;
}

/// <summary>How a locked die looks.</summary>
[Serializable]
public class DiceLockStyle
{
    [Tooltip("Multiplied with the die's color while locked.")]
    public Color lockedTint = new Color(0.7f, 0.7f, 0.75f, 1f);
    [Tooltip("Where a locked die moves (UI units of the canvas), e.g. pulled down off the play area.")]
    public Vector2 offset = new Vector2(0f, -14f);
    [Min(0f)] public float duration = 0.12f;
}

/// <summary>Points a poker dice hand is worth.</summary>
[Serializable]
public class CombinationScore
{
    public PokerDiceCombination combination;
    public string displayName;
    public int points;

    public CombinationScore(PokerDiceCombination combination, string displayName, int points)
    {
        this.combination = combination;
        this.displayName = displayName;
        this.points = points;
    }
}

/// <summary>A scored set of dice: the best hand they make, with every card bonus applied.</summary>
public class DiceScore
{
    public PokerDiceCombination combination;
    public string combinationName;
    /// <summary>The hand's points (with the dice sum when it counts, and hand multipliers).</summary>
    public int basePoints;
    /// <summary>Everything the cards added on top, multipliers included.</summary>
    public int bonusPoints;
    public int total;
}

/// <summary>How a dice round ended.</summary>
public class DiceRoundResult
{
    public DiceScore score;
    public int target;
    public bool passed;
    public int[] values;
}

/// <summary>
/// The poker dice table. Owns the dice, the roll animation, lock looks, the scoring table and the
/// table's UI; a <see cref="Dice"/> only animates itself.
///
/// <para>A round (<see cref="StartRound"/>): the first <see cref="Roll"/> throws every die. After it,
/// clicking a die locks / unlocks it, and each further Roll (up to <see cref="rerollsPerRound"/> plus
/// card bonuses) rethrows the unlocked ones. <see cref="Finish"/> - or running out of rerolls - scores
/// the best poker hand the dice make, cards included, against the target.</para>
/// </summary>
public class DiceManager : MonoBehaviour
{
    public static DiceManager Instance;

    [Header("Dice")]
    [SerializeField] private List<Dice> dice = new List<Dice>();
    [Tooltip("Faces 1 to 6, in order.")]
    [SerializeField] private Sprite[] faceSprites = new Sprite[PokerDiceEvaluator.Faces];
    [Tooltip("Frames shown (in random order) while a die tumbles. Empty: random faces.")]
    [SerializeField] private Sprite[] tumbleSprites = new Sprite[0];

    [Header("Animation")]
    public DiceRollAnimation rollAnimation = new DiceRollAnimation();
    public DiceLockStyle lockStyle = new DiceLockStyle();

    [Header("Audio")]
    [SerializeField] private AudioClip rollSfx;
    [SerializeField] private AudioClip landSfx;
    [SerializeField] private AudioClip lockSfx;
    [SerializeField] private AudioClip unlockSfx;
    [SerializeField] private AudioClip passedSfx;
    [SerializeField] private AudioClip failedSfx;

    [Header("Round")]
    [Tooltip("Rerolls after the first throw (cards can add more).")]
    [Min(0)] public int rerollsPerRound = 2;
    [Tooltip("Throw all dice as soon as the round starts, instead of waiting for the Roll button.")]
    [SerializeField] private bool autoFirstRoll = false;
    [Tooltip("Score the dice as soon as the last reroll lands.")]
    [SerializeField] private bool autoFinishWhenOutOfRerolls = true;
    [Tooltip("Seconds the result stays on the table before the round hands back.")]
    [SerializeField, Min(0f)] private float finishDelay = 1.5f;
    [Tooltip("Hide the table root when the round ends.")]
    [SerializeField] private bool hideTableOnFinish = true;

    [Header("Scoring")]
    [Tooltip("Add the sum of all dice to the hand's points.")]
    [SerializeField] private bool addDiceSum = false;
    [SerializeField] private List<CombinationScore> combinations = new List<CombinationScore>
    {
        new CombinationScore(PokerDiceCombination.Nothing, "Nothing", 0),
        new CombinationScore(PokerDiceCombination.OnePair, "Pair", 5),
        new CombinationScore(PokerDiceCombination.TwoPair, "Two Pair", 10),
        new CombinationScore(PokerDiceCombination.ThreeOfAKind, "Three of a Kind", 15),
        new CombinationScore(PokerDiceCombination.SmallStraight, "Small Straight", 20),
        new CombinationScore(PokerDiceCombination.LargeStraight, "Large Straight", 30),
        new CombinationScore(PokerDiceCombination.FullHouse, "Full House", 25),
        new CombinationScore(PokerDiceCombination.FourOfAKind, "Four of a Kind", 40),
        new CombinationScore(PokerDiceCombination.FiveOfAKind, "Five of a Kind", 60),
    };

    [Header("UI")]
    [Tooltip("The table (background, dice, buttons). Shown for a round.")]
    [SerializeField] private GameObject tableRoot;
    [SerializeField] private Button rollButton;
    [SerializeField] private TextMeshProUGUI rollButtonLabel;
    [SerializeField] private string firstRollLabel = "Roll";
    [Tooltip("{0} = rerolls left.")]
    [SerializeField] private string rerollLabel = "Reroll ({0})";
    [SerializeField] private Button finishButton;
    [Tooltip("{0} = rerolls left.")]
    [SerializeField] private TextMeshProUGUI rerollsText;
    [SerializeField] private string rerollsFormat = "Rerolls: {0}";
    [SerializeField] private TextMeshProUGUI targetText;
    [Tooltip("{0} = target score.")]
    [SerializeField] private string targetFormat = "Need {0}";
    [SerializeField] private TextMeshProUGUI scoreText;
    [Tooltip("{0} = total, {1} = hand points, {2} = card bonus.")]
    [SerializeField] private string scoreFormat = "{0}";
    [SerializeField] private TextMeshProUGUI combinationText;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private string passedText = "They still care.";
    [SerializeField] private string failedText = "...";

    [Header("Events")]
    public UnityEvent onRoundStarted = new UnityEvent();
    public UnityEvent onRolled = new UnityEvent();
    /// <summary>The round's total score.</summary>
    public UnityEvent<int> onRoundFinished = new UnityEvent<int>();

    public bool RoundActive { get; private set; }
    public bool HasRolled { get; private set; }
    public int RerollsLeft { get; private set; }
    public int Target { get; private set; }
    public bool IsRolling => landingPending > 0;
    public IReadOnlyList<Dice> AllDice => dice;

    private bool finishing;
    private int landingPending;
    private IReadOnlyList<CardInstance> cards = Array.Empty<CardInstance>();
    private Action<DiceRoundResult> roundCallback;
    private DiceRoundResult pendingResult;
    private Tween finishTween;

    private bool CanLock => RoundActive && HasRolled && !IsRolling && !finishing && RerollsLeft > 0;

    private bool initialized;

    private void Awake() => Initialize();

    /// <summary>
    /// Sets up the dice and buttons. Runs from Awake, or from <see cref="StartRound"/> when this sits on
    /// a table panel that starts hidden (an inactive object gets no Awake until it is shown).
    /// </summary>
    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);

        foreach (Dice die in dice)
        {
            if (!die) continue;
            die.Init(this);
            die.SetFaces(faceSprites);
        }

        if (rollButton) rollButton.onClick.AddListener(Roll);
        if (finishButton) finishButton.onClick.AddListener(Finish);
        if (tableRoot) tableRoot.SetActive(false);
        if (resultText) resultText.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------------
    // Round
    // ---------------------------------------------------------------------

    /// <summary>
    /// Opens the table for a round against <paramref name="target"/> with <paramref name="ownedCards"/>'
    /// bonuses. <paramref name="onFinished"/> gets the result once it has been shown.
    /// </summary>
    public void StartRound(int target, IReadOnlyList<CardInstance> ownedCards, Action<DiceRoundResult> onFinished)
    {
        Initialize();

        if (RoundActive)
        {
            h.Out("DiceManager: a round is already running.");
            return;
        }

        RoundActive = true;
        HasRolled = false;
        finishing = false;
        landingPending = 0;
        Target = target;
        cards = ownedCards ?? Array.Empty<CardInstance>();
        roundCallback = onFinished;
        RerollsLeft = rerollsPerRound + ExtraRerolls(cards);

        foreach (Dice die in dice)
            if (die) die.ResetDice();

        if (tableRoot) tableRoot.SetActive(true);
        if (resultText) resultText.gameObject.SetActive(false);
        UpdateUI();
        onRoundStarted?.Invoke();

        if (autoFirstRoll) Roll();
    }

    /// <summary>First call: throws every die. Later calls: rethrows the unlocked ones, using a reroll.</summary>
    public void Roll()
    {
        if (!RoundActive || finishing || IsRolling) return;

        List<Dice> toRoll = new List<Dice>();
        foreach (Dice die in dice)
            if (die && (!HasRolled || !die.IsLocked)) toRoll.Add(die);

        if (HasRolled)
        {
            if (RerollsLeft <= 0 || toRoll.Count == 0) return;
            RerollsLeft--;
        }

        if (toRoll.Count == 0) return;
        HasRolled = true;
        landingPending = toRoll.Count;

        for (int i = 0; i < toRoll.Count; i++)
        {
            int result = UnityEngine.Random.Range(1, PokerDiceEvaluator.Faces + 1);
            toRoll[i].Roll(result, rollAnimation, faceSprites, tumbleSprites, i * rollAnimation.stagger, OnDieLanded);
        }

        PlaySfx(rollSfx);
        UpdateUI();
    }

    /// <summary>Locks or unlocks <paramref name="die"/>, when locking is allowed right now.</summary>
    public void ToggleLock(Dice die)
    {
        if (!die || !CanLock) return;

        die.SetLocked(!die.IsLocked, lockStyle);
        PlaySfx(die.IsLocked ? lockSfx : unlockSfx);
        UpdateUI();
    }

    /// <summary>Scores the dice and ends the round (after <see cref="finishDelay"/>).</summary>
    public void Finish()
    {
        if (!RoundActive || !HasRolled || IsRolling || finishing) return;
        finishing = true;

        int[] values = Values();
        DiceScore score = Evaluate(values, cards);
        pendingResult = new DiceRoundResult
        {
            score = score,
            target = Target,
            passed = score.total >= Target,
            values = values,
        };

        if (resultText)
        {
            resultText.text = pendingResult.passed ? passedText : failedText;
            resultText.gameObject.SetActive(true);
        }

        PlaySfx(pendingResult.passed ? passedSfx : failedSfx);
        UpdateUI();

        if (finishDelay > 0f) finishTween = Tween.Delay(this, finishDelay, manager => manager.CompleteRound());
        else CompleteRound();
    }

    /// <summary>Drops the round without a result (the song ended mid-round).</summary>
    public void CancelRound()
    {
        if (!RoundActive) return;

        finishTween.Stop();
        foreach (Dice die in dice)
            if (die) die.StopRoll();

        RoundActive = false;
        finishing = false;
        landingPending = 0;
        roundCallback = null;
        pendingResult = null;
        if (tableRoot) tableRoot.SetActive(false);
    }

    private void OnDieLanded(Dice die)
    {
        PlaySfx(landSfx);
        landingPending = Mathf.Max(0, landingPending - 1);
        if (landingPending > 0) return;

        UpdateUI();
        onRolled?.Invoke();

        if (autoFinishWhenOutOfRerolls && RerollsLeft <= 0) Finish();
    }

    private void CompleteRound()
    {
        DiceRoundResult result = pendingResult;
        Action<DiceRoundResult> callback = roundCallback;

        pendingResult = null;
        roundCallback = null;
        RoundActive = false;
        finishing = false;
        if (hideTableOnFinish && tableRoot) tableRoot.SetActive(false);

        onRoundFinished?.Invoke(result.score.total);
        callback?.Invoke(result);
    }

    // ---------------------------------------------------------------------
    // Scoring
    // ---------------------------------------------------------------------

    public int[] Values()
    {
        List<int> values = new List<int>();
        foreach (Dice die in dice)
            if (die) values.Add(die.Value);
        return values.ToArray();
    }

    /// <summary>Points for a combination in the scoring table (0 when it isn't listed).</summary>
    public CombinationScore ScoreFor(PokerDiceCombination combination)
    {
        foreach (CombinationScore entry in combinations)
            if (entry != null && entry.combination == combination) return entry;
        return null;
    }

    /// <summary>
    /// The best score <paramref name="values"/> can make: every hand they contain is scored with the
    /// cards' bonuses and the highest total wins. Order: hand points (+ dice sum) x hand multipliers,
    /// + flat / hand / face bonuses, x score multipliers.
    /// </summary>
    public DiceScore Evaluate(IReadOnlyList<int> values, IReadOnlyList<CardInstance> ownedCards)
    {
        int diceSum = 0;
        foreach (int v in values) diceSum += v;

        DiceScore best = null;
        foreach (PokerDiceCombination hand in PokerDiceEvaluator.Contained(values))
        {
            CombinationScore entry = ScoreFor(hand);
            if (entry == null) continue;

            float handPoints = entry.points + (addDiceSum ? diceSum : 0);
            float bonus = 0f;
            float scoreMultiplier = 1f;

            if (ownedCards != null)
                foreach (CardInstance card in ownedCards)
                {
                    CardEffect effect = card?.Effect;
                    if (effect == null) continue;

                    switch (effect.type)
                    {
                        case CardEffectType.FlatPoints:
                            bonus += effect.amount;
                            break;
                        case CardEffectType.CombinationBonus:
                            if (effect.Matches(hand)) bonus += effect.amount;
                            break;
                        case CardEffectType.CombinationMultiplier:
                            if (effect.Matches(hand)) handPoints *= effect.multiplier;
                            break;
                        case CardEffectType.FaceValueBonus:
                            foreach (int v in values)
                                if (v == effect.faceValue) bonus += effect.amount;
                            break;
                        case CardEffectType.ScoreMultiplier:
                            scoreMultiplier *= effect.multiplier;
                            break;
                    }
                }

            int basePoints = Mathf.RoundToInt(handPoints);
            int total = Mathf.RoundToInt((handPoints + bonus) * scoreMultiplier);

            if (best != null && (total < best.total || (total == best.total && hand < best.combination))) continue;

            best = new DiceScore
            {
                combination = hand,
                combinationName = string.IsNullOrEmpty(entry.displayName) ? CardEffect.Nicify(hand) : entry.displayName,
                basePoints = basePoints,
                bonusPoints = total - basePoints,
                total = total,
            };
        }

        return best ?? new DiceScore { combination = PokerDiceCombination.Nothing, combinationName = "Nothing" };
    }

    private static int ExtraRerolls(IReadOnlyList<CardInstance> ownedCards)
    {
        int extra = 0;
        foreach (CardInstance card in ownedCards)
        {
            CardEffect effect = card?.Effect;
            if (effect != null && effect.type == CardEffectType.ExtraRerolls) extra += effect.amount;
        }
        return Mathf.Max(0, extra);
    }

    // ---------------------------------------------------------------------
    // UI / audio
    // ---------------------------------------------------------------------

    private void UpdateUI()
    {
        bool anyUnlocked = false;
        foreach (Dice die in dice)
            if (die && !die.IsLocked) anyUnlocked = true;

        bool idle = RoundActive && !finishing && !IsRolling;

        if (rollButton) rollButton.interactable = idle && (!HasRolled || (RerollsLeft > 0 && anyUnlocked));
        if (rollButtonLabel) rollButtonLabel.text = HasRolled ? string.Format(rerollLabel, RerollsLeft) : firstRollLabel;
        if (finishButton) finishButton.interactable = idle && HasRolled;
        if (rerollsText) rerollsText.text = string.Format(rerollsFormat, RerollsLeft);
        if (targetText) targetText.text = string.Format(targetFormat, Target);

        bool showScore = RoundActive && HasRolled && !IsRolling;
        DiceScore score = showScore ? Evaluate(Values(), cards) : null;
        if (scoreText) scoreText.text = score != null ? string.Format(scoreFormat, score.total, score.basePoints, score.bonusPoints) : "";
        if (combinationText) combinationText.text = score != null ? score.combinationName : "";
    }

    private static void PlaySfx(AudioClip clip)
    {
        if (clip && SFXManager.Instance) SFXManager.Instance.PlayClip(clip);
    }
}
