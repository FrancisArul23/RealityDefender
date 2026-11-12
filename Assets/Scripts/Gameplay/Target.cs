using System;
using System.Collections;
using UnityEngine;

namespace RealityDefender
{
    /// <summary>
    /// Floating energy orb (prefab). Scales in, bobs and spins, pops then explodes when hit.
    /// Needs SphereCollider + Rigidbody (Is Kinematic ON, Use Gravity OFF).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Target : MonoBehaviour
    {
        #region Inspector

        [SerializeField] GameObject explosionPrefab;

        [Header("Idle Motion")]
        [SerializeField] float bobHeight = 0.02f;
        [SerializeField] float bobSpeed = 2f;
        [SerializeField] float spinSpeed = 45f;

        [Header("Juice")]
        [SerializeField] float spawnDuration = 0.25f;
        [SerializeField] float popScale = 1.35f;
        [SerializeField] float popDuration = 0.08f;

        #endregion

        #region Events

        /// <summary>Raised just before the GameObject is destroyed.</summary>
        public event Action<Target> Destroyed;

        #endregion

        #region Private Fields

        Collider _collider;
        Vector3 _baseScale;
        Vector3 _startLocalPos;
        float _phase;
        bool _isDead;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            _collider = GetComponent<Collider>();
            _baseScale = transform.localScale;
        }

        void Start()
        {
            _startLocalPos = transform.localPosition;
            _phase = UnityEngine.Random.value * 10f;   // so targets don't bob in sync
            StartCoroutine(ScaleIn());
        }

        void Update()
        {
            if (_isDead) return;

            float offset = Mathf.Sin((Time.time + _phase) * bobSpeed) * bobHeight;
            transform.localPosition = _startLocalPos + Vector3.up * offset;
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        }

        #endregion

        #region Hit

        /// <summary>Called by Projectile on impact.</summary>
        public void Hit()
        {
            if (_isDead) return;
            _isDead = true;
            _collider.enabled = false;

            StopAllCoroutines();
            StartCoroutine(PopAndExplode());
        }

        #endregion

        #region Animations

        IEnumerator ScaleIn()
        {
            for (float t = 0f; t < spawnDuration; t += Time.deltaTime)
            {
                transform.localScale = _baseScale * GameManager.EaseOutBack(t / spawnDuration);
                yield return null;
            }
            transform.localScale = _baseScale;
        }

        IEnumerator PopAndExplode()
        {
            Vector3 from = transform.localScale;
            Vector3 to = _baseScale * popScale;

            for (float t = 0f; t < popDuration; t += Time.deltaTime)
            {
                transform.localScale = Vector3.Lerp(from, to, t / popDuration);
                yield return null;
            }

            if (explosionPrefab != null)
            {
                var fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
                Destroy(fx, 3f); // safety net if the particle's Stop Action isn't Destroy
            }

            Destroyed?.Invoke(this);
            Destroy(gameObject);
        }

        #endregion
    }
}