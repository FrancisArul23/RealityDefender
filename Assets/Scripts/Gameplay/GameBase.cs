using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RealityDefender
{
    /// <summary>
    /// The placed platform (prefab root). Plays a scale-in intro, then spawns targets
    /// one by one at the spawn points and reports each kill.
    /// </summary>
    public class GameBase : MonoBehaviour
    {
        #region Inspector

        [SerializeField] Target targetPrefab;
        [SerializeField] Transform[] spawnPoints;

        [Header("Intro")]
        [SerializeField] float introDuration = 0.5f;
        [Tooltip("Delay between each target popping in.")]
        [SerializeField] float targetStagger = 0.15f;

        #endregion

        #region Events

        public event Action<Target> TargetDestroyed;

        #endregion

        #region Private Fields

        readonly List<Target> _targets = new();
        Vector3 _baseScale;
        bool _introPlaying;
        Coroutine _spawnRoutine;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            // Set here (not Start) because SpawnWave is called in the same frame as Instantiate.
            _baseScale = transform.localScale;
            transform.localScale = Vector3.zero;
            _introPlaying = true;
        }

        void Start()
        {
            StartCoroutine(PlayIntro());
        }

        #endregion

        #region Wave

        /// <summary>
        /// Clears old targets and starts spawning a new wave (staggered, after the intro).
        /// Returns how many targets the wave will contain.
        /// </summary>
        public int SpawnWave()
        {
            ClearTargets();

            int count = 0;
            foreach (var point in spawnPoints)
                if (point != null) count++;

            _spawnRoutine = StartCoroutine(SpawnRoutine());
            return count;
        }

        public void ClearTargets()
        {
            if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;

            foreach (var target in _targets)
            {
                if (target == null) continue;
                target.Destroyed -= HandleTargetDestroyed;
                Destroy(target.gameObject);
            }
            _targets.Clear();
        }

        IEnumerator SpawnRoutine()
        {
            while (_introPlaying) yield return null;

            foreach (var point in spawnPoints)
            {
                if (point == null) continue;

                var target = Instantiate(targetPrefab, point.position, point.rotation, transform);
                target.Destroyed += HandleTargetDestroyed;
                _targets.Add(target);

                yield return new WaitForSeconds(targetStagger);
            }
            _spawnRoutine = null;
        }

        void HandleTargetDestroyed(Target target)
        {
            target.Destroyed -= HandleTargetDestroyed;
            _targets.Remove(target);
            TargetDestroyed?.Invoke(target);
        }

        #endregion

        #region Animations

        IEnumerator PlayIntro()
        {
            for (float t = 0f; t < introDuration; t += Time.deltaTime)
            {
                transform.localScale = _baseScale * GameManager.EaseOutBack(t / introDuration);
                yield return null;
            }
            transform.localScale = _baseScale;
            _introPlaying = false;
        }

        #endregion
    }
}