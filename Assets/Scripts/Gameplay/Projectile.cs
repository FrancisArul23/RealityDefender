using System;
using UnityEngine;

namespace RealityDefender
{
    /// <summary>
    /// Pooled energy bolt (prefab). Flies straight, hits a Target, then returns to the pool.
    /// Needs SphereCollider + Rigidbody (Use Gravity OFF). Optional TrailRenderer.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Projectile : MonoBehaviour
    {
        #region Private Fields

        Rigidbody _rb;
        TrailRenderer _trail;
        Action<Projectile> _release;
        float _expiresAt;
        bool _inFlight;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // small + fast: avoid tunnelling
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _trail = GetComponent<TrailRenderer>();
        }

        void Update()
        {
            if (_inFlight && Time.time >= _expiresAt)
                Despawn(); // a miss
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!_inFlight) return;

            var target = collision.collider.GetComponentInParent<Target>();
            if (target != null) target.Hit();

            Despawn();
        }

        #endregion

        #region Launch & Despawn

        /// <summary>Called by GameManager when taken from the pool.</summary>
        public void Launch(Vector3 position, Quaternion rotation, Vector3 velocity,
                           float lifetime, Action<Projectile> release)
        {
            transform.SetPositionAndRotation(position, rotation);
            _rb.position = position;
            _rb.rotation = rotation;
            SetVelocity(velocity);
            _rb.angularVelocity = Vector3.zero;
            if (_trail != null) _trail.Clear();

            _expiresAt = Time.time + lifetime;
            _release = release;
            _inFlight = true;
        }

        void Despawn()
        {
            _inFlight = false;
            SetVelocity(Vector3.zero);
            _release?.Invoke(this);
        }

        void SetVelocity(Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = v;
#else
            _rb.velocity = v;
#endif
        }

        #endregion
    }
}
