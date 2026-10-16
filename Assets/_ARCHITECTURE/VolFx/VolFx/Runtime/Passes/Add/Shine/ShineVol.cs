//  VolFx © NullTale - https://x.com/NullTale

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VolFx
{
    [Serializable, VolumeComponentMenu("VolFx/Shine")]
    public sealed class ShineVol : VolumeComponent, IPostProcessComponent
    {
        // Strength of the effect blending. 0 = disabled, 1 = full effect.
        public GradientParameter             _gradient = new GradientParameter(ShineDefault, false);
        
        [Header("Shine")]
        public ClampedFloatParameter         _Shine      = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter         _Dark       = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter         _Replace    = new ClampedFloatParameter(0f, 0f, 1f);
        
        [Header("Motion")]
        public ClampedFloatParameter         _Width    = new ClampedFloatParameter(1f, 0f, 1f);
        public ClampedFloatParameter         _Rotation     = new ClampedFloatParameter(0f, -1f, 1f);
        public NoInterpClampedFloatParameter _Motion     = new NoInterpClampedFloatParameter(0f, -1f, 1f);
        
        public ClampedFloatParameter         _Gap       = new ClampedFloatParameter(0f, 0f, 1f);
        
        [Header("Gloss")]
        public ClampedFloatParameter         _Gloss    = new ClampedFloatParameter(0f, 0f, 1f);
        public GlossParameter                _GlossTex = new GlossParameter(ShinePass.Gloss.Scratches, false);
        public Texture2DParameter            _GlossCustomTex = new Texture2DParameter(null, false);
        
        // =======================================================================
        public static GradientValue ShineDefault
        {
            get
            {
                var grad = new Gradient();
                grad.SetKeys(new []{new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f)}, new GradientAlphaKey[]{new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, 0f)});
                
                return new GradientValue(grad);
            }
        }

        [Serializable]
        public class GlossParameter : VolumeParameter<ShinePass.Gloss>
        {
            public GlossParameter(ShinePass.Gloss value, bool overrideState) : base(value, overrideState) { }
        }
        
        // =======================================================================
        public bool IsActive() => active && (_Shine.value != 0f || _Dark.value != 0f || _Replace.value != 0f || _Gloss.value != 0f);

        public bool IsTileCompatible() => true;
    }
}
