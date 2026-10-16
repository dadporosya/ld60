using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "KeyContainer", menuName = "Scriptable Objects/KeyContainer")]
public class KeyContainer : ScriptableObject
{
    public List<string> keys;
}
