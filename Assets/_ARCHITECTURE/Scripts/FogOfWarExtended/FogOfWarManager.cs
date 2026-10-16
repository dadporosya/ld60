// using System;
// using FischlWorks_FogWar;
// using UnityEngine;
//
// public class FogOfWarManager : MonoBehaviour
// {
//     public static  FogOfWarManager Instance;
//     public csFogWar fogWarController;
//     [SerializeField] private int defaultFieldOfView = 100;
//     
//     // Awake, not Start: revealers register themselves in their own Start, so the manager (and its
//     // controller reference) has to exist before any Start runs.
//     private void Awake()
//     {
//         h.CreateStaticInstance(this, ref Instance);
//         if (!fogWarController) fogWarController = FindFirstObjectByType<csFogWar>();
//     }
//
//     public csFogWar.FogRevealer AddRevealer(Transform t)
//     {
//         if (!fogWarController) return null;
//
//         int sightRange = defaultFieldOfView;
//         if (t.TryGetComponent(out FogOfWarRevealer rawRevealer))
//         {
//             sightRange = rawRevealer.sightRange;
//         }
//
//         csFogWar.FogRevealer revealer = new csFogWar.FogRevealer(t, sightRange, false);
//         fogWarController.fogRevealers.Add(revealer);
//         return revealer;
//     }
//
//     public void RemoveRevealer(csFogWar.FogRevealer revealer)
//     {
//         if (!fogWarController || revealer == null) return;
//         fogWarController.fogRevealers.Remove(revealer);
//     }
//
// }
