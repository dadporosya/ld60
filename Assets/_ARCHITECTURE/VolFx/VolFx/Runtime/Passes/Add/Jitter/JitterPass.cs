using System.IO;
using System.Linq;
using UnityEngine;

//  JitterFx © NullTale - https://x.com/NullTale
namespace VolFx
{
    [ShaderName("Hidden/VolFx/Jitter")]
    public class JitterPass : VolFx.Pass
    {
        private static readonly int s_Jitter    = Shader.PropertyToID("_Jitter");
        private static readonly int s_NoiseTex  = Shader.PropertyToID("_NoiseTex");
        private static readonly int s_JitterTex = Shader.PropertyToID("_JitterTex");
        private static readonly int s_Mask      = Shader.PropertyToID("_Mask");

        private static readonly int s_R = Shader.PropertyToID("_R");
        private static readonly int s_G = Shader.PropertyToID("_G");
        private static readonly int s_B = Shader.PropertyToID("_B");

        public override string ShaderName => string.Empty;

        public  Texture2D _noiseTex;
        private Texture2D _jitterTex;
        [CurveRange]
        public AnimationCurve _smooth = new(new Keyframe(0f, 0f, 2f, 2f),
                                            new Keyframe(1f, 1f, 0f, 0f)) 
        {
            preWrapMode  = WrapMode.PingPong,
            postWrapMode = WrapMode.PingPong
        };
        
        [Header("Filters")]
        public Color _r = Color.red;
        public Color _g = Color.green;
        public Color _b = Color.blue;

        private                 float   _time;
        private                 float   _timeGoal;
        private                 float   _lerp;
        private                 Vector2 _offset;
        private                 Vector2 _offsetPrev;

        // =======================================================================
        public override void Init()
        {
        }

        public override bool Validate(Material mat)
        {
            var settings = Stack.GetComponent<JitterVol>();

            if (settings.IsActive() == false)
                return false;

            // intensity & move
            var intensity = Mathf.Lerp(0, 0.2f, settings.m_Intensity.value);
            _time -= settings.m_Speed.value * Time.deltaTime;
            _lerp = 1f - _time / _timeGoal;
            if (_time < 0)
            {
                _time = Mathf.Lerp(1f, Random.value * 2f, settings.m_Spikes.value);
                _offsetPrev = _offset;
                _offset += new Vector2(Random.value, Random.value);
                _timeGoal = _time;
                _lerp = 0f;
            }
            
            var lerp   = Mathf.LerpUnclamped(1f, _smooth.Evaluate(_lerp), settings.m_Smooth.value);
            var offset = Vector2.LerpUnclamped(_offset, _offsetPrev, lerp);
            var mask   = new Vector4(settings.m_Focus.value, settings.m_WeightX.value, settings.m_WeightY.value, 1);
                
            // params
            mat.SetTexture(s_NoiseTex, _noiseTex);
            mat.SetVector(s_Jitter, new Vector4(settings.m_Scale.value, offset.x, offset.y, intensity));
            mat.SetVector(s_Mask, mask);
            
            _r.a = settings.m_R.value;
            _g.a = settings.m_G.value;
            _b.a = settings.m_B.value;
            
            //settings.m_Mask.value.GetTexture(ref _jitterTex);
            mat.SetTexture(s_JitterTex, _jitterTex);
            
            mat.SetColor(s_R, _r);
            mat.SetColor(s_G, _g);
            mat.SetColor(s_B, _b);
            
            return true;
        }
        
        protected override bool _editorValidate => _noiseTex == null;
		protected override void _editorSetup(string folder, string asset)
        {
#if UNITY_EDITOR
			var sep = Path.DirectorySeparatorChar;
			
			_noiseTex = UnityEditor.AssetDatabase.FindAssets("t:texture", new string[] {$"{folder}{sep}Data"})
							   .Select(n => UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath(n)))
							   .FirstOrDefault();
            
            if (_noiseTex == null)
                _noiseTex = Texture2D.whiteTexture;
#endif
        }
    }
}