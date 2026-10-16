using System;
using System.IO;
using System.Linq;
using UnityEngine;

//  VolFx © NullTale - https://x.com/NullTale
namespace VolFx
{
    [ShaderName("Hidden/VolFx/Shine")]
    public class ShinePass : VolFx.Pass
    {
        private static readonly int s_GradTex = Shader.PropertyToID("_GradTex");
        private static readonly int s_ProjTex = Shader.PropertyToID("_ProjTex");
        
        
        private static readonly int s_Weight  = Shader.PropertyToID("_Weight");
        private static readonly int s_DataA    = Shader.PropertyToID("_DataA");
        private static readonly int s_DataB    = Shader.PropertyToID("_DataB");
		
		public override string ShaderName => string.Empty;
        private Texture2D _grad;
        
        private                 float _angle;
        private                 float _offset;
        
        [SerializeField] [HideInInspector]
        private Texture2D _gloss_A;
        [SerializeField] [HideInInspector]
        private Texture2D _gloss_B;
        [SerializeField] [HideInInspector]
        private Texture2D _gloss_C;
        [SerializeField] [HideInInspector]
        private Texture2D _gloss_D;
        [SerializeField] [HideInInspector]
        private Texture2D _gloss_E;
        [SerializeField] [HideInInspector]
        private Texture2D _gloss_F;
        [SerializeField] [HideInInspector]
        private Texture2D _gloss_G;
        [SerializeField] [HideInInspector]
        private Texture2D _gloss_H;

        public enum Gloss
        {
            Scratches, // A
            Clouds,    // B
            Hex,        // C
            Fine,       // D
            Worn,       // E
            Dust,       // F
            Spots,      // G
            Pebbles,    // H
            
            Custom = 100
        }
        
        // =======================================================================
        public override void Init()
        {
            _offset = 0f;
        }

        public override bool Validate(Material mat)
        {
            var settings = Stack.GetComponent<ShineVol>();
            if (settings.IsActive() == false)
                return false;
            
            _angle = settings._Rotation.value * (Mathf.PI * 2f);
            _angle %= (Mathf.PI * 2f);
            
            if  (settings._Motion.value == 0f)
                _offset = 0f;
            else
                _offset -= settings._Motion.value * Time.unscaledDeltaTime;
            
            var glossTex = settings._GlossTex.value switch
            {
                Gloss.Scratches => _gloss_A,
                Gloss.Clouds    => _gloss_B,
                Gloss.Hex       => _gloss_C,
                Gloss.Fine      => _gloss_D,
                Gloss.Worn      => _gloss_E,
                Gloss.Dust      => _gloss_F,
                Gloss.Spots     => _gloss_G,
                Gloss.Pebbles   => _gloss_H,
                
                Gloss.Custom    => settings._GlossTex.overrideState ? settings._GlossCustomTex.value : null,
                _               => settings._GlossTex.overrideState ? settings._GlossCustomTex.value : null
            };
            
            if (glossTex == null)
                glossTex = Texture2D.whiteTexture;
            
            settings._gradient.value.GetTexture(ref _grad);
            
            mat.SetTexture(s_GradTex, _grad);
            mat.SetTexture(s_ProjTex, glossTex);
            
            mat.SetVector(s_DataA, new Vector4(_offset, settings._Width.value, settings._Rotation.value, settings._Gap.value));
            mat.SetVector(s_DataB, new Vector4(settings._Shine.value, settings._Dark.value, settings._Replace.value, settings._Gloss.value));

            return true;
        }
        
        protected override bool _editorValidate => _gloss_A == null;
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

            _gloss_A = ctx[0];
            _gloss_B = ctx[1];
            _gloss_C = ctx[2];
            _gloss_D = ctx[3];
            _gloss_E = ctx[4];
            _gloss_F = ctx[5];
            _gloss_G = ctx[6];
            _gloss_H = ctx[7];
#endif
        }
    }
}