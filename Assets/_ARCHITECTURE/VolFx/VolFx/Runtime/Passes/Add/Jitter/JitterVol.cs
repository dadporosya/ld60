using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

//  VolFx © NullTale - https://x.com/NullTale
namespace VolFx
{
    [Serializable, VolumeComponentMenu("VolFx/Jitter")]
    public sealed class JitterVol : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Overall effect intensity")]
        public ClampedFloatParameter m_Intensity = new ClampedFloatParameter(0, 0, 1f);
        
        [Header("Jitter")]
        [Tooltip("Noise scale")]
        [InspectorName("Scale")]
        public ClampedFloatParameter m_Scale = new ClampedFloatParameter(.16f, .03f, .2f);

        [Tooltip("Noise animation speed")]
        [InspectorName("Speed")]
        public ClampedFloatParameter m_Speed = new ClampedFloatParameter(3, 0, 15);

        [Tooltip("Smooths the distortion animation")]
        [InspectorName("Smooth")]
        public ClampedFloatParameter m_Smooth = new ClampedFloatParameter(0, 0, 1);

        [Tooltip("Adds sharp animation peaks")]
        [InspectorName("Spikes")]
        public ClampedFloatParameter m_Spikes = new ClampedFloatParameter(0, 0, 1);
        
        [Tooltip("Reduces distortion near the focus point")]
        public ClampedFloatParameter m_Focus = new ClampedFloatParameter(0, 0, 1);
        // [Tooltip("Controls Jitter strength based on image luminance.")]
        // public CurveParameter m_Mask = new CurveParameter(new CurveValue(new AnimationCurve(new []{ new Keyframe(0, 1), new Keyframe(1, 1) })), false);
        // public NoInterpClampedFloatParameter m_Weight = new NoInterpClampedFloatParameter(1f, 0, 1f);

        [Header("Chromatic")]
        [Tooltip("Red channel offset")]
        public ClampedFloatParameter m_R = new ClampedFloatParameter(1, 0, 1);
        [Tooltip("Green channel offset")]
        public ClampedFloatParameter m_G = new ClampedFloatParameter(1, 0, 1);
        [Tooltip("Blue channel offset")]
        public ClampedFloatParameter m_B = new ClampedFloatParameter(1, 0, 1);
        
        [Header("Weights")]
        [InspectorName("Y")]
        public ClampedFloatParameter m_WeightY = new ClampedFloatParameter(1, 0, 1);
        [InspectorName("X")]
        public ClampedFloatParameter m_WeightX = new ClampedFloatParameter(1, 0, 1);

        // =======================================================================
        // Can be used to skip rendering if false
        public bool IsActive() => active && (m_Intensity.value > 0);

        public bool IsTileCompatible() => false;
    }
}
