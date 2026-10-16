using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

//  FlowFx © NullTale - https://x.com/NullTale
namespace VolFx
{
    [Serializable, VolumeComponentMenu("VolFx/Crt")]
    public sealed class CrtVol : VolumeComponent, IPostProcessComponent
    {
        //public ClampedFloatParameter m_Weight     = new ClampedFloatParameter(1, 0, 1, false);
        public ClampedFloatParameter m_Mask       = new ClampedFloatParameter(0f, 0f, 1f);

        
        [Header("Beam")]
        public ClampedFloatParameter m_Beam       = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter m_Dispersion = new ClampedFloatParameter(.2f, 0f, 1f);
        public ClampedFloatParameter m_Correction = new ClampedFloatParameter(0f, 0f, 1f, false);

        [Header("Glow")]
        public ColorParameter        m_Color   = new ColorParameter(new Color(1f, 1f, 1f, 0f), false);
        public ClampedFloatParameter m_Power   = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter m_Flicker = new ClampedFloatParameter(0f, 0f, 1f);
        
        [Header("Matrix")]
        public NoInterpClampedFloatParameter m_Res      = new NoInterpClampedFloatParameter(60f, 20f, 360f);
        public MaskParameter                 m_Pattern  = new MaskParameter(CrtPass.Mask.Colorfull, false);
        public ClampedFloatParameter         m_Offset   = new ClampedFloatParameter(0f, 0f, 1f);
        public NoInterpClampedFloatParameter m_Organic  = new NoInterpClampedFloatParameter(0f, 0f, 1f);
        
        public Texture2DParameter    m_Custom = new Texture2DParameter(null, false);
        public ClampedFloatParameter m_Motion = new ClampedFloatParameter(0f, -1f, 1f);
        
        //public ClampedFloatParameter m_Power = new ClampedFloatParameter(0f, 1f, 1f);
        
        [Serializable]
        public class MaskParameter : VolumeParameter<CrtPass.Mask>
        {
            public MaskParameter(CrtPass.Mask value, bool overrideState) : base(value, overrideState) { }
        }
        
        
        // =======================================================================
        public bool IsActive() => active && (m_Mask.value > 0f || m_Beam.value > 0 || m_Color.value.a > 0 || m_Correction.value > 0);
        public bool IsTileCompatible() => false;
    }

}