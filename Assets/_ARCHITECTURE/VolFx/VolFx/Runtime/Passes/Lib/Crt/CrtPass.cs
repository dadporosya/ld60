using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

//  CrtFx © NullTale - https://x.com/NullTale
namespace VolFx
{
    [ShaderName("Hidden/Vol/Crt")]
    public class CrtPass : VolFx.Pass
    {        
        private static readonly int s_GlowTex = Shader.PropertyToID("_GlowTex");
        private static readonly int s_MaskTex = Shader.PropertyToID("_MaskTex");
        
        private static readonly int s_Step = Shader.PropertyToID("_Step");
        private static readonly int s_Data_A = Shader.PropertyToID("_Data_A");
        private static readonly int s_Data_B = Shader.PropertyToID("_Data_B");

        private static readonly int s_R    = Shader.PropertyToID("_R");
        private static readonly int s_G    = Shader.PropertyToID("_G");
        private static readonly int s_B    = Shader.PropertyToID("_B");

        private static readonly int s_Bleed = Shader.PropertyToID("_Bleed");
        
        
        public override string ShaderName => string.Empty;
        
        // [Range(0, 1f)]
        // [Tooltip("Beam texture resolution (parameter can be used for performance reasons or to blur an image)")]
        // public float _scale = .5f;
        
        // public  bool _hdr;
        
        [Header("Filters")]
        public Vector4 _R = new Vector4(1f, 0f, 0.1f, 1.54f);
        public Vector4 _G = new Vector4(0f, 1f, 0.1f, -1f);
        public Vector4 _B = new Vector4(0f, 0f, 1.0f, 1.71f);
        
        //private                 ProfilingSampler _sampler;
        //private                 RenderTargetFlip _beam;
        private                 float            _step = 0.01f;
        private                 float            _flicker;
        private                 float            _timeLast;
        private                 float            _motion;
        private                 Texture2D        _glow;
        private                 Texture2D        _mask;
        
        [SerializeField] [HideInInspector]
        private                 Texture2D        Colorfull;
        [SerializeField] [HideInInspector]
        private                 Texture2D        HorizontalGlow;
        [SerializeField] [HideInInspector]
        private                 Texture2D        Chromatic;
        [SerializeField] [HideInInspector]
        private                 Texture2D        Classic;
        [SerializeField] [HideInInspector]
        private                 Texture2D        Dots;
        [SerializeField] [HideInInspector]
        private                 Texture2D        Cells;
        [SerializeField] [HideInInspector]
        private                 Texture2D        Focus;
        [SerializeField] [HideInInspector]
        private                 Texture2D        Green;
        [SerializeField] [HideInInspector]
        private                 Texture2D        Blue;
        [SerializeField] [HideInInspector]
        private                 Texture2D        Red;
        [SerializeField] [HideInInspector]
        private                 Texture2D        White;

        /*[Tooltip("Desired fps")]
        private float _fps = 60f;*/
        
        // =======================================================================
        public enum Mask
        {
            Colorfull,
            HorizontalGlow,
            Chromatic,
            Classic,
            Dots,
            Cells,
            Focus,
            Green,
            Blue,
            Red,
            White,
            
            Custom = 100
        }

        // =======================================================================
        public override void Init()
        {
            _timeLast = -1f;
            _motion = 0f;
        }

        public override bool Validate(Material mat)
        {
            var settings = Stack.GetComponent<CrtVol>();
            if (settings.IsActive() == false)
                return false;
            
            var aspect = Screen.width / (float)Screen.height;
            var step   = new Vector2(_step * settings.m_Dispersion.value * aspect, _step * settings.m_Dispersion.value);
            var res    = new Vector2(settings.m_Res.value * aspect, settings.m_Res.value);
            
            if (Time.unscaledTime - _timeLast > (1f / 25f))
            {
                if (Random.value < .15f * settings.m_Flicker.value)
                {
                    _timeLast = Time.unscaledTime;
                    _flicker = Random.value * settings.m_Power.value * .45f * settings.m_Flicker.value;
                }
            }

            if (settings.m_Motion.value != 0)
                _motion += settings.m_Motion.value * Time.unscaledDeltaTime;
            else
                _motion = 0f;

            //mat.SetTexture(s_GlowTex, settings.m_Correction.value.GetTexture(ref _glow));
            mat.SetVector(s_Step, new Vector4(step.x, step.y, settings.m_Beam.value, settings.m_Power.value + _flicker));
            mat.SetVector(s_Data_A, new Vector4(res.x, res.y, settings.m_Mask.value,  settings.m_Offset.value + _motion));
            mat.SetVector(s_Data_B, new Vector4(settings.m_Organic.value, settings.m_Correction.value, 0f, 0f));
            
            mat.SetVector(s_R, _R);
            mat.SetVector(s_G, _G);
            mat.SetVector(s_B, _B);
            
            mat.SetColor(s_Bleed, settings.m_Color.value);
            
            var pattern = settings.m_Pattern.value switch
            {
                Mask.Colorfull      => Colorfull,
                Mask.HorizontalGlow => HorizontalGlow,
                Mask.Chromatic      => Chromatic,
                Mask.Classic        => Classic,
                Mask.Dots           => Dots,
                Mask.Cells          => Cells,
                Mask.Focus          => Focus,
                Mask.Green          => Green,
                Mask.Blue           => Blue,
                Mask.Red            => Red,
                Mask.White          => White,
                Mask.Custom         => _mask,
                _                   => _mask
            };
            
            var mask = settings.m_Pattern.value == Mask.Custom ? (settings.m_Custom.overrideState ? settings.m_Custom.value : _mask) : pattern;
            if (mask == null)
                mask  = _mask;
            
            mat.SetTexture(s_MaskTex, mask);
            
            return true;
        }

        // -----------------------------------------------------------------------
        private float _getDrawTime()
        {
            var drawTime = Time.time;
#if UNITY_EDITOR
            if (Application.isPlaying == false)
                drawTime = (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
            
            return drawTime;
        }
        
        protected override bool _editorValidate => Colorfull == null;
		protected override void _editorSetup(string folder, string asset)
        {
#if UNITY_EDITOR
			var sep = Path.DirectorySeparatorChar;
			
            var ctx = UnityEditor.AssetDatabase
                                     .FindAssets("t:texture", new string[] {$"{folder}{sep}Data"})
                                     .Select(n => UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath(n)))
                                     .ToList();
            
            if (ctx.Count < 11)
                ctx.AddRange(Enumerable.Repeat(Texture2D.whiteTexture, 11));

            _mask = ctx.FirstOrDefault();
            
            Colorfull      = ctx[0];
            HorizontalGlow = ctx[1];
            Chromatic      = ctx[2];
            Classic        = ctx[3];
            Dots           = ctx[4];
            Cells          = ctx[5];
            Focus          = ctx[6];
            Green          = ctx[7];
            Blue           = ctx[8];
            Red            = ctx[9];
            White          = ctx[10];

            if (_mask == null)
                _mask = Texture2D.whiteTexture;
#endif
        }
    }
}