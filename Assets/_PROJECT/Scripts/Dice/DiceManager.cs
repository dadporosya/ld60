using System;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
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

    [Tooltip("How far a rolled die travels, in a random direction (UI units). It always lands inside " +
             "the Dice Parent's rect, clear of the other dice when there's room. Max 0: dice don't travel.")]
    public Vector2 travelDistance = new Vector2(40f, 160f);
    [Tooltip("Travel progress over the roll (time 0..1 -> 0..1).")]
    public AnimationCurve travelCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));

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

/// <summary>The outline a die gets while hovered.</summary>
[Serializable]
public class DiceOutlineStyle
{
    public bool enabled = true;
    public Color color = new Color(1f, 0.95f, 0.75f, 1f);
    [Tooltip("Outline thickness (UI units). Unity's UI Outline traces the sprite by offsetting it; keep it small (2-6).")]
    public Vector2 width = new Vector2(4f, 4f);
    [Min(0f)] public float fadeDuration = 0.1f;
    [Tooltip("Only while clicking the die would lock / unlock it (not mid-roll, not before the first roll, not out of rerolls).")]
    public bool onlyWhenLockable = true;
    [Tooltip("Always shown on a sealed die - one locked before the last roll, which can't be touched any more this " +
             "round. Shown even with the hover outline off.")]
    public Color sealedColor = new Color(0.95f, 0.2f, 0.2f, 1f);
}

/// <summary>
/// Text popups over the table (through the <see cref="TextAlertManager"/>): the score's change, the hand the
/// dice made, and the goals being reached.
/// </summary>
[Serializable]
public class DicePopups
{
    public bool enabled = true;
    [Tooltip("Seconds between popups that come at once (a landing that reaches a goal), so they don't pile up.")]
    [Min(0f)] public float interval = 0.25f;

    [Header("Where (empty: the middle of the dice parent)")]
    public Transform scoreAnchor;
    public Transform combinationAnchor;
    public Transform goalAnchor;
    [Tooltip("Added to the anchor, in canvas units, so popups that come together don't stack on one spot.")]
    public Vector2 scoreOffset = new Vector2(0f, 70f);
    public Vector2 combinationOffset = Vector2.zero;
    public Vector2 goalOffset = new Vector2(0f, -70f);

    [Header("Text")]
    [Tooltip("{0} = points gained.")]
    public string gainFormat = "+{0}";
    [Tooltip("{0} = points lost (a positive number).")]
    public string lossFormat = "-{0}";
    public string goalReachedText = "Goal reached!";
    public string riskyGoalReachedText = "You feel hopeful.";
    [Tooltip("At the end of a round that reached the risky goal: the next cards are upgraded.")]
    public string cardUpgradedText = "Card upgraded!";
    [Tooltip("A reroll whose rolled dice make no combination: the round's points are lost and it ends. Empty: none.")]
    public string bustText = "Bust!";
    [Tooltip("A die destroyed for rerolls (a card's right click). {0} = rerolls gained.")]
    public string rerollsGainedFormat = "+{0} reroll";
    [Tooltip("A die destroyed for mult (a card's right click). {0} = mult gained.")]
    public string multGainedFormat = "+{0} mult";
    [Tooltip("A round scored (last reroll landed or Finish pressed), shown before the next round starts. " +
             "{0} = the round's score, {1} = mult, {2} = points. Empty: none.")]
    public string roundScoreFormat = "Round Score: {0}";
    [Tooltip("The turn's next round starting. {0} = the round, {1} = rounds per turn. Empty: none.")]
    public string roundStartFormat = "Round {0}";

    [Header("Round Popups")]
    [Tooltip("Size of the Round Score popup, relative to the other popups.")]
    [Min(0f)] public float roundScoreScale = 1.6f;
    [Tooltip("Seconds the Round Score popup stays up before fading, on top of the usual hold.")]
    [Min(0f)] public float roundScoreExtraHold = 0.5f;

    [Header("Risky Goal Popup")]
    [Tooltip("Size of the risky goal popup, relative to the other popups.")]
    [Min(0f)] public float riskyGoalScale = 1.15f;
    [Tooltip("Seconds the risky goal popup stays up before fading, on top of the usual hold.")]
    [Min(0f)] public float riskyGoalExtraHold = 1f;

    [Header("Colors")]
    public Color gainColor = new Color(0.45f, 0.9f, 0.45f, 1f);
    public Color lossColor = new Color(0.95f, 0.4f, 0.4f, 1f);
    public Color combinationColor = Color.white;
    public Color goalColor = new Color(1f, 0.9f, 0.45f, 1f);
    public Color riskyGoalColor = new Color(0.45f, 0.9f, 0.45f, 1f);
    public Color roundScoreColor = new Color(1f, 0.9f, 0.45f, 1f);
    public Color roundStartColor = Color.white;
}

/// <summary>How a dice turn came out: under the target, the target, or the risky goal.</summary>
public enum DiceOutcome
{
    Lost,
    Passed,
    Hope,
}

/// <summary>What the outcome spotlight shows for one <see cref="DiceOutcome"/>.</summary>
[Serializable]
public class DiceOutcomeLook
{
    public string text;
    public Color color = Color.white;
    [Tooltip("Played as the text appears. Empty: silent.")]
    public AudioClip sfx;
    [Range(0f, 1f)] public float volume = 1f;

    public DiceOutcomeLook() { }

    public DiceOutcomeLook(string text, Color color)
    {
        this.text = text;
        this.color = color;
    }
}

/// <summary>
/// The big cinematic frames at the end of a turn. The turn ends -> <see cref="delay"/> -> the outcome frame
/// (LOST / PASSED / HOPE) cuts in with its sfx -> <see cref="hold"/> -> the title frame (the level's title, e.g.
/// MOTHER) cuts in -> <see cref="titleHold"/> -> the turn hands back (the round end's fade to black and the outcome
/// dialogue follow, see <see cref="FuneralGameManager"/>).
/// </summary>
[Serializable]
public class DiceOutcomeSpotlight
{
    [Tooltip("The outcome frame: the text and its background, shown and hidden as one. Keep it out of the Table " +
             "Root and above the FadeImageController's image to keep it readable over the black. Empty: the " +
             "Text's own object.")]
    public GameObject root;
    [Tooltip("The big outcome text. Empty: no spotlight - the turn hands back after Finish Delay, as before.")]
    public TextMeshProUGUI text;
    [Tooltip("Seconds after the turn's result before the outcome frame appears.")]
    [Min(0f)] public float delay = 0.4f;
    [Tooltip("Seconds the outcome frame stays up before the title frame (or, without one, the turn hands back).")]
    [Min(0f)] public float hold = 1.2f;
    [Tooltip("Seconds the frame fades out over once it's hidden (the black has covered the screen). 0: off at once.")]
    [Min(0f)] public float fadeOut = 0.3f;

    public DiceOutcomeLook lost = new DiceOutcomeLook("LOST", new Color(0.9f, 0.15f, 0.15f, 1f));
    public DiceOutcomeLook passed = new DiceOutcomeLook("PASSED", Color.white);
    public DiceOutcomeLook hope = new DiceOutcomeLook("HOPE", new Color(0.35f, 0.9f, 0.4f, 1f));

    [Header("Title frame")]
    [Tooltip("The second frame: the level's title and its background, shown and hidden as one. Same placement " +
             "rules as Root. Empty: the Title Text's own object.")]
    public GameObject titleRoot;
    [Tooltip("The level's title (Level's Spotlight Title, else its Name). Empty: no title frame.")]
    public TextMeshProUGUI titleText;
    [Tooltip("Seconds the title frame stays up before the turn hands back (and the fade to black starts).")]
    [Min(0f)] public float titleHold = 1.2f;
    [Tooltip("Played as the title frame cuts in. Empty: silent.")]
    public AudioClip titleSfx;
    [Range(0f, 1f)] public float titleVolume = 1f;

    public GameObject Root => root ? root : text ? text.gameObject : null;
    public GameObject TitleRoot => titleRoot ? titleRoot : titleText ? titleText.gameObject : null;

    public DiceOutcomeLook For(DiceOutcome outcome) => outcome switch
    {
        DiceOutcome.Hope => hope,
        DiceOutcome.Passed => passed,
        _ => lost,
    };
}

/// <summary>Points a poker dice hand is worth.</summary>
[Serializable]
public class CombinationScore
{
    [Tooltip("Off: the hand doesn't count - dice making it score as the next best hand they make, and the hint paper leaves it out.")]
    public bool enabled = true;
    [Tooltip("Off: the hand still shows its points while rolling, but a round ending with it as the best hand " +
             "scores 0 - and it doesn't count as a combination for the first throw's odds.")]
    public bool countsAsCombination = true;
    public PokerDiceCombination combination;
    public string displayName;
    public int points;

    // Unity builds list entries through this one: without it the field initializers (enabled = true) are skipped.
    public CombinationScore() { }

    public CombinationScore(PokerDiceCombination combination, string displayName, int points)
    {
        this.combination = combination;
        this.displayName = displayName;
        this.points = points;
    }
}

/// <summary>
/// A scored set of dice: the best hand they make, with every card bonus applied. The score is
/// <see cref="points"/> x <see cref="mult"/>, as in Balatro.
/// </summary>
public class DiceScore
{
    public PokerDiceCombination combination;
    public string combinationName;
    /// <summary>The hand's points + the dice sum (when it counts) + card points, card point multipliers applied.</summary>
    public int points;
    /// <summary>1 + card mult (and mult gained this round), card mult multipliers applied.</summary>
    public float mult = 1f;
    /// <summary><see cref="points"/> x <see cref="mult"/>, rounded.</summary>
    public int total;
    /// <summary>Rerolls the cards that counted add to the next round, once this one is finished.</summary>
    public int nextRoundRerolls;
}

/// <summary>How one round (one dice game) of a turn ended.</summary>
public class DiceRoundResult
{
    public DiceScore score;
    public int[] values;
}

/// <summary>How a dice turn ended: every round's points added up, against the target.</summary>
public class DiceTurnResult
{
    public List<DiceRoundResult> rounds = new List<DiceRoundResult>();
    /// <summary>The points of all rounds added up.</summary>
    public int total;
    public int target;
    public bool passed;
    /// <summary>The risky goal: <see cref="target"/> x the risky multiplier.</summary>
    public int riskyTarget;
    /// <summary>The total reached the risky goal: the next cards are upgraded.</summary>
    public bool riskyPassed;

    public DiceOutcome Outcome => !passed ? DiceOutcome.Lost : riskyPassed ? DiceOutcome.Hope : DiceOutcome.Passed;
}

/// <summary>
/// The poker dice table. Owns the dice, the roll animation, lock looks, the scoring table and the
/// table's UI; a <see cref="Dice"/> only animates itself.
///
/// <para>A turn (<see cref="StartTurn"/>) is <see cref="roundsPerTurn"/> rounds. A round is one dice game:
/// the first <see cref="Roll"/> throws every die. After it, clicking a die locks / unlocks it, and each
/// further Roll (as many as the player likes with <see cref="unlimitedRerolls"/>, else up to
/// <see cref="rerollsPerRound"/> plus card bonuses) rethrows the unlocked ones. A roll seals the dice locked for it:
/// they stay locked for the rest of the round (red outline). A reroll whose rolled dice make no combination by
/// themselves busts: the round scores 0 and ends at once - unless they show a 1 (Single One). <see cref="Finish"/> - or running out of rerolls - scores
/// the best poker hand the dice make, cards included, and banks it. After the last round the banked points are
/// checked against the target.</para>
///
/// <para>Cards can add dice to the table (<see cref="CardEffectType.ExtraDice"/>) and let the player right click
/// dice showing certain faces (<see cref="CardEffect.IsDieAction"/>): reroll it alone for free, destroy it for the
/// rest of the round for rerolls, or flip it to another face. Destroyed dice don't score.</para>
///
/// <para>Two goals: the target (the minimum to pass) and the risky goal, <see cref="riskyGoalMultiplier"/>
/// times higher - reaching it upgrades the next cards offered (<see cref="DiceTurnResult.riskyPassed"/>).</para>
/// </summary>
public class DiceManager : MonoBehaviour
{
    public static DiceManager Instance;

    [Header("Dice")]
    [Tooltip("How many dice the table has. Spawned from Dice Prefab at scene start; dice already in the " +
             "list below count towards it. Extra Dice cards spawn more for the turns they're owned in.")]
    [SerializeField, Min(1)] private int startDiceCount = 6;
    [Tooltip("Spawned to make up Start Dice Count. A prefab asset, or a template die in the scene (hidden, " +
             "never rolled).")]
    [SerializeField] private Dice dicePrefab;
    [Tooltip("The dice area: spawned dice go here, are scattered across it, and rolled dice only ever land " +
             "inside its rect. No layout group on it - the dice move freely. Empty: the template's parent, " +
             "else the table root.")]
    [SerializeField] private RectTransform diceParent;
    [Tooltip("Extra gap kept between dice when they land (UI units).")]
    [SerializeField, Min(0f)] private float diceSpacing = 8f;
    [Tooltip("Dice placed by hand. Spawned dice are added here at runtime.")]
    [SerializeField] private List<Dice> dice = new List<Dice>();
    [Tooltip("Faces 1 to 6, in order.")]
    [SerializeField] private Sprite[] faceSprites = new Sprite[PokerDiceEvaluator.Faces];
    [Tooltip("Frames shown (in random order) while a die tumbles. Empty: random faces.")]
    [SerializeField] private Sprite[] tumbleSprites = new Sprite[0];

    [Header("Animation")]
    public DiceRollAnimation rollAnimation = new DiceRollAnimation();
    public DiceLockStyle lockStyle = new DiceLockStyle();
    [Tooltip("Outline a die gets while the mouse is over it.")]
    public DiceOutlineStyle hoverOutline = new DiceOutlineStyle();

    [Header("Audio")]
    [Tooltip("Every dice sound plays at a random pitch in this range (x = min, y = max). 1 to 1: no randomization.")]
    [SerializeField] private Vector2 sfxPitchRange = new Vector2(0.94f, 1.06f);
    [SerializeField] private AudioClip rollSfx;
    [SerializeField] private AudioClip landSfx;
    [SerializeField] private AudioClip lockSfx;
    [SerializeField] private AudioClip unlockSfx;
    [Tooltip("A die flipped to another face (a card's right click).")]
    [SerializeField] private AudioClip flipSfx;
    [Tooltip("A die destroyed (a card's right click).")]
    [SerializeField] private AudioClip destroySfx;
    [Tooltip("A reroll made no combination: the round's points are lost.")]
    [SerializeField] private AudioClip bustSfx;
    [SerializeField] private AudioClip passedSfx;
    [SerializeField] private AudioClip failedSfx;
    [Tooltip("Played as a good text alert pops up: points gained, a combination, a goal reached, a card upgraded, " +
             "rerolls gained.")]
    [SerializeField] private AudioClip goodAlertSfx;
    [Tooltip("Played as a bad text alert pops up: points lost.")]
    [SerializeField] private AudioClip badAlertSfx;

    [Header("Turn")]
    [Tooltip("Dice games per turn. Each round gets its own rerolls; the points of every round add up, and " +
             "the total is checked against the target after the last one.")]
    [Min(1)] public int roundsPerTurn = 1;

    [Header("Round")]
    [Tooltip("Reroll as often as the player likes, until Finish (or a bust). Off: Rerolls Per Round (plus cards) only.")]
    [SerializeField] private bool unlimitedRerolls = true;
    [Tooltip("A reroll whose rolled dice (the locked ones left out) make no combination busts: the round scores 0 " +
             "and ends at once. Rolling a 1 (Single One, when enabled) saves it.")]
    [SerializeField] private bool bustWithoutCombination = true;
    [Tooltip("The round's first throw (every die) can bust too.")]
    [SerializeField] private bool bustOnFirstThrow = false;
    [Tooltip("Rerolls after the first throw (cards can add more). Unused with Unlimited Rerolls.")]
    [Min(0)] public int rerollsPerRound = 2;
    [Tooltip("Throw all dice as soon as the round starts, instead of waiting for the Roll button.")]
    [SerializeField] private bool autoFirstRoll = false;
    [Tooltip("Seconds the table waits after opening (and after the tutorial) before the first throw - the Roll " +
             "button stays off meanwhile.")]
    [SerializeField, Min(0f)] private float firstRollDelay = 1f;
    [Tooltip("How much rarer any combination (a hand with Counts As Combination on) is on a round's first throw: " +
             "0.3 = 30% less likely. Does nothing when every throw makes one - e.g. 6+ dice with Pair counting.")]
    [SerializeField, Range(0f, 1f)] private float firstRollCombinationReduction = 0.3f;
    [Tooltip("Score the dice as soon as the last reroll lands - unless a card's right click could still change them.")]
    [SerializeField] private bool autoFinishWhenOutOfRerolls = true;
    [Tooltip("Seconds the result stays on the table before the turn hands back.")]
    [SerializeField, Min(0f)] private float finishDelay = 1.5f;
    [Tooltip("Seconds after a round is scored (the last reroll landed or Finish was pressed) before the Round Score " +
             "popup shows. The next round - or, after the last one, the turn's result - follows it.")]
    [SerializeField, Min(0f)] private float roundScoreAlertDelay = 0.8f;
    [Tooltip("Seconds after the Round Score popup before the turn's next round starts (its Round popup shows).")]
    [SerializeField, Min(0f)] private float nextRoundDelay = 1.6f;
    [Tooltip("Seconds after the next round's Round popup before its dice are thrown.")]
    [SerializeField, Min(0f)] private float nextRoundRollDelay = 1f;
    [Tooltip("Hide the table root when the turn ends.")]
    [SerializeField] private bool hideTableOnFinish = true;
    [Tooltip("The risky goal is the target times this. Reaching it upgrades the next cards offered.")]
    [SerializeField, Min(1f)] private float riskyGoalMultiplier = 1.5f;

    [Header("Tutorial")]
    [Tooltip("Played over the table before the first round of every run (each launch / StartGame). " +
             "The first throw waits for it. Empty: no tutorial.")]
    [SerializeField] private DialogueContainer tutorialDialogue;
    [Tooltip("Optional speaker for the tutorial's nodes that don't name one.")]
    [SerializeField] private Talkable tutorialSpeaker;
    [Tooltip("Binds its scene objects into a DialogueContainerWithModels tutorial (models enabled per node). " +
             "Defaults to the first one found.")]
    [SerializeField] private TalkableWModelsController tutorialObjects;
    [Tooltip("Keep the skip button (the DialogueManager's hold-to-skip indicator, with its fill image) on screen " +
             "from the tutorial's first node until it ends.")]
    [SerializeField] private bool showSkipDuringTutorial = true;
    [Tooltip("Debug: play the tutorial every round, not only the first time.")]
    [SerializeField] private bool alwaysPlayTutorial = false;

    [Header("Scoring")]
    [Tooltip("A round scores points x mult: points start at the hand's points, mult at 1, and cards add to / multiply both. " +
             "This adds the sum of all dice to the points: every die scores its face value, the hand adds its bonus.")]
    [SerializeField] private bool addDiceSum = true;
    [Tooltip("A round whose dice make no hand (Nothing) scores 0 - no dice sum, no card bonuses.")]
    [SerializeField] private bool noPointsWithoutCombination = false;
    [Tooltip("Each hand's bonus. Defaults from Balance/dice_balance.py (POINTS), which also works out " +
             "LevelManager's targets from it.")]
    [SerializeField] private List<CombinationScore> combinations = new List<CombinationScore>
    {
        new CombinationScore(PokerDiceCombination.Nothing, "Nothing", 0),
        new CombinationScore(PokerDiceCombination.SingleOne, "Single One", 0),
        new CombinationScore(PokerDiceCombination.OnePair, "Pair", 10),
        new CombinationScore(PokerDiceCombination.TwoPair, "Two Pair", 20),
        new CombinationScore(PokerDiceCombination.ThreeOfAKind, "Three of a Kind", 30),
        new CombinationScore(PokerDiceCombination.SmallStraight, "Small Straight", 50),
        new CombinationScore(PokerDiceCombination.LargeStraight, "Large Straight", 100),
        new CombinationScore(PokerDiceCombination.FullHouse, "Full House", 67),
        new CombinationScore(PokerDiceCombination.FourOfAKind, "Four of a Kind", 100),
        new CombinationScore(PokerDiceCombination.TwoThreeOfAKind, "Two Three of a Kind", 150),
        new CombinationScore(PokerDiceCombination.FiveOfAKind, "Five of a Kind", 200),
    };

    [Header("UI")]
    [Tooltip("The table (background, dice, buttons). Shown for a round.")]
    [SerializeField] private GameObject tableRoot;
    [SerializeField] private Button rollButton;
    [SerializeField] private TextMeshProUGUI rollButtonLabel;
    [SerializeField] private string firstRollLabel = "Roll";
    [Tooltip("{0} = rerolls left.")]
    [SerializeField] private string rerollLabel = "Reroll ({0})";
    [Tooltip("The Roll button after the first throw, with Unlimited Rerolls.")]
    [SerializeField] private string unlimitedRerollLabel = "Reroll";
    [SerializeField] private Button finishButton;
    [Tooltip("What the Finish button always says - written into its own text. It ends the round, and can be " +
             "pressed any time the dice are down (not mid-roll, not before the first throw).")]
    [SerializeField] private string finishLabel = "Finish";
    [Tooltip("{0} = rerolls left.")]
    [SerializeField] private TextMeshProUGUI rerollsText;
    [SerializeField] private string rerollsFormat = "Rerolls: {0}";
    [SerializeField] private TextMeshProUGUI targetText;
    [Tooltip("{0} = target score, {1} = risky goal.")]
    [SerializeField] private string targetFormat = "Need {0}";
    [Tooltip("Target Text once the score reaches the target: now it shows the risky goal. {0} = risky goal, " +
             "{1} = target score. Back to Target Format if a reroll drops the score under the target.")]
    [SerializeField] private string reachedTargetFormat = "Risky {0}";
    [Tooltip("Scale punch on Target Text when it switches to the risky goal. 0: none.")]
    [SerializeField, Min(0f)] private float targetSwitchPunch = 0.2f;
    [Tooltip("Optional: shows the risky goal.")]
    [SerializeField] private TextMeshProUGUI riskyTargetText;
    [Tooltip("{0} = risky goal, {1} = target score.")]
    [SerializeField] private string riskyTargetFormat = "Risky {0}";
    [Tooltip("Optional: which round of the turn is on. Hidden while a turn has a single round.")]
    [SerializeField] private TextMeshProUGUI roundText;
    [Tooltip("{0} = current round, {1} = rounds per turn.")]
    [SerializeField] private string roundFormat = "Round {0}/{1}";
    [Tooltip("The turn's total so far (rounds banked + the dice on the table). With more than one round per " +
             "turn: the points of the finished rounds only - the running round's go to Round Score Text, and " +
             "are added here as the round finishes.")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [Tooltip("{0} = the score. Copied into the CountingText added to Score Text when it has none; a CountingText " +
             "already there keeps its own format.")]
    [SerializeField] private string scoreFormat = "{0}";
    [Tooltip("Counts the score up / down and shakes when it changes. Defaults to the CountingText on Score Text (added if missing).")]
    [SerializeField] private CountingText scoreCounter;
    [Tooltip("Optional: the running round's points (what the dice on the table make). Only shown while a turn " +
             "has more than one round. Counts like Score Text (a CountingText is added if missing).")]
    [SerializeField] private TextMeshProUGUI roundScoreText;
    [Tooltip("{0} = the running round's points. Copied into the CountingText added to Round Score Text when it has none.")]
    [SerializeField] private string roundScoreFormat = "{0}";
    [Tooltip("Score Text's colour, with more than one round per turn, while the points banked over the rounds " +
             "are under the target. Round Score Text keeps its own colour.")]
    [SerializeField, FormerlySerializedAs("roundBelowTargetColor")]
    private Color scoreBelowTargetColor = new Color(1f, 0.35f, 0.35f, 1f);
    [Tooltip("The hand the dice make. Empty while they make none that counts (Nothing, or only hands turned off " +
             "in the scoring table).")]
    [SerializeField] private TextMeshProUGUI combinationText;
    [Tooltip("Optional: the running round's score as mult and points. Empty before the first throw lands.")]
    [SerializeField] private TextMeshProUGUI comboScoreText;
    [Tooltip("{0} = mult, {1} = points, {2} = the score (points x mult).")]
    [SerializeField] private string comboScoreFormat = "{0} x {1}";
    [Tooltip("Scale punch on Combo Score Text when its mult or points change. 0: none.")]
    [SerializeField, Min(0f)] private float comboScorePunch = 0.15f;
    [Tooltip("Shakes the combination text's letters harder the stronger the hand (its points against the best hand " +
             "in the scoring table) by driving its Shake Strength. Defaults to the TextAnimation on Combination Text " +
             "(added if missing); Shake is added to its animations when it has none.")]
    [SerializeField] private TextAnimation combinationAnimation;
    [Tooltip("TextAnimation Shake Strength for the weakest (x) and the best (y) hand. No hand: no shake.")]
    [SerializeField] private Vector2 combinationShakeStrength = new Vector2(0.5f, 6f);
    [Tooltip("Seconds the shake takes to reach a new hand's strength.")]
    [SerializeField, Min(0f)] private float combinationShakeBlend = 0.15f;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private string passedText = "They still care.";
    [SerializeField] private string failedText = "...";

    [Header("Popups")]
    public DicePopups popups = new DicePopups();

    [Header("Die Info Card")]
    [Tooltip("Resting the mouse on a special die shows its card (what it does).")]
    [SerializeField] private bool showDieInfo = true;
    [Tooltip("Seconds the mouse rests on the die before its card shows.")]
    [SerializeField, Min(0f)] private float dieInfoDelay = 0.6f;
    [Tooltip("The card. Empty: the CardManager's Card Prefab.")]
    [SerializeField] private CardView dieInfoPrefab;
    [Tooltip("Where the card is shown, drawn over the dice. Empty: the Table Root (so it hides with the table).")]
    [SerializeField] private RectTransform dieInfoParent;
    [Tooltip("The card's centre, from the die's (UI units of the parent). It is kept inside the parent's rect.")]
    [SerializeField] private Vector2 dieInfoOffset = new Vector2(0f, 220f);
    [Tooltip("Size of the card relative to the prefab.")]
    [SerializeField, Min(0.01f)] private float dieInfoScale = 0.6f;
    [SerializeField, Min(0f)] private float dieInfoFadeDuration = 0.15f;

    [Header("Outcome Spotlight")]
    [Tooltip("The big LOST / PASSED / HOPE text once the turn is over, before the fade to the outcome dialogue.")]
    public DiceOutcomeSpotlight spotlight = new DiceOutcomeSpotlight();

    [Header("Debug")]
    [Tooltip("Debug only: during a turn the keys below end it on the spot - lost, won, or won with the risky goal.")]
    [SerializeField] private bool debugKeys = true;
    [SerializeField] private Key debugLoseKey = Key.W;
    [SerializeField] private Key debugWinKey = Key.E;
    [SerializeField] private Key debugRiskyWinKey = Key.R;

    [Header("Events")]
    public UnityEvent onTurnStarted = new UnityEvent();
    /// <summary>Each round of a turn, the first included.</summary>
    public UnityEvent onRoundStarted = new UnityEvent();
    public UnityEvent onRolled = new UnityEvent();
    /// <summary>The round's score (that round alone).</summary>
    public UnityEvent<int> onRoundFinished = new UnityEvent<int>();
    /// <summary>The turn's total score (every round added up).</summary>
    public UnityEvent<int> onTurnFinished = new UnityEvent<int>();
    /// <summary>The outcome spotlight frame cut in (with its sfx).</summary>
    public UnityEvent<DiceOutcome> onSpotlight = new UnityEvent<DiceOutcome>();

    public bool TurnActive { get; private set; }
    /// <summary>The round being played, 1-based (0 before the turn's first round).</summary>
    public int CurrentRound { get; private set; }
    /// <summary>Rounds in the running turn (<see cref="roundsPerTurn"/> as it was when the turn started).</summary>
    public int RoundsInTurn { get; private set; } = 1;
    public bool IsLastRound => CurrentRound >= RoundsInTurn;
    /// <summary>Points banked by the turn's finished rounds.</summary>
    public int BankedScore { get; private set; }
    public bool HasRolled { get; private set; }
    public int RerollsLeft { get; private set; }
    public int Target { get; private set; }
    /// <summary>The risky goal: <see cref="Target"/> x <see cref="riskyGoalMultiplier"/>.</summary>
    public int RiskyTarget => Mathf.CeilToInt(Target * riskyGoalMultiplier);
    /// <summary>The scoring table: each hand's bonus points.</summary>
    public IReadOnlyList<CombinationScore> Combinations => combinations;
    public bool IsRolling => landingPending > 0;
    public IReadOnlyList<Dice> AllDice => dice;
    /// <summary>Dice in the running round: the turn's dice count, without the ones destroyed this round.</summary>
    public IReadOnlyList<Dice> DiceInPlay
    {
        get
        {
            List<Dice> inPlay = new List<Dice>();
            foreach (Dice die in dice)
                if (InPlay(die)) inPlay.Add(die);
            return inPlay;
        }
    }

    private static bool InPlay(Dice die) => die && die.gameObject.activeSelf && !die.IsRemoved;

    private bool finishing;
    private int landingPending;
    // The dice of the throw in the air, and whether they bust without a combination once down.
    private readonly List<Dice> thrown = new List<Dice>();
    private bool thrownCanBust;
    // Dice on the table this turn (the first ones of the list); the rest are hidden.
    private int diceInTurn;

    // The round has opened but the first throw isn't allowed yet (tutorial, first roll delay).
    private bool introRunning;
    private Tween firstRollTween;
    // Bumped per turn and on cancel: a tutorial ending late only carries on the turn it belongs to.
    private int turnId;
    private DialogueContainer runningTutorial;
    private DialogueManager tutorialManager;
    private UnityAction tutorialEnded;
    private UnityAction tutorialFirstNode;
    private bool tutorialPinnedSkip;
    // Set once the tutorial has been seen to the end in this run; cleared by ResetTutorial (StartGame).
    private bool tutorialPlayedThisRun;
    private IReadOnlyList<CardInstance> cards = Array.Empty<CardInstance>();
    // Mult gained this round by destroying dice (Destroy Face cards); added to every hand's mult.
    private float roundMult;
    // Rerolls earned by cards in a finished round, for the next one - carried into the next turn too.
    private int carriedRerolls;
    private Tween comboPunchTween;
    private Vector3? comboTextScale;
    private Action<DiceTurnResult> turnCallback;
    // The turn's rounds so far; the last one is added (not yet banked) as it finishes.
    private readonly List<DiceRoundResult> roundResults = new List<DiceRoundResult>();
    private DiceTurnResult pendingResult;
    // The running turn's spotlight title, and whether its table waits for HideTable (see StartTurn).
    private string turnSpotlightTitle;
    private bool turnKeepsTableUp;
    private Tween finishTween;
    private readonly List<Tween> popupTweens = new List<Tween>();
    private float nextPopupTime;

    // The target text shows the risky goal (the score reached the target).
    private bool targetShowsRisky;
    private Tween targetPunchTween;
    private Vector3? targetTextScale;

    // The Finish button's own text, which always shows finishLabel.
    private TMP_Text finishButtonLabel;

    // The score on screen: the last one the dice made, kept while they are rolled again.
    private DiceScore shownScore;

    /// <summary>
    /// The score's counter: <see cref="scoreCounter"/>, else the CountingText on <see cref="scoreText"/> -
    /// added when it has none, so the score always counts instead of jumping.
    /// </summary>
    private CountingText ScoreCounter => CounterOn(scoreText, scoreFormat, ref scoreCounter);

    /// <summary>The running round's counter: the CountingText on <see cref="roundScoreText"/> (added if missing).</summary>
    private CountingText RoundScoreCounter => CounterOn(roundScoreText, roundScoreFormat, ref roundScoreCounter);
    private CountingText roundScoreCounter;
    // Score Text's own colour, from the scene: back to it once the target is reached.
    private Color? scoreColor;

    private static CountingText CounterOn(TextMeshProUGUI text, string format, ref CountingText counter)
    {
        if (counter || !text) return counter;

        counter = text.GetComponent<CountingText>();
        if (!counter)
        {
            counter = text.gameObject.AddComponent<CountingText>();
            // The counter only has the one number to show: {1} / {2} would have nothing to fill them.
            if (!format.Contains("{1}") && !format.Contains("{2}")) counter.Format = format;
        }
        return counter;
    }

    /// <summary>
    /// The combination text's letter animation: <see cref="combinationAnimation"/>, else the TextAnimation on
    /// <see cref="combinationText"/> - added when it has none. Either way it shakes its whole text.
    /// </summary>
    private TextAnimation CombinationAnimation
    {
        get
        {
            if (combinationAnimation || !combinationText) return combinationAnimation;

            combinationAnimation = combinationText.GetComponent<TextAnimation>();
            if (!combinationAnimation)
            {
                combinationAnimation = combinationText.gameObject.AddComponent<TextAnimation>();
                combinationAnimation.animationTarget = TextAnimation.AnimationTarget.WholeText;
            }
            if (!combinationAnimation.animations.Contains(TextAnimation.AnimationType.Shake))
                combinationAnimation.animations.Add(TextAnimation.AnimationType.Shake);
            if (combinationAnimation.animationTarget is TextAnimation.AnimationTarget.None or TextAnimation.AnimationTarget.Tags)
                combinationAnimation.animationTarget = TextAnimation.AnimationTarget.Both;

            combinationAnimation.shakeStrength = 0f;
            return combinationAnimation;
        }
    }

    // The combination's shake strength right now and the tween easing it to a new hand's.
    private Tween combinationShakeTween;
    private float combinationShakeTarget = -1f;

    /// <summary>Eases the combination text's Shake Strength to the one for a hand of <paramref name="strength"/> (0..1).</summary>
    private void SetCombinationShake(float strength)
    {
        TextAnimation anim = CombinationAnimation;
        if (!anim) return;

        float target = strength > 0f ? Mathf.Lerp(combinationShakeStrength.x, combinationShakeStrength.y, strength) : 0f;
        if (Mathf.Approximately(target, combinationShakeTarget)) return;
        combinationShakeTarget = target;

        combinationShakeTween.Stop();
        if (combinationShakeBlend > 0f && isActiveAndEnabled)
            combinationShakeTween = Tween.Custom(anim, anim.shakeStrength, target, combinationShakeBlend,
                (a, v) => a.shakeStrength = v);
        else anim.shakeStrength = target;
    }

    private bool CanLock => TurnActive && HasRolled && !IsRolling && !finishing && HasRerollsLeft;

    private bool HasRerollsLeft => unlimitedRerolls || RerollsLeft > 0;

    /// <summary>True when clicking a die would lock or unlock it right now.</summary>
    public bool CanLockNow => CanLock;

    private bool initialized;

    private void Awake() => Initialize();

    /// <summary>
    /// Sets up the dice and buttons. Runs from Awake, or from <see cref="StartTurn"/> when this sits on
    /// a table panel that starts hidden (an inactive object gets no Awake until it is shown).
    /// </summary>
    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);

        List<Dice> spawned = SpawnDice(startDiceCount);

        foreach (Dice die in dice)
        {
            if (!die) continue;
            die.Init(this);
            die.SetFaces(faceSprites);
            ShowRandomFace(die);
        }

        ScatterSpawned(spawned);

        if (Area && Area.GetComponent<LayoutGroup>())
            h.Out($"DiceManager: the dice area '{Area.name}' has a layout group - it will snap the dice back. " +
                  "Remove it: the dice place and move themselves.");

        if (rollButton) rollButton.onClick.AddListener(Roll);
        if (finishButton)
        {
            finishButton.onClick.AddListener(Finish);
            finishButtonLabel = finishButton.GetComponentInChildren<TMP_Text>(true);
            if (rerollsText && rerollsText.transform.IsChildOf(finishButton.transform))
                h.Out($"DiceManager: Rerolls Text '{rerollsText.name}' is the Finish button's label - it keeps " +
                      $"showing '{finishLabel}'. Point Rerolls Text at a text of its own.");
        }
        if (tableRoot) tableRoot.SetActive(false);
        if (resultText) resultText.gameObject.SetActive(false);
        foreach (GameObject frame in new[] { spotlight.Root, spotlight.TitleRoot })
        {
            if (!frame) continue;
            frame.SetActive(false);
            if (hideTableOnFinish && tableRoot && frame.transform.IsChildOf(tableRoot.transform))
                h.Out($"DiceManager: the spotlight frame '{frame.name}' is inside the Table Root - it's hidden " +
                      "with the table. Move it out.");
        }
    }

    /// <summary>
    /// Where dice live: <see cref="diceParent"/>, else the template die's parent, else the table root.
    /// Rolled dice land inside its rect.
    /// </summary>
    private RectTransform Area
    {
        get
        {
            if (diceParent) return diceParent;
            if (dicePrefab && dicePrefab.gameObject.scene.IsValid() && dicePrefab.transform.parent is RectTransform templateParent)
                return templateParent;
            if (tableRoot && tableRoot.transform is RectTransform table) return table;
            return transform as RectTransform;
        }
    }

    /// <summary>Spawns dice from <see cref="dicePrefab"/> until the table has <paramref name="count"/>. Returns the new ones.</summary>
    private List<Dice> SpawnDice(int count)
    {
        List<Dice> spawned = new List<Dice>();
        bool template = dicePrefab && dicePrefab.gameObject.scene.IsValid();
        dice.RemoveAll(die => !die || die == dicePrefab);
        if (template) dicePrefab.gameObject.SetActive(false);

        int missing = count - dice.Count;
        if (missing <= 0) return spawned;

        if (!dicePrefab)
        {
            h.Out($"DiceManager: {missing} dice short of {count} and no Dice Prefab to spawn them.");
            return spawned;
        }

        Transform parent = Area ? Area : transform;
        for (int i = 0; i < missing; i++)
        {
            Dice die = Instantiate(dicePrefab, parent);
            die.name = $"{dicePrefab.name} {dice.Count + 1}";
            die.gameObject.SetActive(true);
            dice.Add(die);
            spawned.Add(die);
        }

        return spawned;
    }

    // ---------------------------------------------------------------------
    // Dice kinds (won in the dice choice)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Turns a random plain die of the table into <paramref name="offer"/>'s kind of die, in its tint and showing its
    /// card when hovered - or, when none is plain any more, a random one. It keeps its face unless the kind doesn't
    /// allow it. Returns the die (null without dice).
    /// </summary>
    public Dice ReplaceRandomDefaultDie(DiceKindOffer offer)
    {
        Initialize();
        if (offer == null) return null;
        DiceKind kind = offer.kind;

        // The table's dice: the start dice, plus the ones Extra Dice cards brought.
        int count = Mathf.Min(dice.Count, Mathf.Max(diceInTurn, startDiceCount));
        List<Dice> plain = new List<Dice>(), any = new List<Dice>();
        for (int i = 0; i < count; i++)
        {
            Dice die = dice[i];
            if (!die) continue;
            any.Add(die);
            if (die.Kind == DiceKind.Default) plain.Add(die);
        }

        List<Dice> pool = plain.Count > 0 ? plain : any;
        if (pool.Count == 0)
        {
            h.Out("DiceManager: no dice to replace.");
            return null;
        }

        Dice replaced = pool[UnityEngine.Random.Range(0, pool.Count)];
        replaced.SetKind(kind, offer.tint, offer.Face);
        if (!DiceKinds.Allows(kind, replaced.Value)) replaced.ShowFace(DiceKinds.RandomFace(kind));
        h.Out($"DiceManager: '{replaced.name}' is now a {kind} die.");
        return replaced;
    }

    /// <summary>Every die back to a plain one (a new game).</summary>
    public void ClearDiceKinds()
    {
        Initialize();
        foreach (Dice die in dice)
            if (die) die.SetKind(DiceKind.Default, Color.white);
        HideDieInfo();
    }

    // ---------------------------------------------------------------------
    // Die info card (hovering a special die)
    // ---------------------------------------------------------------------

    // The die the mouse rests on, waiting for the delay; the one whose card is up.
    private Dice infoPending;
    private Dice infoShown;
    private Tween infoDelayTween;
    private CardView infoCard;
    private Vector3 infoCardScale;

    /// <summary>The mouse came onto / left <paramref name="die"/>: a special die's card shows after <see cref="dieInfoDelay"/>.</summary>
    public void SetDieHovered(Dice die, bool hovered)
    {
        if (!hovered)
        {
            if (die == infoPending || die == infoShown) HideDieInfo();
            return;
        }

        if (die == infoPending || die == infoShown) return;
        HideDieInfo();
        if (!showDieInfo || !die || die.Kind == DiceKind.Default || die.Info == null) return;

        infoPending = die;
        if (dieInfoDelay > 0f)
            infoDelayTween = Tween.Delay(this, dieInfoDelay, manager => manager.ShowDieInfo(), useUnscaledTime: true);
        else ShowDieInfo();
    }

    private void ShowDieInfo()
    {
        Dice die = infoPending;
        infoPending = null;
        if (!InPlay(die) || die.Info == null) return;

        CardView card = DieInfoCard();
        if (!card) return;

        infoShown = die;
        Tween.StopAll(card.Group);
        Tween.StopAll(card.transform);
        card.gameObject.SetActive(true);
        card.Bind(die.Info);
        card.transform.SetAsLastSibling();
        PlaceDieInfo();

        if (dieInfoFadeDuration > 0f)
        {
            card.Group.alpha = 0f;
            card.transform.localScale = infoCardScale * 0.9f;
            Tween.Alpha(card.Group, 1f, dieInfoFadeDuration, useUnscaledTime: true);
            Tween.Scale(card.transform, infoCardScale, dieInfoFadeDuration, Ease.OutBack, useUnscaledTime: true);
        }
        else
        {
            card.Group.alpha = 1f;
            card.transform.localScale = infoCardScale;
        }
    }

    private void HideDieInfo()
    {
        infoDelayTween.Stop();
        infoPending = null;
        if (!infoShown) return;
        infoShown = null;
        if (!infoCard) return;

        Tween.StopAll(infoCard.Group);
        Tween.StopAll(infoCard.transform);
        if (dieInfoFadeDuration > 0f && infoCard.isActiveAndEnabled)
            Tween.Alpha(infoCard.Group, 0f, dieInfoFadeDuration, useUnscaledTime: true)
                .OnComplete(infoCard, card => card.gameObject.SetActive(false));
        else infoCard.gameObject.SetActive(false);
    }

    /// <summary>The info card, made on first use. It never takes the mouse, so the die under it stays hovered.</summary>
    private CardView DieInfoCard()
    {
        if (infoCard) return infoCard;

        CardView prefab = dieInfoPrefab ? dieInfoPrefab : CardManager.Instance ? CardManager.Instance.CardPrefab : null;
        RectTransform parent = dieInfoParent ? dieInfoParent : tableRoot ? tableRoot.transform as RectTransform : Area;
        if (!prefab || !parent)
        {
            h.Out(!prefab ? "DiceManager: no card prefab for the die info card (Die Info Prefab, or the CardManager's)."
                          : "DiceManager: nowhere to show the die info card (Die Info Parent).");
            return null;
        }

        infoCard = Instantiate(prefab, parent);
        infoCard.name = "Die Info Card";
        infoCardScale = infoCard.transform.localScale * dieInfoScale;
        infoCard.Group.blocksRaycasts = false;
        infoCard.Group.interactable = false;
        infoCard.Rect.anchorMin = infoCard.Rect.anchorMax = new Vector2(0.5f, 0.5f);
        infoCard.gameObject.SetActive(false);
        return infoCard;
    }

    /// <summary>Puts the card at the die + <see cref="dieInfoOffset"/>, kept inside its parent's rect.</summary>
    private void PlaceDieInfo()
    {
        if (!infoCard || !infoShown || !(infoCard.transform.parent is RectTransform parent)) return;

        Vector2 spot = (Vector2)parent.InverseTransformPoint(infoShown.transform.position) + dieInfoOffset;

        RectTransform rect = infoCard.Rect;
        Vector2 size = Vector2.Scale(rect.rect.size, new Vector2(Mathf.Abs(infoCardScale.x), Mathf.Abs(infoCardScale.y)));
        Rect bounds = parent.rect;
        float xMin = bounds.xMin + size.x * rect.pivot.x, xMax = bounds.xMax - size.x * (1f - rect.pivot.x);
        float yMin = bounds.yMin + size.y * rect.pivot.y, yMax = bounds.yMax - size.y * (1f - rect.pivot.y);
        if (xMin <= xMax) spot.x = Mathf.Clamp(spot.x, xMin, xMax);
        if (yMin <= yMax) spot.y = Mathf.Clamp(spot.y, yMin, yMax);

        rect.localPosition = new Vector3(spot.x, spot.y, 0f);
    }

    // The card follows its die while it rolls.
    private void LateUpdate()
    {
        if (!infoShown) return;
        if (InPlay(infoShown)) PlaceDieInfo();
        else HideDieInfo();
    }

    // ---------------------------------------------------------------------
    // Placement inside the dice area
    // ---------------------------------------------------------------------

    /// <summary>
    /// A new die's starting face, set once before it is ever seen. After that a die only changes face by being
    /// thrown (or by a card), so nothing jumps on the table before a roll.
    /// </summary>
    private static void ShowRandomFace(Dice die) => die.ShowFace(UnityEngine.Random.Range(1, PokerDiceEvaluator.Faces + 1));

    /// <summary>
    /// Puts <paramref name="count"/> dice on the table for the turn: spawns the missing ones (Extra Dice cards)
    /// and hides the ones over the count.
    /// </summary>
    private void SetDiceCount(int count)
    {
        List<Dice> spawned = SpawnDice(count);
        foreach (Dice die in spawned)
        {
            die.Init(this);
            die.SetFaces(faceSprites);
            ShowRandomFace(die);
        }

        diceInTurn = count;
        for (int i = 0; i < dice.Count; i++)
            if (dice[i]) dice[i].gameObject.SetActive(i < count);

        ScatterSpawned(spawned);
    }

    /// <summary>Spreads freshly spawned dice over the area, clear of each other and of the dice already there.</summary>
    private void ScatterSpawned(List<Dice> spawned)
    {
        if (spawned.Count == 0 || !Area) return;

        List<Vector3> taken = new List<Vector3>();
        foreach (Dice die in dice)
            if (die && die.gameObject.activeSelf && !spawned.Contains(die)) taken.Add(Occupied(die, die.TablePosition));

        foreach (Dice die in spawned)
        {
            Vector3 spot = PickSpot(die, taken, anywhere: true);
            die.PlaceAt(spot);
            taken.Add(Occupied(die, spot));
        }
    }

    /// <summary>
    /// A landing spot for <paramref name="die"/> (local, in its parent): <see cref="DiceRollAnimation.travelDistance"/>
    /// away in a random direction - or, with <paramref name="anywhere"/>, anywhere in the area - kept inside the
    /// area's rect and, when there's room, clear of <paramref name="taken"/> (area-space x, y and radius).
    /// </summary>
    private Vector3 PickSpot(Dice die, List<Vector3> taken, bool anywhere)
    {
        RectTransform area = Area;
        if (!area) return die.TablePosition;

        Vector2 range = rollAnimation.travelDistance.x < rollAnimation.travelDistance.y
            ? rollAnimation.travelDistance : new Vector2(rollAnimation.travelDistance.y, rollAnimation.travelDistance.x);
        if (!anywhere && range.y <= 0f) return die.TablePosition;

        float radius = die.Radius;
        Rect bounds = area.rect;
        float xMin = bounds.xMin + radius, xMax = bounds.xMax - radius;
        float yMin = bounds.yMin + radius, yMax = bounds.yMax - radius;
        // An area smaller than the die: its centre is the only place left.
        if (xMin > xMax) xMin = xMax = bounds.center.x;
        if (yMin > yMax) yMin = yMax = bounds.center.y;

        Vector2 from = ToArea(die, die.TablePosition);
        Vector2 best = new Vector2(Mathf.Clamp(from.x, xMin, xMax), Mathf.Clamp(from.y, yMin, yMax));
        float bestClearance = float.NegativeInfinity;

        const int Tries = 20;
        for (int i = 0; i < Tries; i++)
        {
            Vector2 spot = anywhere
                ? new Vector2(UnityEngine.Random.Range(xMin, xMax), UnityEngine.Random.Range(yMin, yMax))
                : from + UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(Mathf.Max(0f, range.x), range.y);
            spot.x = Mathf.Clamp(spot.x, xMin, xMax);
            spot.y = Mathf.Clamp(spot.y, yMin, yMax);

            float clearance = Clearance(spot, radius, taken);
            if (clearance >= 0f) return FromArea(die, spot);
            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                best = spot;
            }
        }

        // Too crowded for a clean spot: the least overlapping one.
        return FromArea(die, best);
    }

    /// <summary>Smallest gap between a die at <paramref name="spot"/> and the taken spots (negative: overlapping).</summary>
    private float Clearance(Vector2 spot, float radius, List<Vector3> taken)
    {
        float clearance = float.PositiveInfinity;
        foreach (Vector3 other in taken)
        {
            float gap = Vector2.Distance(spot, other) - radius - other.z - diceSpacing;
            if (gap < clearance) clearance = gap;
        }
        return clearance;
    }

    /// <summary>A die at <paramref name="localPosition"/> as an area-space (x, y, radius) obstacle.</summary>
    private Vector3 Occupied(Dice die, Vector3 localPosition)
    {
        Vector2 p = ToArea(die, localPosition);
        return new Vector3(p.x, p.y, die.Radius);
    }

    private Vector2 ToArea(Dice die, Vector3 localPosition)
    {
        Transform parent = die.transform.parent;
        if (!parent || parent == Area) return localPosition;
        return Area.InverseTransformPoint(parent.TransformPoint(localPosition));
    }

    private Vector3 FromArea(Dice die, Vector2 areaPosition)
    {
        Transform parent = die.transform.parent;
        Vector3 z = new Vector3(0f, 0f, die.TablePosition.z);
        if (!parent || parent == Area) return (Vector3)areaPosition + z;
        Vector3 local = parent.InverseTransformPoint(Area.TransformPoint(areaPosition));
        local.z = die.TablePosition.z;
        return local;
    }

    // ---------------------------------------------------------------------
    // Turn / round
    // ---------------------------------------------------------------------

    /// <summary>
    /// Opens the table for a turn of <see cref="roundsPerTurn"/> rounds against <paramref name="target"/>
    /// with <paramref name="ownedCards"/>' bonuses. <paramref name="onFinished"/> gets the result once the
    /// last round's has been shown. <paramref name="spotlightTitle"/>: the spotlight's title frame (empty: none).
    /// <paramref name="keepTableUp"/>: the table stays up after the turn, until <see cref="HideTable"/>.
    /// </summary>
    public void StartTurn(int target, IReadOnlyList<CardInstance> ownedCards, Action<DiceTurnResult> onFinished,
        string spotlightTitle = null, bool keepTableUp = false)
    {
        Initialize();

        if (TurnActive)
        {
            h.Out("DiceManager: a turn is already running.");
            return;
        }

        TurnActive = true;
        Target = target;
        cards = ownedCards ?? Array.Empty<CardInstance>();
        turnCallback = onFinished;
        turnSpotlightTitle = spotlightTitle;
        turnKeepsTableUp = keepTableUp;
        RoundsInTurn = Mathf.Max(1, roundsPerTurn);
        CurrentRound = 0;
        BankedScore = 0;
        roundResults.Clear();

        StopPopups();
        turnId++;
        if (tableRoot) tableRoot.SetActive(true);
        if (resultText) resultText.gameObject.SetActive(false);
        HideSpotlight(instant: true);
        SetDiceCount(DiceCountFor(cards));
        onTurnStarted?.Invoke();

        PrepareRound();
        if (ShouldPlayTutorial) PlayTutorial(QueueFirstRoll);
        else QueueFirstRoll();
    }

    /// <summary>
    /// Resets the dice and rerolls for the turn's next round. The first throw waits for <see cref="QueueFirstRoll"/>.
    /// The dice stay as they were left - the last round's, or the last turn's - until that throw.
    /// </summary>
    private void PrepareRound()
    {
        CurrentRound++;
        HasRolled = false;
        finishing = false;
        landingPending = 0;
        RerollsLeft = RerollsFor(cards) + carriedRerolls;
        carriedRerolls = 0;
        roundMult = 0f;

        // A turn's first round: the table has only just opened, so the dice are tidied at once - unlocked, upright,
        // the destroyed ones back. Later rounds leave them exactly as the last round did (place, face, tilt, lock):
        // nothing moves before the throw, which picks every die up as it lies (see ReleaseDice).
        if (CurrentRound == 1)
            for (int i = 0; i < Mathf.Min(diceInTurn, dice.Count); i++)
            {
                Dice die = dice[i];
                if (!die) continue;
                die.Restore();
                die.ResetDice();
            }

        introRunning = true;
        UpdateUI();
        onRoundStarted?.Invoke();
    }

    /// <summary>The first throw becomes possible (and, with Auto First Roll, happens) after <see cref="firstRollDelay"/>.</summary>
    private void QueueFirstRoll()
    {
        firstRollTween.Stop();
        if (firstRollDelay > 0f) firstRollTween = Tween.Delay(this, firstRollDelay, manager => manager.EndIntro());
        else EndIntro();
    }

    private void EndIntro()
    {
        if (!TurnActive) return;
        introRunning = false;
        if (autoFirstRoll && !HasRolled) Roll();
        else UpdateUI();
    }

    // ---------------------------------------------------------------------
    // Tutorial
    // ---------------------------------------------------------------------

    private bool ShouldPlayTutorial =>
        tutorialDialogue && (alwaysPlayTutorial || !tutorialPlayedThisRun);

    /// <summary>A new run: the next round plays the tutorial again.</summary>
    public void ResetTutorial() => tutorialPlayedThisRun = false;

    /// <summary>A new run: the tutorial plays again and no rerolls are carried over from the last run.</summary>
    public void ResetRun()
    {
        ResetTutorial();
        carriedRerolls = 0;
    }

    /// <summary>Plays the tutorial over the table, then <paramref name="then"/> (if this turn is still on).</summary>
    private void PlayTutorial(Action then)
    {
        DialogueManager manager = DialogueManager.Instance;
        if (!manager || manager.DialogueActive)
        {
            h.Out(manager ? "DiceManager: a dialogue is already running, the tutorial waits for the next round."
                          : "DiceManager: no DialogueManager in the scene, skipping the tutorial.");
            then();
            return;
        }

        // A runtime copy: filling in speakers writes into the nodes, which would otherwise end up in the asset.
        DialogueContainer instance = Instantiate(tutorialDialogue);
        instance.name = tutorialDialogue.name;
        if (tutorialSpeaker) tutorialSpeaker.InitDialogue(instance);
        else
        {
            instance.AssignNodeIds();
            foreach (DialogueNode node in instance.nodes) node?.Init();
        }

        int id = turnId;
        runningTutorial = instance;
        tutorialManager = manager;

        tutorialEnded = () =>
        {
            StopTutorial();
            tutorialPlayedThisRun = true;
            PlayHistory.MarkDialoguePlayed(tutorialDialogue.name);
            if (id == turnId && TurnActive) then();
        };
        if (manager.onDialogueEnd == null) manager.onDialogueEnd = new UnityEvent();
        manager.onDialogueEnd.AddListener(tutorialEnded);

        if (showSkipDuringTutorial)
        {
            // From the first node on: the skip button shows as the tutorial's text does.
            tutorialFirstNode = () =>
            {
                manager.onStartNode.RemoveListener(tutorialFirstNode);
                tutorialFirstNode = null;
                manager.PinSkipIndicator = true;
                tutorialPinnedSkip = true;
            };
            if (manager.onStartNode == null) manager.onStartNode = new UnityEvent();
            manager.onStartNode.AddListener(tutorialFirstNode);
        }

        // A models dialogue only drives the objects bound into it: started straight through the manager
        // its slots stay empty and no model ever turns on. The controller binds them and plays their intro.
        if (instance is DialogueContainerWithModels modelsDialogue)
        {
            if (!tutorialObjects) tutorialObjects = FindFirstObjectByType<TalkableWModelsController>(FindObjectsInactive.Include);
            if (tutorialObjects)
            {
                tutorialObjects.PlayDialogue(modelsDialogue, tutorialSpeaker);
                return;
            }
            h.Out($"DiceManager: '{instance.name}' has models but no TalkableWModelsController is in the scene.");
        }

        manager.StartDialogue(instance, tutorialSpeaker);
    }

    /// <summary>Stops listening to the tutorial and drops its copy (the dialogue itself is ended by whoever ends it).</summary>
    private void StopTutorial()
    {
        DialogueManager manager = tutorialManager;
        if (manager)
        {
            if (tutorialEnded != null) manager.onDialogueEnd.RemoveListener(tutorialEnded);
            if (tutorialFirstNode != null) manager.onStartNode.RemoveListener(tutorialFirstNode);
            if (tutorialPinnedSkip) manager.PinSkipIndicator = false;
        }

        tutorialEnded = null;
        tutorialFirstNode = null;
        tutorialPinnedSkip = false;
        tutorialManager = null;
        if (runningTutorial) Destroy(runningTutorial);
        runningTutorial = null;
    }

    /// <summary>First call: throws every die. Later calls: rethrows the unlocked ones, using a reroll.</summary>
    public void Roll()
    {
        if (!TurnActive || finishing || IsRolling || introRunning) return;

        bool firstThrow = !HasRolled;
        if (firstThrow) ReleaseDice();

        List<Dice> toRoll = new List<Dice>();
        foreach (Dice die in DiceInPlay)
            if (firstThrow || !die.IsLocked) toRoll.Add(die);

        if (!firstThrow)
        {
            if (!HasRerollsLeft || toRoll.Count == 0) return;
            if (!unlimitedRerolls) RerollsLeft--;
        }

        if (toRoll.Count == 0) return;

        // The dice locked for this roll stay locked for the rest of the round.
        foreach (Dice die in DiceInPlay)
            if (die.IsLocked) die.Seal();

        HasRolled = true;
        Throw(toRoll, firstThrow, canBust: bustWithoutCombination && (!firstThrow || bustOnFirstThrow));
        PlaySfx(rollSfx);
        UpdateUI();
    }

    /// <summary>
    /// A round's first throw takes every die of the turn as the last round left it: destroyed ones come back and
    /// locked ones are freed - they stay where they lie, and the throw eases them out of the lock.
    /// </summary>
    private void ReleaseDice()
    {
        for (int i = 0; i < Mathf.Min(diceInTurn, dice.Count); i++)
        {
            Dice die = dice[i];
            if (!die) continue;
            die.Restore();
            die.Unlock();
        }
    }

    /// <summary>
    /// Throws <paramref name="toRoll"/>; the others stay put. With <paramref name="canBust"/> the round busts once
    /// they're down if they make no combination by themselves.
    /// </summary>
    private void Throw(List<Dice> toRoll, bool firstThrow = false, bool canBust = false)
    {
        landingPending = toRoll.Count;
        thrown.Clear();
        thrown.AddRange(toRoll);
        thrownCanBust = canBust;
        List<int> results = firstThrow ? FirstThrowResults(toRoll) : ResultsFor(toRoll);

        // Dice staying put (locked) block their spots; each thrown die then claims where it will land.
        List<Vector3> taken = new List<Vector3>();
        foreach (Dice die in DiceInPlay)
            if (!toRoll.Contains(die)) taken.Add(Occupied(die, die.TablePosition));

        for (int i = 0; i < toRoll.Count; i++)
        {
            Dice die = toRoll[i];
            Vector3 landAt = PickSpot(die, taken, anywhere: false);
            taken.Add(Occupied(die, landAt));

            die.Roll(results[i], rollAnimation, faceSprites, tumbleSprites, i * rollAnimation.stagger, landAt, OnDieLanded);
        }
    }

    /// <summary>
    /// Where <paramref name="toRoll"/> will land, each by its <see cref="DiceKind"/>: plain dice anywhere, Blue /
    /// Yellow / Pink on the faces they allow; then Green on the face the other dice show most, then Red on one
    /// none of them show (the dice staying put included).
    /// </summary>
    private List<int> ResultsFor(List<Dice> toRoll)
    {
        int[] results = new int[toRoll.Count];
        List<int> known = new List<int>();
        foreach (Dice die in DiceInPlay)
            if (!toRoll.Contains(die)) known.Add(die.Value);

        for (int i = 0; i < toRoll.Count; i++)
        {
            DiceKind kind = toRoll[i].Kind;
            if (kind is DiceKind.Green or DiceKind.Red) continue;
            results[i] = DiceKinds.RandomFace(kind);
            known.Add(results[i]);
        }

        // Every green copies the same face: the one shown most before any of them landed.
        int mostCommon = DiceKinds.MostCommon(known);
        for (int i = 0; i < toRoll.Count; i++)
        {
            if (toRoll[i].Kind != DiceKind.Green) continue;
            results[i] = mostCommon > 0 ? mostCommon : DiceKinds.RandomFace(DiceKind.Green);
            known.Add(results[i]);
        }

        // Each red differs from the reds before it too. Every face taken (7+ dice): any face.
        for (int i = 0; i < toRoll.Count; i++)
        {
            if (toRoll[i].Kind != DiceKind.Red) continue;
            int missing = DiceKinds.Missing(known);
            results[i] = missing > 0 ? missing : DiceKinds.RandomFace(DiceKind.Red);
            known.Add(results[i]);
        }

        return new List<int>(results);
    }

    /// <summary>
    /// A round's first throw: a combination is <see cref="firstRollCombinationReduction"/> less likely than
    /// with fair dice. A throw that makes a hand is, at that chance, swapped for a fair throw that makes none.
    /// </summary>
    private List<int> FirstThrowResults(List<Dice> toRoll)
    {
        List<int> results = ResultsFor(toRoll);
        if (!MakesHand(results) || UnityEngine.Random.value >= firstRollCombinationReduction) return results;

        // Fair dice redrawn until they make no hand. Some dice counts (and green dice) always make one, so the
        // tries are capped.
        for (int attempt = 0; attempt < 500; attempt++)
        {
            List<int> retry = ResultsFor(toRoll);
            if (!MakesHand(retry)) return retry;
        }
        return results;
    }

    /// <summary>
    /// Whether <paramref name="values"/> make any hand that counts as a combination. <see cref="PokerDiceCombination.SingleOne"/>
    /// only counts with <paramref name="savesFromBust"/> (it keeps a reroll from busting, but isn't a combination
    /// for the first throw's odds).
    /// </summary>
    private bool MakesHand(IReadOnlyList<int> values, bool savesFromBust = false)
    {
        foreach (PokerDiceCombination hand in PokerDiceEvaluator.Contained(values, StraightGapsFor(cards)))
        {
            if (hand == PokerDiceCombination.SingleOne)
            {
                if (savesFromBust && ScoreFor(hand) != null) return true;
            }
            else if (CountsAsCombination(hand)) return true;
        }
        return false;
    }

    /// <summary>Locks or unlocks <paramref name="die"/>, when locking is allowed right now.</summary>
    public void ToggleLock(Dice die)
    {
        // Sealed (locked before the last roll): locked for good.
        if (!die || !CanLock || die.IsSealed) return;

        die.SetLocked(!die.IsLocked, lockStyle);
        PlaySfx(die.IsLocked ? lockSfx : unlockSfx);
        UpdateUI();
    }

    // ---------------------------------------------------------------------
    // Die actions (cards used by right clicking a die)
    // ---------------------------------------------------------------------

    /// <summary>Dice can be acted on: the round's dice are down and it isn't being scored.</summary>
    private bool CanAct => TurnActive && HasRolled && !IsRolling && !finishing && !introRunning;

    /// <summary>The owned card a right click on <paramref name="die"/> would use right now, or null.</summary>
    public CardInstance DieActionFor(Dice die)
    {
        if (!CanAct || !InPlay(die) || die.IsSealed) return null;

        foreach (CardInstance card in cards)
        {
            CardEffect effect = card?.Effect;
            if (effect == null || !effect.ActsOn(die.Value)) continue;
            // The last die can't go: the round needs something to score.
            if (effect.type == CardEffectType.DestroyFace && DiceInPlay.Count <= 1) continue;
            // A blue die stays odd, a yellow one even, a pink one high.
            if (effect.type == CardEffectType.FlipFace && !DiceKinds.Allows(die.Kind, effect.FlipOf(die.Value))) continue;
            return card;
        }
        return null;
    }

    /// <summary>A cyan die not showing 1, while the dice can be acted on: a right click turns it to 1.</summary>
    private bool CanTurnToOne(Dice die) =>
        CanAct && InPlay(die) && !die.IsSealed && die.Kind == DiceKind.Cyan && die.Value != 1;

    /// <summary>True when right clicking <paramref name="die"/> would use a card on it (or turn a cyan die to 1).</summary>
    public bool HasDieAction(Dice die) => DieActionFor(die) != null || CanTurnToOne(die);

    /// <summary>
    /// Right click: uses the first owned card that acts on <paramref name="die"/>'s face - or, with none, turns a
    /// cyan die to 1.
    /// </summary>
    public void UseDieAction(Dice die)
    {
        CardInstance card = DieActionFor(die);
        if (card == null)
        {
            if (!CanTurnToOne(die)) return;
            DiceScore old = shownScore;
            die.Flip(1);
            PlaySfx(flipSfx);
            UpdateUI();
            ShowRollPopups(old, shownScore);
            return;
        }

        CardEffect effect = card.Effect;
        DiceScore before = shownScore;
        switch (effect.type)
        {
            case CardEffectType.RerollFace:
                // Free: no reroll used. The landing scores it like any other throw.
                Throw(new List<Dice> { die });
                PlaySfx(rollSfx);
                UpdateUI();
                return;

            case CardEffectType.DestroyFace:
                int gained = Mathf.Max(0, card.Amount);
                float multGained = Mathf.Max(0f, card.Multiplier);
                RerollsLeft += gained;
                roundMult += multGained;
                die.Remove();
                PlaySfx(destroySfx);
                if (gained > 0 && !unlimitedRerolls)
                    Popup(string.Format(popups.rerollsGainedFormat, gained), ScoreAnchor, popups.scoreOffset, popups.gainColor);
                if (multGained > 0f)
                    Popup(string.Format(popups.multGainedFormat, CardEffect.FormatMult(multGained)), ScoreAnchor,
                        popups.scoreOffset, popups.gainColor);
                break;

            case CardEffectType.FlipFace:
                die.Flip(effect.FlipOf(die.Value));
                PlaySfx(flipSfx);
                break;
        }

        UpdateUI();
        ShowRollPopups(before, shownScore);
    }

    /// <summary>Whether a right click could still change something about the dice on the table.</summary>
    private bool AnyDieAction()
    {
        foreach (Dice die in DiceInPlay)
            if (HasDieAction(die)) return true;
        return false;
    }

    /// <summary>
    /// Scores the dice and ends the round. After <see cref="roundScoreAlertDelay"/> the round's score pops up
    /// (<see cref="DicePopups.roundScoreFormat"/>); then a round before the last banks its points and the next
    /// round starts after <see cref="nextRoundDelay"/>, and the last one checks the turn's total against the
    /// target and ends the turn after <see cref="finishDelay"/>.
    /// </summary>
    public void Finish()
    {
        if (!TurnActive || !HasRolled || IsRolling || finishing) return;
        finishing = true;

        int[] values = Values();
        DiceRoundResult round = new DiceRoundResult { score = RoundScore(values), values = values };
        roundResults.Add(round);
        carriedRerolls += round.score.nextRoundRerolls;

        UpdateUI();
        AfterDelay(roundScoreAlertDelay, manager => manager.RoundScored());
    }

    /// <summary>The finished round's score pops up; then the next round, or the end of the turn.</summary>
    private void RoundScored()
    {
        if (!TurnActive) return;

        DiceScore score = roundResults[roundResults.Count - 1].score;
        Popup(string.Format(popups.roundScoreFormat, score.total, CardEffect.FormatMult(score.mult), score.points),
            CombinationAnchor, popups.combinationOffset, popups.roundScoreColor, popups.roundScoreScale,
            popups.roundScoreExtraHold);

        if (!IsLastRound)
        {
            AfterDelay(nextRoundDelay, manager => manager.NextRound());
            return;
        }

        int total = BankedScore + score.total;
        EndTurn(total, total >= Target, RiskyTarget > 0 && total >= RiskyTarget);
    }

    /// <summary>Runs <paramref name="then"/> after <paramref name="delay"/> seconds (as the finish tween, so a cancel stops it), or now.</summary>
    private void AfterDelay(float delay, Action<DiceManager> then)
    {
        if (delay > 0f) finishTween = Tween.Delay(this, delay, then);
        else then(this);
    }

    /// <summary>Shows the turn's result and hands it back after <see cref="finishDelay"/>.</summary>
    private void EndTurn(int total, bool passed, bool riskyPassed)
    {
        pendingResult = new DiceTurnResult
        {
            rounds = new List<DiceRoundResult>(roundResults),
            total = total,
            target = Target,
            passed = passed,
            riskyTarget = RiskyTarget,
            riskyPassed = riskyPassed,
        };

        if (resultText)
        {
            resultText.text = pendingResult.passed ? passedText : failedText;
            resultText.gameObject.SetActive(true);
        }

        PlaySfx(pendingResult.passed ? passedSfx : failedSfx);
        UpdateUI();
        if (pendingResult.riskyPassed) Popup(popups.cardUpgradedText, GoalAnchor, popups.goalOffset, popups.riskyGoalColor);

        if (spotlight.text) AfterDelay(spotlight.delay, manager => manager.SpotlightOutcome());
        else AfterDelay(finishDelay, manager => manager.CompleteTurn());
    }

    /// <summary>The outcome frame, on at once with its sfx; the title frame follows after <see cref="DiceOutcomeSpotlight.hold"/>.</summary>
    private void SpotlightOutcome()
    {
        if (!TurnActive || pendingResult == null) return;

        ShowSpotlight(pendingResult.Outcome);
        AfterDelay(spotlight.hold, manager => manager.SpotlightTitle());
    }

    /// <summary>The title frame cuts in over the outcome frame; the turn hands back after <see cref="DiceOutcomeSpotlight.titleHold"/>.</summary>
    private void SpotlightTitle()
    {
        if (!TurnActive || pendingResult == null) return;

        if (!spotlight.titleText || string.IsNullOrEmpty(turnSpotlightTitle))
        {
            CompleteTurn();
            return;
        }

        spotlight.titleText.text = turnSpotlightTitle;
        ShowFrame(spotlight.TitleRoot, spotlight.titleText, spotlight.Root);
        if (spotlight.titleSfx && SFXManager.Instance) SFXManager.Instance.PlayClip(spotlight.titleSfx, spotlight.titleVolume);
        AfterDelay(spotlight.titleHold, manager => manager.CompleteTurn());
    }

    private Tween spotlightTween;

    /// <summary>Shows the outcome frame for <paramref name="outcome"/> (with its sfx), instantly.</summary>
    public void ShowSpotlight(DiceOutcome outcome)
    {
        TextMeshProUGUI text = spotlight.text;
        if (!text) return;

        DiceOutcomeLook look = spotlight.For(outcome);
        text.text = look.text;
        text.color = look.color;
        ShowFrame(spotlight.Root, text, spotlight.TitleRoot);

        if (look.sfx && SFXManager.Instance) SFXManager.Instance.PlayClip(look.sfx, look.volume);
        onSpotlight?.Invoke(outcome);
    }

    /// <summary>
    /// Hard cut to <paramref name="frame"/> (back at full alpha), <paramref name="other"/> off - unless
    /// <paramref name="text"/> lives on or inside it (one frame or one text for both): then it's up too.
    /// </summary>
    private void ShowFrame(GameObject frame, TextMeshProUGUI text, GameObject other)
    {
        spotlightTween.Stop();
        if (other)
        {
            if (text && text.transform.IsChildOf(other.transform)) ShowObject(other);
            else other.SetActive(false);
        }
        if (frame) ShowObject(frame);
    }

    private static void ShowObject(GameObject target)
    {
        if (target.TryGetComponent(out CanvasGroup group)) group.alpha = 1f;
        target.SetActive(true);
    }

    /// <summary>Takes the spotlight frame away: over <see cref="DiceOutcomeSpotlight.fadeOut"/>, or at once.</summary>
    public void HideSpotlight(bool instant = false)
    {
        spotlightTween.Stop();
        GameObject outcomeFrame = spotlight.Root;
        GameObject titleFrame = spotlight.TitleRoot;
        GameObject shown = titleFrame && titleFrame.activeSelf ? titleFrame
            : outcomeFrame && outcomeFrame.activeSelf ? outcomeFrame : null;

        if (!shown || instant || spotlight.fadeOut <= 0f)
        {
            if (outcomeFrame) outcomeFrame.SetActive(false);
            if (titleFrame) titleFrame.SetActive(false);
            return;
        }

        // An outcome frame inside the shown one (or the same text) fades with it.
        if (outcomeFrame && !outcomeFrame.transform.IsChildOf(shown.transform)) outcomeFrame.SetActive(false);
        // The frame fades as one (text and background together).
        if (!shown.TryGetComponent(out CanvasGroup group)) group = shown.AddComponent<CanvasGroup>();
        // Unscaled, like the round end's fades around it.
        spotlightTween = Tween.Alpha(group, 0f, spotlight.fadeOut, useUnscaledTime: true)
            .OnComplete(shown, s => s.SetActive(false));
    }

    /// <summary>Hides the table of a turn started with keepTableUp (see <see cref="StartTurn"/>), once it's over.</summary>
    public void HideTable()
    {
        if (!TurnActive && hideTableOnFinish && tableRoot) tableRoot.SetActive(false);
    }

    /// <summary>
    /// Banks the finished round's points and opens the turn's next round: the dice keep their faces and are
    /// thrown again after <see cref="nextRoundRollDelay"/>, once its Round popup has shown.
    /// </summary>
    private void NextRound()
    {
        if (!TurnActive) return;

        int roundScore = roundResults[roundResults.Count - 1].score.total;
        BankedScore += roundScore;
        onRoundFinished?.Invoke(roundScore);

        // The intro keeps the Roll / Finish buttons off until the dice are thrown.
        PrepareRound();
        Popup(string.Format(popups.roundStartFormat, CurrentRound, RoundsInTurn), CombinationAnchor,
            popups.combinationOffset, popups.roundStartColor);
        AfterDelay(nextRoundRollDelay, manager => manager.ThrowNextRound());
    }

    private void ThrowNextRound()
    {
        if (!TurnActive) return;
        introRunning = false;
        Roll();
    }

    /// <summary>
    /// Debug: ends the turn on the spot, whatever the dice show and whichever round is on - lost (under the
    /// target), passed (the target, not the risky goal) or passed with the risky goal (upgrades the next cards).
    /// </summary>
    public void DebugEndTurn(bool passed, bool risky)
    {
        if (!TurnActive || pendingResult != null) return;
        if (runningTutorial)
        {
            h.Out("DiceManager: the tutorial is playing - the debug keys work once it's over.");
            return;
        }

        finishTween.Stop();
        firstRollTween.Stop();
        foreach (Dice die in dice)
            if (die) die.StopRoll();
        landingPending = 0;
        introRunning = false;
        finishing = true;

        // A round being banked (between rounds) is already listed; otherwise the dice on the table are the round.
        if (roundResults.Count < CurrentRound)
        {
            int[] values = Values();
            roundResults.Add(new DiceRoundResult { score = RoundScore(values), values = values });
        }

        int scored = 0;
        foreach (DiceRoundResult round in roundResults) scored += round.score.total;

        risky &= passed;
        int total = risky ? Mathf.Max(Target, RiskyTarget)
            : passed ? Target
            : Mathf.Clamp(scored, 0, Mathf.Max(0, Target - 1));
        h.Out($"DiceManager: debug {(risky ? "risky win" : passed ? "win" : "loss")} with {total} (target {Target}, risky {RiskyTarget}).");
        EndTurn(total, passed, risky);
    }

    private void Update()
    {
        if (!TurnActive || !DebugHotkeys.Allowed(debugKeys)) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (Pressed(keyboard, debugLoseKey)) DebugEndTurn(passed: false, risky: false);
        else if (Pressed(keyboard, debugWinKey)) DebugEndTurn(passed: true, risky: false);
        else if (Pressed(keyboard, debugRiskyWinKey)) DebugEndTurn(passed: true, risky: true);
    }

    private static bool Pressed(Keyboard keyboard, Key key) => key != Key.None && keyboard[key].wasPressedThisFrame;

    /// <summary>Drops the turn without a result (the song ended mid-turn).</summary>
    public void CancelTurn()
    {
        if (!TurnActive) return;
        HideDieInfo();

        finishTween.Stop();
        firstRollTween.Stop();
        turnId++;
        introRunning = false;
        StopTutorial();
        foreach (Dice die in dice)
            if (die) die.StopRoll();

        TurnActive = false;
        finishing = false;
        landingPending = 0;
        turnCallback = null;
        pendingResult = null;
        roundResults.Clear();
        StopPopups();
        HideSpotlight(instant: true);
        if (tableRoot) tableRoot.SetActive(false);
    }

    private void OnDieLanded(Dice die)
    {
        PlaySfx(landSfx);
        landingPending = Mathf.Max(0, landingPending - 1);
        if (landingPending > 0) return;

        if (thrownCanBust && !MakesHand(ValuesOf(thrown), savesFromBust: true))
        {
            Bust();
            return;
        }

        DiceScore before = shownScore;
        UpdateUI();
        ShowRollPopups(before, shownScore);
        onRolled?.Invoke();

        // A card's right click could still change the dice: the player finishes themselves.
        if (autoFinishWhenOutOfRerolls && !HasRerollsLeft && !AnyDieAction()) Finish();
    }

    /// <summary>
    /// The rolled dice made no combination: the round's points are lost - it scores 0 - and it ends at once,
    /// as if finished.
    /// </summary>
    private void Bust()
    {
        finishing = true;

        int[] values = Values();
        CombinationScore nothing = ScoreFor(PokerDiceCombination.Nothing);
        DiceScore lost = new DiceScore
        {
            combination = PokerDiceCombination.Nothing,
            combinationName = nothing != null && !string.IsNullOrEmpty(nothing.displayName) ? nothing.displayName : "Nothing",
        };
        roundResults.Add(new DiceRoundResult { score = lost, values = values });

        DiceScore before = shownScore;
        UpdateUI();
        ShowRollPopups(before, shownScore);
        Popup(popups.bustText, CombinationAnchor, popups.combinationOffset, popups.lossColor, bad: true);
        PlaySfx(bustSfx);
        onRolled?.Invoke();

        AfterDelay(roundScoreAlertDelay, manager => manager.RoundScored());
    }

    private static int[] ValuesOf(List<Dice> some)
    {
        List<int> values = new List<int>();
        foreach (Dice die in some)
            if (InPlay(die)) values.Add(die.Value);
        return values.ToArray();
    }

    private void CompleteTurn()
    {
        HideDieInfo();
        DiceTurnResult result = pendingResult;
        Action<DiceTurnResult> callback = turnCallback;

        pendingResult = null;
        turnCallback = null;
        TurnActive = false;
        finishing = false;
        // Kept up: the caller hides it once the screen is covered.
        if (hideTableOnFinish && tableRoot && !turnKeepsTableUp) tableRoot.SetActive(false);

        onRoundFinished?.Invoke(result.rounds[result.rounds.Count - 1].score.total);
        onTurnFinished?.Invoke(result.total);
        callback?.Invoke(result);
    }

    // ---------------------------------------------------------------------
    // Scoring
    // ---------------------------------------------------------------------

    public int[] Values()
    {
        List<int> values = new List<int>();
        foreach (Dice die in DiceInPlay) values.Add(die.Value);
        return values.ToArray();
    }

    /// <summary>
    /// How strong a hand is, 0..1: its points in the scoring table against the best hand there. Card bonuses
    /// don't count - a pair stays a pair.
    /// </summary>
    public float CombinationStrength(PokerDiceCombination combination)
    {
        int best = 0;
        foreach (CombinationScore entry in combinations)
            if (entry != null && entry.enabled) best = Mathf.Max(best, entry.points);

        CombinationScore score = ScoreFor(combination);
        return best > 0 && score != null ? Mathf.Clamp01((float)score.points / best) : 0f;
    }

    /// <summary>Points for a combination in the scoring table (null when it isn't listed or is turned off).</summary>
    public CombinationScore ScoreFor(PokerDiceCombination combination)
    {
        foreach (CombinationScore entry in combinations)
            if (entry != null && entry.enabled && entry.combination == combination) return entry;
        return null;
    }

    /// <summary>
    /// The best score <paramref name="values"/> can make: every hand they contain is scored with the cards'
    /// bonuses and the highest total wins. A score is points x mult. Points start at the hand's points (+ the dice
    /// sum), mult at 1 + <paramref name="extraMult"/> (gained this round, e.g. by destroying dice). Then, over all
    /// cards that count: + points, x points, + mult, x mult. With <see cref="noPointsWithoutCombination"/>, dice
    /// that make no hand score 0.
    /// </summary>
    public DiceScore Evaluate(IReadOnlyList<int> values, IReadOnlyList<CardInstance> ownedCards, float extraMult = 0f)
    {
        int diceSum = 0;
        foreach (int v in values) diceSum += v;

        DiceScore best = null;
        foreach (PokerDiceCombination hand in PokerDiceEvaluator.Contained(values, StraightGapsFor(ownedCards)))
        {
            // No hand, no points: the dice only score through a combination.
            if (noPointsWithoutCombination && hand == PokerDiceCombination.Nothing) continue;

            CombinationScore entry = ScoreFor(hand);
            if (entry == null) continue;

            float addedPoints = 0f, pointsFactor = 1f, addedMult = 0f, multFactor = 1f;
            int nextRoundRerolls = 0;

            if (ownedCards != null)
                foreach (CardInstance card in ownedCards)
                {
                    CardEffect effect = card?.Effect;
                    if (effect == null || !effect.IsScoring) continue;

                    // Face Value / Parity cards count once per matching die.
                    int times = effect.TimesIn(values, hand);
                    if (times <= 0) continue;
                    nextRoundRerolls += effect.nextRoundRerolls;

                    // The card's own amount / multiplier: an upgraded card's are higher.
                    switch (effect.operation)
                    {
                        case ScoreOperation.AddPoints: addedPoints += card.Amount * times; break;
                        case ScoreOperation.MultiplyPoints: pointsFactor *= Mathf.Pow(card.Multiplier, times); break;
                        case ScoreOperation.AddMult: addedMult += card.Multiplier * times; break;
                        case ScoreOperation.MultiplyMult: multFactor *= Mathf.Pow(card.Multiplier, times); break;
                    }
                }

            int points = Mathf.RoundToInt((entry.points + (addDiceSum ? diceSum : 0) + addedPoints) * pointsFactor);
            float mult = (1f + extraMult + addedMult) * multFactor;
            // Single One only saves from a bust: without a combination there are no points to make.
            if (noPointsWithoutCombination && hand == PokerDiceCombination.SingleOne) points = 0;
            int total = Mathf.RoundToInt(points * mult);

            if (best != null && (total < best.total ||
                                 (total == best.total && PokerDiceEvaluator.Rank(hand) < PokerDiceEvaluator.Rank(best.combination))))
                continue;

            best = new DiceScore
            {
                combination = hand,
                combinationName = string.IsNullOrEmpty(entry.displayName) ? CardEffect.Nicify(hand) : entry.displayName,
                points = points,
                mult = mult,
                total = total,
                nextRoundRerolls = nextRoundRerolls,
            };
        }

        if (best != null) return best;

        CombinationScore nothing = ScoreFor(PokerDiceCombination.Nothing);
        return new DiceScore
        {
            combination = PokerDiceCombination.Nothing,
            combinationName = nothing != null && !string.IsNullOrEmpty(nothing.displayName) ? nothing.displayName : "Nothing",
        };
    }

    /// <summary>
    /// What a finished round banks: <see cref="Evaluate"/>, but 0 points when the best hand doesn't count as a
    /// combination (<see cref="CombinationScore.countsAsCombination"/>).
    /// </summary>
    private DiceScore RoundScore(IReadOnlyList<int> values)
    {
        DiceScore score = Evaluate(values, cards, roundMult);
        CombinationScore entry = ScoreFor(score.combination);
        if (entry == null || entry.countsAsCombination) return score;

        score.points = 0;
        score.total = 0;
        score.nextRoundRerolls = 0;
        return score;
    }

    /// <summary>Whether <paramref name="hand"/> is a combination that counts: not Nothing, enabled and counted.</summary>
    private bool CountsAsCombination(PokerDiceCombination hand)
    {
        if (hand == PokerDiceCombination.Nothing) return false;
        CombinationScore entry = ScoreFor(hand);
        return entry != null && entry.countsAsCombination;
    }

    /// <summary>A round's rerolls: <see cref="rerollsPerRound"/> + Extra Rerolls cards - Extra Dice cards' cost.</summary>
    private int RerollsFor(IReadOnlyList<CardInstance> ownedCards)
    {
        int rerolls = rerollsPerRound;
        foreach (CardInstance card in ownedCards)
        {
            CardEffect effect = card?.Effect;
            if (effect == null) continue;
            if (effect.type == CardEffectType.ExtraRerolls) rerolls += Mathf.Max(0, card.Amount);
            else if (effect.type == CardEffectType.ExtraDice) rerolls -= Mathf.Max(0, effect.rerollCost);
        }
        return Mathf.Max(0, rerolls);
    }

    /// <summary>Missing faces a straight may skip: the most any owned Gap Straight card allows.</summary>
    private static int StraightGapsFor(IReadOnlyList<CardInstance> ownedCards)
    {
        int gaps = 0;
        if (ownedCards == null) return gaps;
        foreach (CardInstance card in ownedCards)
        {
            CardEffect effect = card?.Effect;
            if (effect != null && effect.type == CardEffectType.GapStraight) gaps = Mathf.Max(gaps, card.Amount);
        }
        return gaps;
    }

    /// <summary>Dice on the table this turn: <see cref="startDiceCount"/> + Extra Dice cards.</summary>
    private int DiceCountFor(IReadOnlyList<CardInstance> ownedCards)
    {
        int count = startDiceCount;
        foreach (CardInstance card in ownedCards)
        {
            CardEffect effect = card?.Effect;
            if (effect != null && effect.type == CardEffectType.ExtraDice) count += Mathf.Max(0, card.Amount);
        }
        return Mathf.Max(1, count);
    }

    // ---------------------------------------------------------------------
    // UI / audio
    // ---------------------------------------------------------------------

    private void UpdateUI()
    {
        bool anyUnlocked = false;
        foreach (Dice die in DiceInPlay)
            if (!die.IsLocked) anyUnlocked = true;

        bool idle = TurnActive && !finishing && !IsRolling && !introRunning;

        if (rollButton) rollButton.interactable = idle && (!HasRolled || (HasRerollsLeft && anyUnlocked));
        if (rollButtonLabel)
            rollButtonLabel.text = !HasRolled ? firstRollLabel
                : unlimitedRerolls ? unlimitedRerollLabel : string.Format(rerollLabel, RerollsLeft);
        // Never into the Finish button's own label: that one always says finishLabel.
        if (rerollsText && rerollsText != finishButtonLabel)
        {
            // Nothing to count with unlimited rerolls.
            rerollsText.gameObject.SetActive(!unlimitedRerolls);
            rerollsText.text = string.Format(rerollsFormat, RerollsLeft);
        }
        if (finishButtonLabel) finishButtonLabel.text = finishLabel;
        if (riskyTargetText) riskyTargetText.text = string.Format(riskyTargetFormat, RiskyTarget, Target);
        if (roundText)
        {
            roundText.gameObject.SetActive(RoundsInTurn > 1);
            roundText.text = string.Format(roundFormat, Mathf.Max(1, CurrentRound), RoundsInTurn);
        }

        // The round's score is what the dice on the table make right now - never added up over rolls. While
        // dice are in the air the last one stays up; once they land it counts from that to the new score
        // (10 -> 20, or down). On screen it sits on top of the rounds already banked this turn; the turn's
        // first round starts at 0 and counts up from there.
        // A scored round shows what it banked: a hand that doesn't count drops to 0 as the round ends.
        if (!TurnActive || !HasRolled) shownScore = null;
        else if (finishing && roundResults.Count >= CurrentRound) shownScore = roundResults[roundResults.Count - 1].score;
        else if (!IsRolling) shownScore = Evaluate(Values(), cards, roundMult);

        UpdateTargetText();

        // The round can be finished any time once the dice are down - target reached or not.
        if (finishButton) finishButton.interactable = idle && HasRolled;

        // One round: the score is the turn's total. Several: it is what the finished rounds banked (the round
        // being scored joins it as it finishes), and the running round's points get their own label.
        bool multiRound = RoundsInTurn > 1;
        CountingText counter = ScoreCounter;
        if (counter)
        {
            if (!multiRound)
            {
                if (shownScore == null && CurrentRound <= 1) counter.SetInstant(0);
                else counter.SetValue(TurnTotal(shownScore));
            }
            else
            {
                int accumulated = BankedScore + (finishing ? shownScore?.total ?? 0 : 0);
                if (CurrentRound <= 1 && !HasRolled) counter.SetInstant(0);
                else counter.SetValue(accumulated);

                // Red until the points banked over the rounds reach the minimum goal.
                scoreColor ??= counter.BaseColor;
                counter.BaseColor = Target <= 0 || accumulated >= Target ? scoreColor.Value : scoreBelowTargetColor;
            }
        }

        if (roundScoreText)
        {
            roundScoreText.gameObject.SetActive(TurnActive && multiRound);
            CountingText roundCounter = RoundScoreCounter;
            if (roundCounter)
            {
                // A fresh round starts at 0; then it counts to what the dice make.
                if (shownScore == null) roundCounter.SetInstant(0);
                else roundCounter.SetValue(shownScore.total);
            }
        }

        if (combinationText)
        {
            // No hand that counts (a disabled hand falls back to Nothing): the text stays empty.
            bool proper = shownScore != null && shownScore.combination != PokerDiceCombination.Nothing;
            combinationText.text = proper ? shownScore.combinationName : "";
        }
        SetCombinationShake(shownScore != null ? CombinationStrength(shownScore.combination) : 0f);
        UpdateComboScoreText();

        // Whether a die may be locked just changed (a roll started or landed): hovered dice follow.
        foreach (Dice die in dice)
            if (die) die.RefreshOutline();
    }

    /// <summary>
    /// Combo Score Text: the running round's mult and points ("2.5 x 34"), punched when either changes. While dice
    /// are in the air the last score stays up.
    /// </summary>
    private void UpdateComboScoreText()
    {
        if (!comboScoreText) return;

        string text = shownScore == null ? ""
            : string.Format(comboScoreFormat, CardEffect.FormatMult(shownScore.mult), shownScore.points, shownScore.total);
        if (text == comboScoreText.text) return;
        comboScoreText.text = text;

        if (comboScorePunch > 0f && !string.IsNullOrEmpty(text) && isActiveAndEnabled)
        {
            comboPunchTween.Stop();
            comboScoreText.transform.localScale = comboTextScale ??= comboScoreText.transform.localScale;
            comboPunchTween = Tween.PunchScale(comboScoreText.transform, Vector3.one * comboScorePunch, 0.25f);
        }
    }

    /// <summary>The turn's total with <paramref name="roundScore"/> as the running round's: banked rounds + it.</summary>
    private int TurnTotal(DiceScore roundScore) => BankedScore + (roundScore?.total ?? 0);

    /// <summary>
    /// The target text shows the target until the turn's total reaches it, then the risky goal - with a
    /// punch as it switches. Back to the target if a reroll drops the total under it.
    /// </summary>
    /// <summary>The turn's total on screen has reached <see cref="Target"/> (always true without a target).</summary>
    private bool TargetReached => Target <= 0 || ((shownScore != null || CurrentRound > 1) && TurnTotal(shownScore) >= Target);

    private void UpdateTargetText()
    {
        bool reached = Target > 0 && TargetReached;
        bool switched = reached && !targetShowsRisky;
        targetShowsRisky = reached;

        if (!targetText) return;
        targetText.text = string.Format(reached ? reachedTargetFormat : targetFormat,
            reached ? RiskyTarget : Target, reached ? Target : RiskyTarget);

        if (switched && targetSwitchPunch > 0f)
        {
            targetPunchTween.Stop();
            targetText.transform.localScale = targetTextScale ??= targetText.transform.localScale;
            targetPunchTween = Tween.PunchScale(targetText.transform, Vector3.one * targetSwitchPunch, 0.3f);
        }
    }

    // ---------------------------------------------------------------------
    // Popups
    // ---------------------------------------------------------------------

    // Empty anchors pop up at the dice parent (its pivot - the middle with the default centred pivot).
    private Transform ScoreAnchor => popups.scoreAnchor ? popups.scoreAnchor : Area;
    private Transform CombinationAnchor => popups.combinationAnchor ? popups.combinationAnchor : Area;
    private Transform GoalAnchor => popups.goalAnchor ? popups.goalAnchor : Area;

    /// <summary>
    /// After a landing: how much the score changed (+10 / -5), the hand the dice make, and a goal reached
    /// (the turn's total climbing past the target or the risky goal).
    /// </summary>
    private void ShowRollPopups(DiceScore before, DiceScore after)
    {
        if (!popups.enabled || after == null) return;

        int oldTotal = TurnTotal(before);
        int newTotal = TurnTotal(after);
        int change = newTotal - oldTotal;
        if (change != 0)
            Popup(string.Format(change > 0 ? popups.gainFormat : popups.lossFormat, Mathf.Abs(change)), ScoreAnchor,
                popups.scoreOffset, change > 0 ? popups.gainColor : popups.lossColor, bad: change < 0);

        if (after.combination != PokerDiceCombination.Nothing)
            Popup(after.combinationName, CombinationAnchor, popups.combinationOffset, popups.combinationColor);

        if (Target > 0 && oldTotal < Target && newTotal >= Target)
            Popup(popups.goalReachedText, GoalAnchor, popups.goalOffset, popups.goalColor);
        if (RiskyTarget > 0 && oldTotal < RiskyTarget && newTotal >= RiskyTarget)
            Popup(popups.riskyGoalReachedText, GoalAnchor, popups.goalOffset, popups.riskyGoalColor,
                popups.riskyGoalScale, popups.riskyGoalExtraHold);
    }

    /// <summary>
    /// Pops <paramref name="text"/> up at <paramref name="anchor"/> + <paramref name="offset"/> (canvas units),
    /// <see cref="DicePopups.interval"/> after the previous popup, <paramref name="scale"/> times its usual size and
    /// held <paramref name="extraHold"/> seconds longer. It plays <see cref="badAlertSfx"/> as it appears when
    /// <paramref name="bad"/>, else <see cref="goodAlertSfx"/>.
    /// </summary>
    private void Popup(string text, Transform anchor, Vector2 offset, Color color, float scale = 1f, float extraHold = 0f,
        bool bad = false)
    {
        AudioClip sfx = bad ? badAlertSfx : goodAlertSfx;
        if (!popups.enabled || string.IsNullOrEmpty(text)) return;
        if (!TextAlertManager.Instance)
        {
            h.Out($"DiceManager: no TextAlertManager for the popup '{text}'.");
            return;
        }
        if (!anchor) anchor = tableRoot ? tableRoot.transform : transform;

        float delay = Mathf.Max(0f, nextPopupTime - Time.time);
        nextPopupTime = Time.time + delay + popups.interval;

        if (delay <= 0f)
        {
            CreatePopup(text, anchor, offset, color, scale, extraHold, sfx);
            return;
        }

        popupTweens.RemoveAll(tween => !tween.isAlive);
        popupTweens.Add(Tween.Delay(anchor, delay, a =>
        {
            if (TextAlertManager.Instance) CreatePopup(text, a, offset, color, scale, extraHold, sfx);
        }));
    }

    private void CreatePopup(string text, Transform anchor, Vector2 offset, Color color, float scale, float extraHold,
        AudioClip sfx)
    {
        // The scale goes in with it, so the alerts already up make room for its real size.
        AlertDamageText alert = TextAlertManager.Instance.CreateTextAlert(text, anchor, color, offset, scale);
        if (!alert) return;
        PlaySfx(sfx);
        // Before the alert's Start, which reads the hold to schedule its fade.
        alert.holdDuration += extraHold;
    }

    private void StopPopups()
    {
        foreach (Tween tween in popupTweens) tween.Stop();
        popupTweens.Clear();
        nextPopupTime = 0f;
    }

    /// <summary>Plays <paramref name="clip"/> at a random pitch from <see cref="sfxPitchRange"/>, so repeats don't sound identical.</summary>
    private void PlaySfx(AudioClip clip)
    {
        if (clip && SFXManager.Instance) SFXManager.Instance.PlayClip(clip, randomPitchRange: sfxPitchRange);
    }
}
