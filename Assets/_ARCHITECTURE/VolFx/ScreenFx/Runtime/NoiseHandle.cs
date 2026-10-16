using UnityEngine;

namespace ScreenFx
{
    public class NoiseHandle : MonoBehaviour
    {
        public float _ampl = 1;
        public float _freq = 1;
        public float _torq = 1;
        
        private ScreenFx.FloatHandle _amplH = new ScreenFx.FloatHandle();
        private ScreenFx.FloatHandle _freqH = new ScreenFx.FloatHandle();
        private ScreenFx.FloatHandle _torqH = new ScreenFx.FloatHandle();
        
        private ScreenFx.NoiseHandle _nosie;
        
        [Range(0, 1)]
        [SerializeField]
        protected float m_Weight = 1f;

        public float Weight
        {
            get => m_Weight;
            set => m_Weight = value;
        }
        
        // =======================================================================

        protected void OnEnable()
        {
            _nosie = ScreenFx.GetNoiseHandle(null);
            
            _nosie._ampl.Add(_amplH);
            _nosie._freq.Add(_freqH);
            _nosie._torque.Add(_torqH);
        }

        protected void OnDisable()
        {
            _nosie._ampl.Remove(_amplH);
            _nosie._freq.Remove(_freqH);
            _nosie._torque.Remove(_torqH);
        }

        public void Update()
        {
            _amplH._value = _ampl * m_Weight;
            _freqH._value = _freq * m_Weight;
            _torqH._value = _torq * m_Weight;
        }
    }
}