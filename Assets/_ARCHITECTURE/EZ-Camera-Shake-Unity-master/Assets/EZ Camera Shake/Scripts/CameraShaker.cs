using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace EZCameraShake
{
    /// <summary>
    /// MODIFIED FOR THIS PROJECT. Two changes matter:
    ///
    /// 1. The shake is applied in LateUpdate, not Update, so the camera pose is final before the
    ///    frame is rendered.
    /// 2. The shaker no longer OWNS transform.localPosition. RTSCameraController writes that every
    ///    frame (it is where camera zoom lives), and the stock version overwrote it unconditionally.
    ///    Two scripts fighting over one property in the same callback means any third script that
    ///    reads the camera between them - CursorPathManager, casting ScreenPointToRay - builds its
    ///    ray from a pose the frame is NOT rendered from, so clicks land away from the cursor by
    ///    exactly the zoom offset. Positional shake is now opt-in and additive.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("EZ Camera Shake/Camera Shaker")]
    public class CameraShaker : MonoBehaviour
    {
        /// <summary>
        /// The single instance of the CameraShaker in the current scene. Do not use if you have multiple instances.
        /// </summary>
        public static CameraShaker Instance;
        static Dictionary<string, CameraShaker> instanceList = new Dictionary<string, CameraShaker>();

        /// <summary>
        /// The default position influcence of all shakes created by this shaker.
        /// </summary>
        public Vector3 DefaultPosInfluence = new Vector3(0.15f, 0.15f, 0.15f);
        /// <summary>
        /// The default rotation influcence of all shakes created by this shaker.
        /// </summary>
        public Vector3 DefaultRotInfluence = new Vector3(1, 1, 1);
        /// <summary>
        /// Offset that will be applied to the camera's default (0,0,0) rest position
        /// </summary>
        public Vector3 RestPositionOffset = new Vector3(0, 0, 0);
        /// <summary>
        /// Offset that will be applied to the camera's default (0,0,0) rest rotation
        /// </summary>
        public Vector3 RestRotationOffset = new Vector3(0, 0, 0);

        /// <summary>
        /// Apply the POSITIONAL part of the shake. OFF by default in this project: something else
        /// (RTSCameraController's zoom) owns the camera's localPosition, and a shaker that also
        /// writes it desyncs the rendered pose from the pose raycasts are built from. Turn this on
        /// only for a camera nothing else moves.
        /// </summary>
        [Tooltip("Apply the positional part of the shake. Leave OFF for any camera whose " +
                 "localPosition is driven by another script (e.g. RTS zoom).")]
        public bool shakePosition = false;

        Vector3 posAddShake, rotAddShake;

        /// <summary>Positional shake added last frame, so it can be undone instead of overwriting.</summary>
        Vector3 appliedPosShake;

        /// <summary>
        /// The camera's local rotation at startup. Shake is applied on top of this,
        /// so the camera returns to its authored rotation at rest instead of (0,0,0).
        /// </summary>
        Quaternion restLocalRotation;

        public List<CameraShakeInstance> cameraShakeInstances = new List<CameraShakeInstance>();

        void Awake()
        {
            // Set this FIRST. It used to sit after the dictionary Add below, which throws, so a
            // duplicate name left restLocalRotation at default(Quaternion) - the zero quaternion,
            // not identity - and the camera's rest rotation became garbage.
            restLocalRotation = transform.localRotation;

            // Every camera in this scene is called "Main Camera", and Dictionary.Add throws on a
            // duplicate key. The cutscene wakes the spotlight cameras mid-sequence, so the stock
            // Add() blew up in Awake the moment the second one activated. Indexing overwrites.
            instanceList[gameObject.name] = this;

            // Don't steal the singleton from a shaker that is already live: the spotlight cameras
            // are switched off again a second later, and Instance would be left pointing at a dead
            // camera for the rest of the session.
            if (Instance == null || !Instance.isActiveAndEnabled) Instance = this;
        }

        void LateUpdate()
        {
            posAddShake = Vector3.zero;
            rotAddShake = Vector3.zero;

            for (int i = 0; i < cameraShakeInstances.Count; i++)
            {
                if (i >= cameraShakeInstances.Count)
                    break;

                CameraShakeInstance c = cameraShakeInstances[i];

                if (c.CurrentState == CameraShakeState.Inactive && c.DeleteOnInactive)
                {
                    cameraShakeInstances.RemoveAt(i);
                    i--;
                }
                else if (c.CurrentState != CameraShakeState.Inactive)
                {
                    posAddShake += CameraUtilities.MultiplyVectors(c.UpdateShake(), c.PositionInfluence);
                    rotAddShake += CameraUtilities.MultiplyVectors(c.UpdateShake(), c.RotationInfluence);
                }
            }

            // Position: ADDITIVE, and only when explicitly asked for. Undo what we added last frame,
            // then add the new offset, so whatever else wrote localPosition this frame (the RTS
            // zoom) survives. The old code assigned localPosition outright, which wiped the zoom and
            // left the rendered pose disagreeing with the pose ScreenPointToRay had already used.
            if (shakePosition)
            {
                transform.localPosition -= appliedPosShake;
                appliedPosShake = posAddShake + RestPositionOffset;
                transform.localPosition += appliedPosShake;
            }
            else if (appliedPosShake != Vector3.zero)
            {
                // Toggled off at runtime: give back whatever we were still holding.
                transform.localPosition -= appliedPosShake;
                appliedPosShake = Vector3.zero;
            }

            // Apply shake on top of the camera's starting rotation instead of resetting
            // to (0,0,0), so the rest pose is whatever the camera had at startup.
            transform.localRotation =
                restLocalRotation * Quaternion.Euler(rotAddShake + RestRotationOffset);
        }
 
        /// <summary>
        /// Gets the CameraShaker with the given name, if it exists.
        /// </summary>
        /// <param name="name">The name of the camera shaker instance.</param>
        /// <returns></returns>
        public static CameraShaker GetInstance(string name)
        {
            CameraShaker c;

            if (instanceList.TryGetValue(name, out c))
                return c;

            Debug.LogError("CameraShake " + name + " not found!");

            return null;
        }

        /// <summary>
        /// Starts a shake using the given preset.
        /// </summary>
        /// <param name="shake">The preset to use.</param>
        /// <returns>A CameraShakeInstance that can be used to alter the shake's properties.</returns>
        public CameraShakeInstance Shake(CameraShakeInstance shake)
        {
            cameraShakeInstances.Add(shake);
            return shake;
        }

        /// <summary>
        /// Shake the camera once, fading in and out  over a specified durations.
        /// </summary>
        /// <param name="magnitude">The intensity of the shake.</param>
        /// <param name="roughness">Roughness of the shake. Lower values are smoother, higher values are more jarring.</param>
        /// <param name="fadeInTime">How long to fade in the shake, in seconds.</param>
        /// <param name="fadeOutTime">How long to fade out the shake, in seconds.</param>
        /// <returns>A CameraShakeInstance that can be used to alter the shake's properties.</returns>
        public CameraShakeInstance ShakeOnce(float magnitude, float roughness, float fadeInTime, float fadeOutTime)
        {
            CameraShakeInstance shake = new CameraShakeInstance(magnitude, roughness, fadeInTime, fadeOutTime);
            shake.PositionInfluence = DefaultPosInfluence;
            shake.RotationInfluence = DefaultRotInfluence;
            cameraShakeInstances.Add(shake);

            return shake;
        }

        /// <summary>
        /// Shake the camera once, fading in and out over a specified durations.
        /// </summary>
        /// <param name="magnitude">The intensity of the shake.</param>
        /// <param name="roughness">Roughness of the shake. Lower values are smoother, higher values are more jarring.</param>
        /// <param name="fadeInTime">How long to fade in the shake, in seconds.</param>
        /// <param name="fadeOutTime">How long to fade out the shake, in seconds.</param>
        /// <param name="posInfluence">How much this shake influences position.</param>
        /// <param name="rotInfluence">How much this shake influences rotation.</param>
        /// <returns>A CameraShakeInstance that can be used to alter the shake's properties.</returns>
        public CameraShakeInstance ShakeOnce(float magnitude, float roughness, float fadeInTime, float fadeOutTime, Vector3 posInfluence, Vector3 rotInfluence)
        {
            CameraShakeInstance shake = new CameraShakeInstance(magnitude, roughness, fadeInTime, fadeOutTime);
            shake.PositionInfluence = posInfluence;
            shake.RotationInfluence = rotInfluence;
            cameraShakeInstances.Add(shake);

            return shake;
        }

        /// <summary>
        /// Start shaking the camera.
        /// </summary>
        /// <param name="magnitude">The intensity of the shake.</param>
        /// <param name="roughness">Roughness of the shake. Lower values are smoother, higher values are more jarring.</param>
        /// <param name="fadeInTime">How long to fade in the shake, in seconds.</param>
        /// <returns>A CameraShakeInstance that can be used to alter the shake's properties.</returns>
        public CameraShakeInstance StartShake(float magnitude, float roughness, float fadeInTime)
        {
            CameraShakeInstance shake = new CameraShakeInstance(magnitude, roughness);
            shake.PositionInfluence = DefaultPosInfluence;
            shake.RotationInfluence = DefaultRotInfluence;
            shake.StartFadeIn(fadeInTime);
            cameraShakeInstances.Add(shake);
            return shake;
        }

        public void EndShake(float fadeOutTime=0, CameraShakeInstance instance=null)
        {
            if (instance==null) instance = cameraShakeInstances.Last();
            instance.StartFadeOut(fadeOutTime);
        }

        /// <summary>
        /// Start shaking the camera.
        /// </summary>
        /// <param name="magnitude">The intensity of the shake.</param>
        /// <param name="roughness">Roughness of the shake. Lower values are smoother, higher values are more jarring.</param>
        /// <param name="fadeInTime">How long to fade in the shake, in seconds.</param>
        /// <param name="posInfluence">How much this shake influences position.</param>
        /// <param name="rotInfluence">How much this shake influences rotation.</param>
        /// <returns>A CameraShakeInstance that can be used to alter the shake's properties.</returns>
        public CameraShakeInstance StartShake(float magnitude, float roughness, float fadeInTime, Vector3 posInfluence, Vector3 rotInfluence)
        {
            CameraShakeInstance shake = new CameraShakeInstance(magnitude, roughness);
            shake.PositionInfluence = posInfluence;
            shake.RotationInfluence = rotInfluence;
            shake.StartFadeIn(fadeInTime);
            cameraShakeInstances.Add(shake);
            return shake;
        }

        /// <summary>
        /// Gets a copy of the list of current camera shake instances.
        /// </summary>
        public List<CameraShakeInstance> ShakeInstances
        { get { return new List<CameraShakeInstance>(cameraShakeInstances); } }

        void OnDestroy()
        {
            // Only clear the entry if it is still OURS: cameras share names here, so a blind Remove
            // would evict a different, still-live shaker.
            if (instanceList.TryGetValue(gameObject.name, out CameraShaker registered) && registered == this)
                instanceList.Remove(gameObject.name);

            if (Instance == this) Instance = null;
        }
    }
}