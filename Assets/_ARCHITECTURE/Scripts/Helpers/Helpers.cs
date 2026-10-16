using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using M = System.Math;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using EZCameraShake;
using Unity.VisualScripting;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public static class h
{
    // Math
    /// <summary>
    /// Returns the sign of the integer: 1 for positive, -1 for negative, 0 for zero.
    /// </summary>
    public static int Sign(int n)
    {
        if (n > 0) return 1;
        if (n < 0) return -1;
        return 0;
    }
    /// <summary>
    /// Returns the sign of the float: 1 for positive, -1 for negative, 0 for zero.
    /// </summary>
    public static float Sign(float n)
    {
        if (n > 0) return 1;
        if (n < 0) return -1;
        return 0;
    }
    
    /// <summary>
    /// Returns the maximum value from the provided float array.
    /// </summary>
    public static float Max(params float[] args)
    {
        
        float result=args[0];
        for (int i = 1; i<args.Length; i++)
        {
            result = M.Max(result, args[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns the maximum value from the provided int array.
    /// </summary>
    public static int Max(params int[] args)
    {
        int result=args[0];
        for (int i = 1; i<args.Length; i++)
        {
            result = M.Max(result, args[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns the maximum value from the provided float list.
    /// </summary>
    public static float Max(List<float> args)
    {
        float result=args[0];
        for (int i = 1; i<args.Count; i++)
        {
            result = M.Max(result, args[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns the maximum value from the provided int list.
    /// </summary>
    public static int Max(List<int> args)
    {
        int result=args[0];
        for (int i = 1; i<args.Count; i++)
        {
            result = M.Max(result, args[i]);
        }
        return result;
    }
    
    /// <summary>
    /// Returns the sum of all integers in the provided list.
    /// </summary>
    public static int Sum(List<int> a)
    {
        int s = 0;
        for(int i = 0; i<a.Count; i++)
        {
            s += a[i];
        }
        return s;
    }

    /// <summary>
    /// Returns the sum of all floats in the provided list.
    /// </summary>
    public static float Sum(List<float> a)
    {
        float s = 0;
        for(int i = 0; i<a.Count; i++)
        {
            s += a[i];
        }
        return s;
    }

    /// <summary>
    /// Returns the minimum value from the provided float array.
    /// </summary>
    public static float Min(params float[] args)
    {
        float result=args[0];
        for (int i = 1; i<args.Length; i++)
        {
            result = M.Min(result, args[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns the minimum value from the provided int array.
    /// </summary>
    public static int Min(params int[] args)
    {
        int result=args[0];
        for (int i = 1; i<args.Length; i++)
        {
            result = M.Min(result, args[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns the minimum value from the provided float list.
    /// </summary>
    public static float Min(List<float> args)
    {
        float result=args[0];
        for (int i = 1; i<args.Count; i++)
        {
            result = M.Min(result, args[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns the minimum value from the provided int list.
    /// </summary>
    public static int Min(List<int> args)
    {
        int result=args[0];
        for (int i = 1; i<args.Count; i++)
        {
            result = M.Min(result, args[i]);
        }
        return result;
    }

    /// <summary>
    /// Calculates the area (width multiplied by height) of a Vector2.
    /// </summary>
    /// <param name="v2">The Vector2 to calculate the area for.</param>
    /// <returns>The product of the x and y components.</returns>
    public static float Area(Vector2 v2)
    {
        return v2.x * v2.y;
    }
    
    // OUTPUT & DEBUG
    /// <summary>
    /// Logs the string representations of the provided objects to the console, separated by semicolons.
    /// </summary>
    public static void Out(params object[] args)
    {
        string result = "";

        foreach (var arg in args)
        {
            result += Str(arg) + "; ";
        }

        Debug.Log(result);
    }
    /// <summary>
    /// Converts a value to its string representation, supporting custom formatting for various types.
    /// </summary>
    /// <param name="value">The value to convert to a string.</param>
    /// <returns>A formatted string representation of the value.</returns>
    public static string Str<T>(T value)
    {
        if (value == null) return "null";
        string result = "";
        List<System.Type> defaultTypes = new List<System.Type>()
        {
            typeof(int), typeof(string), typeof(float), typeof(bool)
        };
        System.Type type = typeof(T);
        if (defaultTypes.Contains(type))
        {
            result = value.ToString();
        } else if (value is Vector2 v2)
        {
            result = $"x: {v2.x}, y: {v2.y}";
        } else if (value is Vector3 v3)
        {
            result = $"x: {v3.x}, y: {v3.y}, z: {v3.z}";
        } else if (value is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                result += $"{Str(entry.Key)}: {Str(entry.Value)}\n";
            }
        } else if (value is IList list)
        {
            foreach (var item in list)
            {
                result += $"{Str(item)}, ";
            }
        } else if (value is ObjectsCountContainer container)
        {
            result = Str(container.values);
        }
        else result = value.ToString();

        return result;
    }

    /// <summary>
    /// Logs a single value to the console using its string representation.
    /// </summary>
    /// <param name="value">The value to log.</param>
    public static void Out<T> (T value){
        Debug.Log(Str(value));
    }

    /// <summary>
    /// Logs all elements of a list to the console, each on a new line.
    /// </summary>
    /// <param name="data">The list to log.</param>
    public static void Out<T>(List<T> data)
    {
        Debug.Log(string.Join("\n", data));
    }

    /// <summary>
    /// Logs all key-value pairs from a dictionary to the console, each on a new line.
    /// </summary>
    /// <param name="data">The dictionary to log.</param>
    public static void Out<K, V>(Dictionary<K, V> data)
    {
        if (data == null || data.Count == 0)
        {
            Debug.Log("(empty dictionary)");
            return;
        }
        Debug.Log(string.Join("\n", data.Select(p => $"{p.Key}: {p.Value}")));
    }

    /// <summary>
    /// Logs a Vector2's x and y components to the console.
    /// </summary>
    /// <param name="v">The Vector2 to log.</param>
    public static void Out(Vector2 v)
    {
        Debug.Log($"x: {v.x}, y: {v.y}");
    }

    /// <summary>
    /// Logs a Vector3's x, y, and z components to the console.
    /// </summary>
    /// <param name="v">The Vector3 to log.</param>
    public static void Out(Vector3 v)
    {
        Debug.Log($"x: {v.x}, y: {v.y}, z: {v.z}");
    }


    // RANDOM
    /// <summary>
    /// Returns a random integer between a (inclusive) and b (exclusive).
    /// </summary>
    public static int Range(int a, int b)
    {
        return Random.Range(a, b);
    }

    /// <summary>
    /// Returns a random float between a (inclusive) and b (inclusive).
    /// </summary>
    public static float Range(float a, float b)
    {
        return Random.Range(a, b);
    }

    /// <summary>
    /// Returns a random float within range a, using coefficient c as variance (±c%).
    /// </summary>
    /// <summary>
    /// Returns a random float within range a, using coefficient c as variance (±c%).
    /// </summary>
    /// <param name="a">The base value.</param>
    /// <param name="c">The variance coefficient as a percentage.</param>
    /// <returns>A random float between a*(1-c) and a*(1+c).</returns>
    public static float RangeWithCoef(float a, float c)
    {
        return h.Range(a * (1 - c), a * (1 + c));
    }

    /// <summary>
    /// Returns a random float within range (a - c) to (a + c).
    /// </summary>
    /// <param name="a">The center value.</param>
    /// <param name="c">The distribution range.</param>
    /// <returns>A random float between a-c and a+c.</returns>
    public static float RangeWithDistribution(float a, float c)
    {
        return h.Range(a - c, a + c);
    }

    /// <summary>
    /// Returns a random integer within range (a - c) to (a + c).
    /// </summary>
    /// <param name="a">The center value.</param>
    /// <param name="c">The distribution range.</param>
    /// <returns>A random integer between a-c and a+c.</returns>
    public static int RangeWithDistribution(int a, int c)
    {
        return h.Range(a - c, a + c);
    }

    /// <summary>
    /// Returns a random float between -a and a.
    /// </summary>
    public static float Range(float a)
    {
        return Range(-a, a);
    }

    /// <summary>
    /// Returns a random int between -a and a.
    /// </summary>
    public static float Range(int a)
    {
        return Range(-a, a);
    }

    /// <summary>
    /// Returns a random multiplicator value in range (1 ± range), for variance effects like 0.9 to 1.1.
    /// </summary>
    public static float RandMult(float range)
    {
        /// returns random multiplicator exmp: from 0.9 to 1.1
        return 1 + Range(range);
    }

    /// <summary>
    /// Returns true if a random value is less than or equal to the specified chance probability.
    /// </summary>
    public static bool RandChance(float chance)
    {
        return Random.value <= chance;
    }

    /// <summary>
    /// Returns a random element from a list.
    /// </summary>
    public static T RandChoice<T>(List<T> list)
    {
        if (list == null || list.Count == 0) return default;
        return list[Random.Range(0, list.Count)];
    }
    
    /// <summary>
    /// Returns a random element from the provided arguments.
    /// </summary>
    /// <param name="args">Variable number of items to choose from.</param>
    /// <returns>A random item from the arguments, or default if no arguments provided.</returns>
    public static T RandChoice<T>(params T[] args)
    {
        if (args == null || args.Length == 0) return default;
        return args[Random.Range(0, args.Length)];
    }
    
    /// <summary>
    /// Returns a random value from a dictionary's values based on random key selection.
    /// </summary>
    public static K RandChoice<T, K>(Dictionary<T, K> data)
    {
        return data[RandChoice(data.Keys.ToList())];
    }
    
    /// <summary>
    /// Selects a random element from a list based on weighted distribution values.
    /// </summary>
    public static T RandomChoiceWithDistribution<T>(List<T> objects, List<float> weights)
    {
        if (objects.Count == 0) return default;
        
        for (int i = 0; i<objects.Count-weights.Count; i++)
        {
            weights.Add(1);
        }

        float totalWeight = Sum(weights);
        float randomValue = Random.value * totalWeight;
        float cumulative = 0f;

        for (int i = 0; i < objects.Count; i++)
        {
            cumulative += weights[i];

            if (randomValue < cumulative)
                return objects[i];
        }

        return objects[objects.Count - 1];
    }

    /// <summary>
    /// Selects a random value from a dictionary based on weighted distribution.
    /// </summary>
    public static T RandomChoiceWithDistribution<T>(Dictionary<T, float> data)
    {
        return RandomChoiceWithDistribution(data.Keys.ToList(), data.Values.ToList());
    }

    // ==================== GEOMETRY ====================

    /// <summary>
    /// Returns a random normalized direction vector in 2D space.
    /// </summary>
    /// <returns>A random normalized Vector2.</returns>
    public static Vector2 RandomDirection()
    {
        float x = Random.value;
        float y = (float)M.Sqrt(1-x*x);
        int[] choice = {-1, 1};
        x *= RandChoice(choice);
        y *= RandChoice(choice);

        return new Vector2(x, y);
    }

    /// <summary>
    /// Returns a random position within a circle of specified radius, optionally centered at a target transform or position.
    /// </summary>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="targetTransform">The transform to center the circle at (takes precedence over targetPosition).</param>
    /// <param name="targetPosition">The position to center the circle at if targetTransform is null.</param>
    /// <returns>A random position within the specified circle.</returns>
    public static Vector3 RandomPositionInCircle(
        float radius,
        Transform targetTransform=null,
        Vector3 targetPosition=default)
    {
        Vector3 initialPosition = targetTransform != null ? targetTransform.position : targetPosition;
        
        Vector2 dir = h.RandomDirection();
        float randRadius = Random.Range(0, radius);

        float newX = randRadius * dir.x;
        float newY = randRadius * dir.y;
        Vector3 desiredPosition = new Vector3(newX, newY, 0);
        desiredPosition += initialPosition;

        return desiredPosition;
    }

    
    
    // ==================== FIND IN SCENE, PROCESS IN PARENT, INSTANCES ====================
    /// <summary>
    /// Finds the first object of type T in the scene and assigns it to target.
    /// </summary>
    public static void AssignFirstObjectByType<T>(ref T target)
        where T : UnityEngine.Object
    {
        target = GameObject.FindFirstObjectByType<T>();
    }

    /// <summary>
    /// Creates a static instance of an object, destroying duplicates and marking it as DontDestroyOnLoad.
    /// The FIRST caller wins and becomes the instance; every later duplicate destroys itself.
    /// (The old FindObjectsOfType-based check destroyed EVERY copy when two existed in the same
    /// scene, leaving the instance null, so nothing worked at all.)
    /// </summary>
    public static void CreateStaticInstance<T>(T obj, ref T instance, bool setDontDestroy=true)
        where T : UnityEngine.Object
    {
        if (!obj) return;

        // instance != null uses Unity's overloaded operator, so a destroyed leftover counts as null.
        if (instance != null && instance != obj)
        {
            Debug.LogWarning($"CreateStaticInstance: duplicate {typeof(T).Name} on '{obj.name}' destroyed, "
                             + $"'{instance.name}' is already the instance.", obj);
            GameObject.Destroy(obj);
            return;
        }

        // Only ROOT objects can be marked persistent. Calling it on a child does nothing and logs a
        // warning (one managed stack trace each), so skip it — never pass transform.root instead:
        // that would drag the whole ----MANAGERS------- hierarchy across scene loads.
        if (setDontDestroy && !obj.GameObject().transform.parent) GameObject.DontDestroyOnLoad(obj.GameObject());
        instance = obj;
    }
    
    /// <summary>
    /// Returns all direct children of the parent transform as a list.
    /// </summary>
    public static List<Transform> GetAllChildren(Transform parent)
    {
        List<Transform> result = new List<Transform>();
        foreach(Transform child in parent)
        {
            result.Add(child);
        }
        return result;
    }

    /// <summary>
    /// Finds the closest n game objects with the specified tag to a target transform, optionally filtered by minimum range.
    /// </summary>
    public static List<GameObject> FindClosestByTag(string tag, Transform target, int n = 1, float minRange=-1)
    {
        if (!target) return default;
        return OrderByDistance(GameObject.FindGameObjectsWithTag(tag), target, n, minRange);
    }

    /// <summary>
    /// Finds the closest n children of a parent transform to a target, optionally filtered by tags and minimum range.
    /// </summary>
    public static List<GameObject> FindClosestByParent(
        Transform parent,
        Transform target,
        int n = 1,
        float minRange=-1,
        List<string> targetTags=null
        )
    {
        if (!target || !parent) return default;

        GameObject[] children = new GameObject[parent.childCount];
        int i = 0;
        foreach (Transform child in parent)
        {
            if (targetTags != null && targetTags.Contains(child.tag)) continue;
            children[i] = child.gameObject;
            i++;
        }
        return OrderByDistance(children, target, n, minRange);
    }

    /// <summary>
    /// Orders an array of game objects by distance to a transform, returning the n closest, optionally filtered by minimum range.
    /// </summary>
    public static List<GameObject> OrderByDistance(GameObject[] a, Transform transform, int n = 1, float minRange=-1)
    {
        if(a.Length == 0) return default;
        
        List<GameObject> result = new List<GameObject>();
        List<GameObject> ordered = a.Where(obj => obj != null).OrderBy(obj => (obj.transform.position - transform.position).sqrMagnitude).Take(n).ToList(); //.Take(n).ToList()
        
        if (minRange <= 0) return ordered;
        minRange*=minRange;
        foreach(GameObject obj in ordered)
        {
            if ((obj.transform.position - transform.position).sqrMagnitude <= minRange)
            {
                result.Add(obj);
            }
        }
        return result;
    }

    /// <summary>
    /// Recursively searches for a child with the specified tag within a transform hierarchy.
    /// </summary>
    public static GameObject FindChildrenWithTag(Transform parent, string tag)
    {
        try
        {
            GameObject result = null;
            foreach (Transform child in parent)
            {

                if (child.CompareTag(tag)) return child.gameObject;
                result = FindChildrenWithTag(child, tag);
                if (result != null) return result;
            }

            return result;
        }
        catch (Exception e)
        {
            h.Out(e.Message);
            return null;
        }
        
    }
    
    /// <summary>
    /// Returns a random child transform from a randomly selected parent in the provided list.
    /// </summary>
    public static Transform GetRndChildFromParents(List<Transform> parentsOfPossibleTargets)
    {
        return GetRndChild(RandChoice(parentsOfPossibleTargets));
    }

    /// <summary>
    /// Returns a random child transform from the provided parent.
    /// </summary>
    public static Transform GetRndChild(Transform parent)
    {
        return RandChoice(GetAllChildren(parent));
    }

    /// <summary>
    /// Returns the first child transform with the specified tag, or null if not found.
    /// </summary>
    public static Transform GetFirstChildByTag(Transform parent, string tag)
    {
        foreach (Transform child in parent)
        {
            if (child.CompareTag(tag))
            {
                return child;
            }
        }
        return default;
    }

    /// <summary>
    /// Recursively searches for a child with the specified tag within a transform hierarchy.
    /// </summary>
    /// <param name="parent">The parent transform to search within.</param>
    /// <param name="tag">The tag to search for.</param>
    /// <returns>The first child GameObject with the specified tag, or null if not found.</returns>
    public static GameObject FindChildWithTag(Transform parent, string tag)
    {
        foreach (Transform child in parent)
        {
            if (child.CompareTag(tag)) return child.gameObject;
            GameObject result = FindChildWithTag(child, tag);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// Recursively finds all children with the specified tag and returns them as a list.
    /// </summary>
    /// <param name="parent">The parent transform to search within.</param>
    /// <param name="tag">The tag to search for.</param>
    /// <returns>A list of all GameObjects with the specified tag in the hierarchy.</returns>
    public static List<GameObject> FindAllChildrenWithTag(Transform parent, string tag)
    {
        List<GameObject> results = new List<GameObject>();
        FindAllChildrenWithTagRecursive(parent, tag, results);
        return results;
    }

    /// <summary>
    /// Helper method that recursively finds all children with a specified tag and adds them to a list.
    /// </summary>
    /// <param name="parent">The parent transform to search within.</param>
    /// <param name="tag">The tag to search for.</param>
    /// <param name="results">The list to accumulate results in.</param>
    private static void FindAllChildrenWithTagRecursive(Transform parent, string tag, List<GameObject> results)
    {
        foreach (Transform child in parent)
        {
            if (child.CompareTag(tag)) results.Add(child.gameObject);
            FindAllChildrenWithTagRecursive(child, tag, results);
        }
    }

    /// <summary>
    /// Returns a random transform from all objects in the scene with the specified tag.
    /// </summary>
    public static Transform GetRndByTag(string tag)
    {
        return RandChoice(GameObject.FindGameObjectsWithTag(tag)).transform;
    }

    /// <summary>
    /// Switches every scene object carrying <paramref name="tag"/> to <paramref name="active"/>, and
    /// returns the ones that actually changed — so a caller that switched something off can put
    /// exactly that set back on.
    ///
    /// <para>The scene is walked by hand rather than asked with
    /// <c>GameObject.FindGameObjectsWithTag</c>, because that only ever returns ACTIVE objects: the
    /// wrong half entirely whenever something is being switched ON. An undefined tag is reported once
    /// instead of throwing per object.</para>
    /// </summary>
    /// <param name="tag">Tag to look for. Empty or null does nothing.</param>
    /// <param name="active">What to switch the matches to.</param>
    /// <param name="owner">Name used in the log line when the tag doesn't exist. Optional.</param>
    /// <returns>The objects whose active state this call changed. Never null.</returns>
    public static List<GameObject> SetActiveByTag(string tag, bool active, string owner = null)
    {
        List<GameObject> changed = new List<GameObject>();
        if (string.IsNullOrEmpty(tag)) return changed;

        // Probe: FindGameObjectsWithTag throws on a tag that isn't defined in the project, and doing
        // that here costs one call instead of one per object in the scan below. The result itself is
        // useless to us, for the reason in the summary.
        try
        {
            GameObject.FindGameObjectsWithTag(tag);
        }
        catch (UnityException)
        {
            Out($"{(string.IsNullOrEmpty(owner) ? "h.SetActiveByTag" : owner)}: the tag '{tag}' is not " +
                "defined in this project (Edit > Project Settings > Tags and Layers) — nothing was " +
                (active ? "switched on." : "switched off."));
            return changed;
        }

        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Transform t in all)
        {
            if (!t || !t.gameObject.scene.IsValid()) continue; // never touch a prefab asset
            if (t.gameObject.activeSelf == active) continue;
            if (!t.CompareTag(tag)) continue;

            t.gameObject.SetActive(active);
            changed.Add(t.gameObject);
        }

        return changed;
    }


    // ANIMATION
    /// <summary>
    /// Smoothly scales a transform to a target scale over a specified duration using a coroutine.
    /// </summary>
    public static void SmoothScaling(
        MonoBehaviour runner,
        Transform tObj,
        Vector3 tScale,
        float duration) // t - target
    {
        runner.StartCoroutine(SmoothScalingCoroutine(tObj, tScale, duration));
    }

    /// <summary>
    /// Coroutine that smoothly scales a transform to a target scale over a specified duration using linear interpolation.
    /// </summary>
    public static IEnumerator SmoothScalingCoroutine(Transform tObj, Vector3 tScale, float duration)
    {
        if (duration <= 0f)
        {
            tObj.localScale = tScale;
            yield break;
        }

        Vector3 startScale = tObj.localScale;
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            float t = elapsedTime / duration;
            tObj.localScale = Vector3.Lerp(startScale, tScale, t);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (tObj) tObj.localScale = tScale;
    }

    /// <summary>
    /// Smoothly rotates a transform over a specified duration.
    /// </summary>
    /// <param name="runner">The MonoBehaviour to run the coroutine on.</param>
    /// <param name="tObj">The transform to rotate.</param>
    /// <param name="rotatingSpeed">The rotation speed in degrees per second.</param>
    /// <param name="duration">The duration of the rotation in seconds.</param>
    /// <param name="endAngle">The target end angle. If >= 0, calculates start angle based on this.</param>
    /// <param name="reverse">If true, reverses the rotation direction.</param>
    /// <param name="acceleration">Optional acceleration factor. Positive for acceleration, negative for deceleration. 0 for constant speed.</param>
    public static void SmoothRotating(
        MonoBehaviour runner,
        Transform tObj,
        float rotatingSpeed,
        float duration,
        float endAngle = -1f,
        bool reverse = false,
        float acceleration = 0f
        )
    {
        if (reverse) rotatingSpeed *= -1;

        if (endAngle >= 0f)
        {
            float startAngle = endAngle - rotatingSpeed * duration;
            startAngle = Mathf.Repeat(startAngle, 360f); // % 360

            tObj.eulerAngles = new Vector3(
                tObj.eulerAngles.x,
                tObj.eulerAngles.y,
                startAngle
            );
        }

        runner.StartCoroutine(
            SmoothRotatingCoroutine(tObj, rotatingSpeed, duration, acceleration)
        );
    }

    /// <summary>
    /// Coroutine that smoothly rotates a transform at a specified speed for a given duration, with optional acceleration.
    /// </summary>
    /// <param name="tObj">The transform to rotate.</param>
    /// <param name="rotatingSpeed">The rotation speed in degrees per second.</param>
    /// <param name="duration">The duration of the rotation in seconds.</param>
    /// <param name="acceleration">Optional acceleration factor. Positive for acceleration, negative for deceleration. 0 for constant speed.</param>
    static IEnumerator SmoothRotatingCoroutine(
        Transform tObj,
        float rotatingSpeed,
        float duration,
        float acceleration = 0f)
    {
        float elapsed = 0f;
        float currentSpeed = rotatingSpeed;

        while (elapsed < duration)
        {
            float dt = Time.deltaTime;
            
            // Apply acceleration/deceleration
            if (acceleration != 0f)
            {
                currentSpeed += acceleration * dt;
            }

            tObj.Rotate(0f, 0f, currentSpeed * dt);

            elapsed += dt;
            yield return null;
        }
    }
    
    /// <summary>
    /// Smoothly translates a transform by a relative offset over a specified duration.
    /// </summary>
    public static void SmoothTranslating(MonoBehaviour runner, Transform tObj, Vector3 tPos, float duration) // t - target
    {
        runner.StartCoroutine(SmoothTranslatingCoroutine(tObj,  tObj.position + tPos, duration));
    }

    /// <summary>
    /// Coroutine that smoothly moves a transform to an absolute target position over a specified duration using linear interpolation.
    /// </summary>
    /// <param name="tObj">The transform to move.</param>
    /// <param name="tPos">The target position to move to.</param>
    /// <param name="duration">The duration of the movement in seconds.</param>
    static IEnumerator SmoothTranslatingCoroutine(Transform tObj, Vector3 tPos, float duration)
    {
        if (duration <= 0f)
        {
            tObj.position = tPos;
            yield break;
        }

        Vector3 startPos = tObj.position;
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            float t = elapsedTime / duration;
            tObj.position = Vector3.Lerp(startPos, tPos, t);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        tObj.position = tPos;
    }
    
    // OTHER HELPERS AND COROUTINES
    /// <summary>
    /// Invokes a callback function after a specified delay using a coroutine.
    /// </summary>
    public static void InvokeAfterTime(MonoBehaviour runner, float duration, UnityAction func)
    {
        runner.StartCoroutine(InvokeAfterTimeCoroutine(func, duration));
    }

    /// <summary>
    /// Coroutine that waits for a specified duration before invoking a callback.
    /// </summary>
    /// <param name="func">The callback function to invoke.</param>
    /// <param name="duration">The delay in seconds before invoking the callback.</param>
    static IEnumerator InvokeAfterTimeCoroutine(UnityAction func, float duration)
    {
        yield return new WaitForSeconds(duration);
        func.Invoke();
    }
    
    
    
    // DATA PROCESS ASSIGNMENT
    /// <summary>
    /// Assigns a component of type T from an owner to a field, if not already assigned.
    /// </summary>
    public static void AssignComponent<T>(Component owner, ref T field) where T : Component
    {
        if (field == null) field = owner.GetComponent<T>();
    }

    /// <summary>
    /// Swaps the values of two reference variables.
    /// </summary>
    public static void Swap<T>(ref T lhs, ref T rhs)
    {
        T temp = lhs;
        lhs = rhs;
        rhs = temp;
    }
    
    /// <summary>
    /// Creates a copy of a ScriptableObject and assigns it to a reference variable.
    /// </summary>
    public static void CopySO<T>(ref T so) where T : ScriptableObject
    {
        so = Object.Instantiate(so);
    }

    /// <summary>
    /// Creates and returns a copy of a ScriptableObject.
    /// </summary>
    public static T CopySO<T>(T so) where T : ScriptableObject
    {
        return Object.Instantiate(so);
    }
    
    // LISTS, ARRAYS, DATA STRUCTURES
    /// <summary>
    /// Creates a list of specified length filled with the provided value.
    /// </summary>
    public static List<T> CreateList<T>(int len, T value)
    {
        List<T> result = new List<T>();
        for (int i = 0; i < len; i++)
        {
            result.Add(value);
        }

        return result;
    }

    /// <summary>
    /// Checks, if all arguments are not null.
    /// !!!WARNING: may not function properly with ints and floats as their default value equals to 0!!!
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public static bool CheckIfAllExist(params object[] args)
    {
        foreach (object arg in args)
        {
            if (arg == default) return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if all elements in a list are not null (or not their default value).
    /// WARNING: may not function properly with ints and floats as their default value equals to 0.
    /// </summary>
    /// <param name="list">The list to check.</param>
    /// <returns>True if all elements are not null/default, false otherwise.</returns>
    public static bool CheckIfAllExist<T>(List<T> list)
    {
        if (list == null) return false;
        return CheckIfAllExist(list.Cast<object>().ToArray());
    }

    /// <summary>
    /// Executes an action for each element in a list.
    /// </summary>
    /// <param name="list">The list to iterate through.</param>
    /// <param name="action">The action to execute for each element.</param>
    public static void ForEach<T>(List<T> list, UnityAction<T> action)
    {
        foreach (T obj in list)
        {
            action.Invoke(obj);
        }
    }
    
    /// <summary>
    /// Assigns key-value pairs from lists to a dictionary.
    /// </summary>
    public static void AssignValuesToDict<TK, TV>(ref Dictionary<TK, TV> original, List<TK> keysIn, List<TV> valuesIn)
    {
        if (keysIn == null || valuesIn == null || keysIn.Count != valuesIn.Count) return;
        for (int i = 0; i < keysIn.Count; i++)
        {
            original[keysIn[i]] = valuesIn[i];
        }
    }

    /// <summary>
    /// Assigns all key-value pairs from one dictionary to another.
    /// </summary>
    public static void AssignValuesToDict<TK, TV>(ref Dictionary<TK, TV> original, Dictionary<TK, TV> data)
    {
        AssignValuesToDict(ref original, data.Keys.ToList(), data.Values.ToList());
    }
    
    // TEXT TMP
    /// <summary>
    /// Checks if a sprite with the specified name exists in the provided TMP_SpriteAsset.
    /// </summary>
    /// <param name="name">The name of the sprite to check.</param>
    /// <param name="spriteAsset">The TMP_SpriteAsset to search in (defaults to TMP_Settings.defaultSpriteAsset).</param>
    /// <returns>True if the sprite exists in the asset.</returns>
    public static bool TMPSpriteExists(string name, TMP_SpriteAsset spriteAsset=null)
    {
        if (!spriteAsset) spriteAsset = TMP_Settings.defaultSpriteAsset;
        return spriteAsset.spriteCharacterTable
            .Any(c => c.name == name);
    }

    /// <summary>
    /// Finds the optimal font size for TextMeshPro text to fit within the specified rectangle.
    /// </summary>
    /// <param name="src">The TextMeshProUGUI component.</param>
    /// <param name="rect">The target RectTransform to fit text within.</param>
    /// <param name="minSize">Minimum font size (-1 for auto).</param>
    /// <param name="maxSize">Maximum font size (-1 for auto).</param>
    /// <param name="loops">Number of iterations for binary search.</param>
    /// <returns>The optimal font size to fit the text in the rectangle.</returns>
    public static float FindOptimalFontSize(TextMeshProUGUI src, RectTransform rect, float minSize=-1f, float maxSize=-1f, int loops=10)
    {
        float optimalSize=1f;
        if (minSize < 0) minSize = 1f;
        if (maxSize < 0) maxSize = src.fontSize;
        
        float targetWidth = rect.rect.width;
        float targetHeight = rect.rect.height;
        float targetArea = targetWidth * targetHeight;
        
        while (minSize < maxSize && loops > 0)
        {
            optimalSize = (minSize + maxSize) / 2f;
            src.fontSize = optimalSize;
            src.ForceMeshUpdate();
            
            Vector2 preferredBoundSize = src.GetPreferredValues(src.text);
            float area = preferredBoundSize.x * preferredBoundSize.y;
        
            if (area > targetArea)
            {
                maxSize = optimalSize;
            } else if (area < targetArea)
            {
                minSize = optimalSize;
            }
            else
            {
                break;
            }
            
            loops--;
        }

        optimalSize = (minSize + maxSize) / 2f;
        // optimalSize = minSize;
        
        optimalSize = h.Max(optimalSize, minSize);
        optimalSize = h.Min(optimalSize, maxSize);

        return optimalSize;
    }
    
    

    /// <summary>
    /// Calculates the hypotenuse (diagonal) of a sprite's bounds from a GameObject.
    /// </summary>
    /// <param name="go">The GameObject with a SpriteRenderer component.</param>
    /// <returns>The hypotenuse of the sprite's bounds.</returns>
    public static float GetSpriteHypotenuse(GameObject go)
    {
        if (!go) return 0;

        Sprite spr = null;
        SpriteRenderer rend = go.GetComponent<SpriteRenderer>();
        if (rend) spr = rend.sprite;
        if (!spr)
        {
            rend = go.GetComponentInChildren<SpriteRenderer>();
            if (rend) spr = rend.sprite;
        }
        if (!spr) return 0;

        return (float)M.Sqrt(M.Pow(spr.bounds.size.x, 2) + M.Pow(spr.bounds.size.y, 2));
    }

    /// <summary>
    /// Calculates the hypotenuse (diagonal) of a sprite's bounds from a SpriteRenderer.
    /// </summary>
    /// <param name="go">The SpriteRenderer component.</param>
    /// <returns>The hypotenuse of the sprite's bounds.</returns>
    public static float GetSpriteHypotenuse(SpriteRenderer go)
    {
        if (!go) return 0;
        Sprite sr = go.sprite;
        if (!sr) return 0;

        return (float)M.Sqrt(M.Pow(sr.bounds.size.x, 2) + M.Pow(sr.bounds.size.y, 2));
    }

    /// <summary>
    /// Calculates the hypotenuse (diagonal) of a sprite's bounds.
    /// </summary>
    /// <param name="sr">The Sprite to measure.</param>
    /// <returns>The hypotenuse of the sprite's bounds.</returns>
    public static float GetSpriteHypotenuse(Sprite sr)
    {
        return (float)M.Sqrt(M.Pow(sr.bounds.size.x, 2) + M.Pow(sr.bounds.size.y, 2));
    }
    
    // PHYSICS
    /// <summary>
    /// Applies friction-based braking to a rigidbody, gradually decelerating it to a stop.
    /// </summary>
    /// <param name="target">The Rigidbody2D to brake.</param>
    /// <param name="friction">The friction coefficient.</param>
    /// <param name="linearVelocityMultOnStart">Multiplier for initial velocity.</param>
    public static void BrakeWithFriction(
        Rigidbody2D target,
        float friction,
        float linearVelocityMultOnStart=1)
    {
        target.GetComponent<MonoBehaviour>().StartCoroutine(BrakeWithFrictionCoroutine(
            target,
            friction,
            linearVelocityMultOnStart
        ));
    }
    
    /// <summary>
    /// Coroutine that applies friction-based deceleration to a rigidbody until it stops.
    /// </summary>
    /// <param name="rb">The Rigidbody2D to brake.</param>
    /// <param name="frictionCoefficient">The friction coefficient.</param>
    /// <param name="linearVelocityMultOnStart">Multiplier for initial velocity.</param>
    static IEnumerator BrakeWithFrictionCoroutine(Rigidbody2D rb, float frictionCoefficient, float linearVelocityMultOnStart=1)
    {
        rb.linearVelocity *= linearVelocityMultOnStart;

        float g = Mathf.Abs(Physics2D.gravity.y) * rb.gravityScale;
        float deceleration = frictionCoefficient * g;

        rb.gravityScale = 0f;

        while (rb.linearVelocity.sqrMagnitude > 0.0001f)
        {
            Vector2 v = rb.linearVelocity;
            float speed = v.magnitude;

            float dv = deceleration * Time.fixedDeltaTime;

            if (dv >= speed)
            {
                rb.linearVelocity = Vector2.zero;
                break;
            }

            rb.linearVelocity -= v.normalized * dv;

            yield return new WaitForFixedUpdate();
        }

        DisableRB(rb);
    }

    /// <summary>
    /// Rotates a transform to align with its rigidbody's velocity direction over a specified duration.
    /// </summary>
    /// <param name="t">The transform to rotate.</param>
    /// <param name="duration">Duration of the rotation in seconds.</param>
    /// <param name="offset">Angle offset to apply to the rotation.</param>
    public static void RotateByGravity(Transform t, float duration, float offset=90)
    {
        Rigidbody2D rb = t.GetComponent<Rigidbody2D>();
        t.GetComponent<MonoBehaviour>().StartCoroutine(
            RotateByGravityCoroutine(t, rb, duration, 90)
        );
    }

    /// <summary>
    /// Coroutine that continuously rotates a transform to align with rigidbody velocity over a specified duration.
    /// </summary>
    /// <param name="t">The transform to rotate.</param>
    /// <param name="rb">The Rigidbody2D to get velocity from.</param>
    /// <param name="duration">Duration of the rotation in seconds.</param>
    /// <param name="offset">Angle offset to apply to the rotation.</param>
    static IEnumerator RotateByGravityCoroutine(Transform t, Rigidbody2D rb, float duration, float offset)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
            t.rotation = Quaternion.Euler(0, 0, angle+offset);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// Multiplies two Vector3s component-wise.
    /// </summary>
    /// <param name="v1">The first Vector3.</param>
    /// <param name="v2">The second Vector3.</param>
    /// <returns>Component-wise product of v1 and v2.</returns>
    public static Vector3 MultiplyVectors(Vector3 v1, Vector3 v2)
    {
        return new Vector3(v1.x*v2.x, v1.y*v2.y, v1.z*v2.z);
    }

    /// <summary>
    /// Multiplies two Vector2s component-wise.
    /// </summary>
    /// <param name="v1">The first Vector2.</param>
    /// <param name="v2">The second Vector2.</param>
    /// <returns>Component-wise product of v1 and v2.</returns>
    public static Vector2 MultiplyVectors(Vector2 v1, Vector2 v2)
    {
        return new Vector2(v1.x*v2.x, v1.y*v2.y);
    }

    /// <summary>
    /// Returns the distance between two vectors as a fraction of the max distance.
    /// If reversed is true, returns 1 minus the fraction.
    /// </summary>
    /// <param name="t1">The first vector position.</param>
    /// <param name="t2">The second vector position.</param>
    /// <param name="maxDistance">The maximum distance for normalization.</param>
    /// <param name="reversed">If true, returns 1 - fraction instead of fraction.</param>
    /// <returns>Distance fraction (0 to 1).</returns>
    public static float DistanceFraction(Vector3 t1, Vector3 t2, float maxDistance, bool reversed=false)
    {
        float fraction = (t1 - t2).magnitude / maxDistance;
        // h.Out(t1, t2);
        // h.Out((t1 - t2).magnitude, maxDistance, fraction, 1-fraction);
        return reversed ? 1-fraction : fraction;
    }

    /// <summary>
    /// Sets a rigidbody to kinematic state and zeroes its velocity and angular velocity.
    /// </summary>
    /// <param name="rb">The Rigidbody2D to disable.</param>
    public static void DisableRB(Rigidbody2D rb)
    {
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    /// <summary>
    /// Detaches a list of ParticleSystems from their parent transforms.
    /// </summary>
    /// <param name="ps">List of ParticleSystems to detach.</param>
    public static void DetatchParticles(List<ParticleSystem> ps)
    {
        h.ForEach(ps, (p) => DetatchParticles(p));
    }

    /// <summary>
    /// Detaches a ParticleSystem from its parent, configures it to stop and destroy after completion.
    /// If tagged as "InstantlyDestroyableParticle", destroys it immediately.
    /// </summary>
    /// <param name="ps">The ParticleSystem to detach.</param>
    public static void DetatchParticles(ParticleSystem ps)
    {
        if (!ps)
        {
            return;
        }
        if(ps.gameObject.tag == "InstantlyDestroyableParticle")
        {
            UnityEngine.Object.Destroy(ps.gameObject);
            return;
        }
        ps.transform.SetParent(null);
        var main = ps.main;
        main.loop = false;
        main.stopAction = ParticleSystemStopAction.Destroy;
        ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        
    }

    /// <summary>
    /// Fades out the target GameObject by gradually reducing its alpha over the specified duration.
    /// Supports SpriteRenderer and TextMeshProUGUI components.
    /// </summary>
    /// <param name="target">The GameObject to fade away.</param>
    /// <param name="duration">The time in seconds over which to fade.</param>
    /// <param name="runner">The MonoBehaviour to run the coroutine on. If null, uses one from the target.</param>
    /// <param name="destroyOnFinish">Whether to destroy the GameObject after fading out.</param>
    public static void FadeOut(GameObject target, float duration, MonoBehaviour runner=null, bool destroyOnFinish=false)
    {
        if (!runner) runner = target.GetComponent<MonoBehaviour>();
        runner.StartCoroutine(FadeOutCoroutine(target, duration,destroyOnFinish));
    }

    /// <summary>
    /// Coroutine that fades out a GameObject's alpha over a specified duration.
    /// </summary>
    /// <param name="target">The GameObject to fade.</param>
    /// <param name="duration">Duration in seconds.</param>
    /// <param name="destroyOnFinish">Whether to destroy after fading.</param>
    /// <summary>
    /// Coroutine that fades out a GameObject's alpha over a specified duration.
    /// </summary>
    /// <param name="target">The GameObject to fade.</param>
    /// <param name="duration">Duration in seconds.</param>
    /// <param name="destroyOnFinish">Whether to destroy after fading.</param>
    static IEnumerator FadeOutCoroutine(GameObject target, float duration, bool destroyOnFinish)
    {
        System.Exception _;
        SpriteRenderer sr = target.GetComponent<SpriteRenderer>();
        TMP_Text tmp = target.GetComponent<TMP_Text>();

        Color getColor;
        System.Action<Color> setColor;

        if (sr)           { getColor = sr.color;  setColor = c => {
                                                                    try {sr.color  = c;}
                                                                    catch (System.Exception e) {_=e; /*just placeholder to ignore alert*/}
                                                                }; }
        else if (tmp)     { getColor = tmp.color; setColor = c => {
                                                                    try {tmp.color  = c;}
                                                                    catch (System.Exception e) {_=e; /*just placeholder to ignore alert*/}
                                                                }; }
        else yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            getColor.a = Mathf.Lerp(1f, 0f, elapsed / duration);
            // h.Out("a: ", getColor.a);
            setColor(getColor);
            yield return null;
        }

        getColor.a = 0f;
        setColor(getColor);

        if (destroyOnFinish)
        {
            GameObject.Destroy(target);
        }
    }
    
    /// <summary>
    /// Fades out the target GameObject by gradually reducing its alpha over the specified duration.
    /// Supports SpriteRenderer and TextMeshProUGUI components.
    /// </summary>
    /// <param name="target">The GameObject to fade away.</param>
    /// <param name="duration">The time in seconds over which to fade.</param>
    /// <param name="runner">The MonoBehaviour to run the coroutine on. If null, uses one from the target.</param>
    /// <param name="destroyOnFinish">Whether to destroy the GameObject after fading out.</param>
    public static void FadeIn(GameObject target, float duration, MonoBehaviour runner=null)
    {
        if (!runner) runner = target.GetComponent<MonoBehaviour>();
        runner.StartCoroutine(FadeInCoroutine(target, duration));
    }
    
    /// <summary>
    /// Coroutine that fades in a GameObject's alpha over a specified duration.
    /// </summary>
    /// <param name="target">The GameObject to fade in.</param>
    /// <param name="duration">Duration in seconds.</param>
    static IEnumerator FadeInCoroutine(GameObject target, float duration)
    {
        System.Exception _;
        SpriteRenderer sr = target.GetComponent<SpriteRenderer>();
        TextMeshProUGUI tmp = target.GetComponent<TextMeshProUGUI>();

        Color getColor;
        System.Action<Color> setColor;

        if (sr)           { getColor = sr.color;  setColor = c => {
                                                                    try {sr.color  = c;}
                                                                    catch (System.Exception e) {_=e; /*just placeholder to ignore alert*/}
                                                                }; }
        else if (tmp)     { getColor = tmp.color; setColor = c => {
                                                                    try {tmp.color  = c;}
                                                                    catch (System.Exception e) {_=e; /*just placeholder to ignore alert*/}
                                                                }; }
        else yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            getColor.a = Mathf.Lerp(0f, 1f, elapsed / duration);
            setColor(getColor);
            yield return null;
        }

        getColor.a = 1f;
        setColor(getColor);
    }

    // CAMERA
    public static CameraShakeInstance ShakeOnce(float magnitude, float sharpness, float fadeInDuration, float fadeOutDuration)
    {
        return CameraShaker.Instance.ShakeOnce(magnitude, sharpness, fadeInDuration, fadeOutDuration);
    }
    

    /// <summary>
    /// Applies a camera shake effect and automatically ends it after a specified duration.
    /// </summary>
    /// <param name="runner">The MonoBehaviour to run the coroutine on.</param>
    /// <param name="magnitude">The magnitude of the shake.</param>
    /// <param name="sharpness">The sharpness of the shake.</param>
    /// <param name="duration">The duration of the shake in seconds.</param>
    /// <param name="fadeInDuration">The fade-in duration for the shake.</param>
    /// <param name="fadeOutDuration">The fade-out duration for the shake.</param>
    /// <returns>The CameraShakeInstance for the applied shake.</returns>
    public static CameraShakeInstance ShakeOnce(MonoBehaviour runner, float magnitude, float sharpness, float duration, float fadeInDuration=0, float fadeOutDuration=0)
    {
        CameraShakeInstance inst = CameraShaker.Instance.StartShake(magnitude, sharpness, fadeInDuration);
        h.InvokeAfterTime(runner, duration + fadeInDuration, () =>
        {
            if (inst == null) return;
            CameraShaker.Instance.EndShake(fadeOutDuration, inst);
        });
        return inst;
    }

    /// <summary>
    /// Starts a camera shake effect.
    /// </summary>
    /// <param name="magnitude">The magnitude of the shake.</param>
    /// <param name="sharpness">The sharpness of the shake.</param>
    /// <param name="fadeInDuration">The fade-in duration for the shake.</param>
    /// <returns>The CameraShakeInstance for the applied shake.</returns>
    public static CameraShakeInstance StartShake(float magnitude, float sharpness, float fadeInDuration)
    {
        return CameraShaker.Instance.StartShake(magnitude, sharpness, fadeInDuration);
    }

    /// <summary>
    /// Ends a camera shake effect with optional fade-out.
    /// </summary>
    /// <param name="fadeOutTime">The time to fade out the shake.</param>
    /// <param name="instance">The specific CameraShakeInstance to end, or null to end a generic shake.</param>
    public static void EndShake(float fadeOutTime=0f, CameraShakeInstance instance=null)
    {
        CameraShaker.Instance.EndShake(fadeOutTime, instance);
    }

    /// <summary>
    /// Ends all active camera shake effects with optional fade-out.
    /// </summary>
    /// <param name="fadeOutTime">The time to fade out all shakes.</param>
    public static void EndAllShakes(float fadeOutTime=0f)
    {
        ForEach(CameraShaker.Instance.cameraShakeInstances, (inst) =>
        {
            EndShake(fadeOutTime:fadeOutTime, instance:inst);
        });
    }

    /// <summary>
    /// Checks if a world position is visible within the main camera's viewport.
    /// </summary>
    /// <param name="pos">The world position to check.</param>
    /// <returns>True if the position is within the camera's viewport, false otherwise.</returns>
    public static bool CheckInsideCamera(Vector3 pos)
    {
        Vector3 viewportPos = Camera.main.WorldToViewportPoint(pos);

        bool isVisible =
            viewportPos.x >= 0 && viewportPos.x <= 1 &&
            viewportPos.y >= 0 && viewportPos.y <= 1;
        
        return isVisible;
    }

    /// <summary>
    /// Gets the world position of the camera's top-left corner.
    /// </summary>
    /// <returns>The world position of the top-left corner.</returns>
    public static Vector2 GetCameraTopLeftCorner()
    {
        Camera cam = Camera.main;
        Vector3 topLeft = cam.ViewportToWorldPoint(
            new Vector3(0f, 1f, cam.nearClipPlane)
        );

        return new Vector2(topLeft.x, topLeft.y);
    }

    /// <summary>
    /// Gets the world position of the camera's bottom-right corner.
    /// </summary>
    /// <returns>The world position of the bottom-right corner.</returns>
    public static Vector2 GetCameraBottomRightCorner()
    {
        Camera cam = Camera.main;
        Vector3 bottomRight = cam.ViewportToWorldPoint(
            new Vector3(1f, 0f, cam.nearClipPlane)
        );

        return new Vector2(bottomRight.x, bottomRight.y);
    }

    /// <summary>
    /// Gets the width of the camera's viewport in world units.
    /// </summary>
    /// <returns>The camera's viewport width.</returns>
    public static float GetCameraWidth()
    {
        Vector2 topLeft = GetCameraTopLeftCorner();
        Vector2 bottomRight = GetCameraBottomRightCorner();
        return bottomRight.x - topLeft.x;
    }

    /// <summary>
    /// Gets the height of the camera's viewport in world units.
    /// </summary>
    /// <returns>The camera's viewport height.</returns>
    public static float GetCameraHeight()
    {
        Vector2 topLeft = GetCameraTopLeftCorner();
        Vector2 bottomRight = GetCameraBottomRightCorner();
        return topLeft.y - bottomRight.y;
    }


    // RESOURCES
    /// <summary>
    /// Checks if a resource exists at the specified path.
    /// </summary>
    /// <param name="path">The resource path to check.</param>
    /// <returns>True if the resource exists, false otherwise.</returns>
    public static bool ResourceExists(string path)
    {
        return Resources.Load(path) != null;
    }

    /// <summary>
    /// Recursively updates the rendering layers of all SpriteRenderers in a transform hierarchy.
    /// </summary>
    /// <param name="t">The root transform to start from.</param>
    /// <param name="deltaLayer">The layer offset to apply to each SpriteRenderer.</param>
    public static void UpdateLayersRecursively(Transform t, int deltaLayer)
    {
        // Check if current transform has SpriteRenderer
        if (t.TryGetComponent<SpriteRenderer>(out SpriteRenderer sr))
        {
            sr.gameObject.layer += deltaLayer;
        }
        
        // Recursively process all children
        foreach (Transform child in t)
        {
            UpdateLayersRecursively(child, deltaLayer);
        }
    }

    /// <summary>
    /// Recursively sets the sprite mask interaction for all SpriteRenderers in a transform hierarchy.
    /// </summary>
    /// <param name="t">The root transform to start from.</param>
    /// <param name="maskInteraction">The sprite mask interaction mode to apply.</param>
    public static void SetSpriteMaskInteractionRecursively(Transform t, SpriteMaskInteraction maskInteraction)
    {
        // Check if current transform has SpriteRenderer
        if (t.TryGetComponent<SpriteRenderer>(out SpriteRenderer sr))
        {
            sr.maskInteraction = maskInteraction;
        }
        
        // Recursively process all children
        foreach (Transform child in t)
        {
            SetSpriteMaskInteractionRecursively(child, maskInteraction);
        }
    }

    /// <summary>
    /// Finds all transforms in the scene with the specified tag and returns them as an array.
    /// </summary>
    /// <param name="tag">The tag to search for.</param>
    /// <returns>An array of transforms with the specified tag.</returns>
    public static Transform[] FindAllTransformsWithTag(string tag)
    {
        return Array.ConvertAll(
            GameObject.FindGameObjectsWithTag(tag),
            go => go.transform
        );
    }

    /// <summary>
    /// Applies a shake effect to a GameObject for a specified duration with fade-in and fade-out.
    /// </summary>
    /// <param name="runner">The MonoBehaviour to run the coroutine on.</param>
    /// <param name="target">The GameObject to shake.</param>
    /// <param name="magnitude">The magnitude of the shake.</param>
    /// <param name="sharpness">The sharpness of the shake.</param>
    /// <param name="duration">The duration of the shake in seconds.</param>
    /// <param name="fadeInDuration">Optional fade-in duration.</param>
    /// <param name="fadeOutDuration">Optional fade-out duration.</param>
    public static void ShakeObject(MonoBehaviour runner, GameObject target,
        float magnitude,
        float sharpness,
        float duration,
        float fadeInDuration = 0,
        float fadeOutDuration = 0)
    {
        runner.StartCoroutine(ShakeObjectCoroutine(
            target, magnitude, sharpness, duration, fadeInDuration, fadeOutDuration
            ));
    }

    /// <summary>
    /// Coroutine that applies a shake effect to a GameObject with optional fade-in and fade-out.
    /// </summary>
    /// <param name="target">The GameObject to shake.</param>
    /// <param name="magnitude">The magnitude of the shake.</param>
    /// <param name="sharpness">The sharpness of the shake.</param>
    /// <param name="duration">The duration of the shake.</param>
    /// <param name="fadeInDuration">Optional fade-in duration.</param>
    /// <param name="fadeOutDuration">Optional fade-out duration.</param>
    public static IEnumerator ShakeObjectCoroutine(
        GameObject target,
        float magnitude,
        float sharpness,
        float duration,
        float fadeInDuration=0,
        float fadeOutDuration=0
    )
    {
        if (target == null)
            yield break;

        Transform boneTransform = target.transform;

        // Save initial local position so we can restore it later
        Vector3 initialLocalPosition = boneTransform.localPosition;

        float totalDuration = fadeInDuration + duration + fadeOutDuration;
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;

            float strength = 1f;

            // Fade in
            if (elapsed <= fadeInDuration)
            {
                strength = Mathf.Clamp01(elapsed / fadeInDuration);
            }
            // Sustain full shake
            else if (elapsed <= fadeInDuration + duration)
            {
                strength = 1f;
            }
            // Fade out
            else
            {
                float fadeOutTime = elapsed - fadeInDuration - duration;
                strength = 1f - Mathf.Clamp01(fadeOutTime / fadeOutDuration);
            }

            // Sharp shake offset
            Vector3 randomOffset = Random.insideUnitSphere * magnitude * strength;

            // Sharpness makes movement snappier
            randomOffset *= sharpness;

            boneTransform.localPosition = initialLocalPosition + randomOffset;

            yield return null;
        }

        // Ensure exact reset at the end
        boneTransform.localPosition = initialLocalPosition;
    }
    
    /// <summary>
    /// Waits <paramref name="seconds"/> of gameplay time, stalling while the game is paused
    /// (mirrors CapturingEvent's pause-aware waiting).
    /// </summary>
    public static IEnumerator WaitGameSeconds(float seconds)
    {
        if (seconds <= 0f) yield break;

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            if (GameFlowManager.Instance && GameFlowManager.Instance.IsPaused())
            {
                yield return null;
                continue;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
    }
    
}