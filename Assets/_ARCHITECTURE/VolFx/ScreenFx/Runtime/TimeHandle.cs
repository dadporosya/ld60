using System.Collections;
using UnityEngine;

namespace ScreenFx
{
    public class TimeHandle : MonoBehaviour
    {
        [SerializeField]
        private float _timeScale = 1f;

        public Optional<int>  _order = new Optional<int>(0, false);
        public Optional<Lerp> _tween = new Optional<Lerp>(false);

        private TimeAsset.TimeHandle _timeHandle;
        private Coroutine _coroutine;

        [System.Serializable]
        public class Lerp
        {
            public float _inStep = 0.3f;
            public float _inLerp = 7f;

            public float _outStep = 0.3f;
            public float _outLerp = 7f;

            public float UpdateIn(float value, float target, float dt)
            {
                if (Mathf.Abs(target - value) <= _inStep)
                    return target;

                value += Mathf.Sign(target - value) * _inStep;
                return Mathf.Lerp(value, target, _inLerp * dt);
            }

            public float UpdateOut(float value, float target, float dt)
            {
                if (Mathf.Abs(target - value) <= _outStep)
                    return target;

                value += Mathf.Sign(target - value) * _outStep;
                return Mathf.Lerp(value, target, _outLerp * dt);
            }
        }

        public float Scale
        {
            get => _timeScale;
            set => _timeScale = Mathf.Max(value, 0);
        }

        // =======================================================================
        private void Awake()
        {
            _timeScale = Mathf.Max(_timeScale, 0);
            
            _timeHandle = new TimeAsset.TimeHandle(
                _order.GetValueOrDefault(),
                _timeScale,
                name
            );

            _timeHandle.Dispose();
        }

        private void OnEnable()
        {
            if (_coroutine != null)
                 ScreenFx.Instance.StopCoroutine(_coroutine);

            _coroutine = ScreenFx.Instance.StartCoroutine(_update());
        }

        private void OnDestroy()
        {
            _timeHandle?.Dispose();
            _timeHandle = null;
        }

        // =======================================================================
        private IEnumerator _update()
        {
            _timeHandle.Activate();
            
            if (_tween.Enabled == false)
                while (gameObject.activeInHierarchy && enabled)
                {
                    _timeHandle._mul = _timeScale;
                    yield return null;
                }

            if (_tween.Enabled)
            {
                var lerp   = _tween.GetValueOrDefault(); 
                // Fade In
                var target = _timeScale;
                var current = 1f;
                while (gameObject.activeInHierarchy && enabled)
                {
                    if (Mathf.Abs(current - target) < 0.001f)
                        break;

                    current = Mathf.Lerp(current, target, lerp._inLerp * Time.unscaledDeltaTime);
                    current = Mathf.MoveTowards(current, target,  lerp._inStep * Time.unscaledDeltaTime);

                    _timeHandle._mul = current;
                    yield return null;
                }

                // Wait while enabled
                while (gameObject.activeInHierarchy && enabled)
                    yield return null;

                // Fade Out
                target = 1f;

                while (Mathf.Abs(_timeHandle._mul - target) > 0.001f)
                {
                    current = Mathf.Lerp(current, target, lerp._outLerp * Time.unscaledDeltaTime);
                    current = Mathf.MoveTowards(current, target,  lerp._outStep * Time.unscaledDeltaTime);

                    _timeHandle._mul = current;
                    yield return null;
                }
            }

            _timeHandle.Dispose();
            _coroutine = null;
        }
    }
}
