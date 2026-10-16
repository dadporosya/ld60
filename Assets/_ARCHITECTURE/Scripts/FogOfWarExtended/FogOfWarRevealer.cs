// using System;
// using System.Collections;
// using FischlWorks_FogWar;
//
// using UnityEngine;
//
// public class FogOfWarRevealer : MonoBehaviour
// {
//     public int sightRange = 100;
//     private csFogWar.FogRevealer revealer;
//
//     private void Start()
//     {
//         Register();
//     }
//
//     /// <summary>
//     /// Registers this revealer with the fog manager. Safe to call more than once — an already
//     /// registered revealer is left alone. If the manager (or its csFogWar controller) isn't up yet,
//     /// registration is retried every frame until it succeeds.
//     /// </summary>
//     public void Register()
//     {
//         if (revealer != null) return;
//
//         FogOfWarManager manager = FogOfWarManager.Instance;
//         if (manager != null) revealer = manager.AddRevealer(transform);
//
//         if (revealer == null && isActiveAndEnabled) StartCoroutine(RegisterWhenReady());
//     }
//
//     private IEnumerator RegisterWhenReady()
//     {
//         // Bounded so a scene with no fog manager at all doesn't retry forever.
//         const float timeout = 5f;
//         float deadline = Time.unscaledTime + timeout;
//
//         while (revealer == null && Time.unscaledTime < deadline)
//         {
//             yield return null;
//
//             FogOfWarManager manager = FogOfWarManager.Instance;
//             if (manager != null) revealer = manager.AddRevealer(transform);
//         }
//     }
//
//     public void SetSightRange(int value)
//     {
//         sightRange = value;
//         if (revealer == null) return;
//         revealer.sightRange = sightRange;
//     }
//
//     private void OnDestroy()
//     {
//         if (FogOfWarManager.Instance != null && revealer != null)
//             FogOfWarManager.Instance.RemoveRevealer(revealer);
//     }
// }