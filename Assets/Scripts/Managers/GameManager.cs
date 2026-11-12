using System.Collections;
using UnityEngine;
using UnityEngine.Pool;

namespace RealityDefender
{
    public enum GameState
    {
        Scanning,     // looking for a floor, waiting for the tap
        Playing,      // base placed, targets alive
        WaveCleared   // all targets destroyed
    }

    /// <summary>
    /// Central game controller: game flow, score, weapon
    /// (firing + projectile pool), audio and haptics. Tells UIManager what to show. One per scene.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        #region Instance (SingleTon)

        public static GameManager Instance { get; private set; }

        #endregion

        #region Inspector Fields

        [Header("References")]
        [SerializeField] UIManager uiManager;
        [SerializeField] ARManager arManager;
        [SerializeField] Camera arCamera;
        [SerializeField] Projectile projectilePrefab;

        [Header("Weapon")]
        [SerializeField] float projectileSpeed = 4f;
        [SerializeField] float projectileLifetime = 3f;
        [SerializeField] float fireCooldown = 0.15f;
        [SerializeField] float spawnDistance = 0.3f;

        [Header("Scoring")]
        [SerializeField] int pointsPerTarget = 10;
        [SerializeField] float waveClearedDelay = 0.4f;

        [SerializeField] bool vibrateOnHit = true;

        #endregion

        #region State

        public GameState State { get; private set; } = GameState.Scanning;
        public int Score { get; private set; }
        public bool CanFire => State == GameState.Playing;

        /// <summary>Projectiles currently in flight (shown by the debug text).</summary>
        public int ActiveProjectiles => _projectilePool?.CountActive ?? 0;

        GameBase _gameBase;
        int _targetsRemaining;
        Coroutine _clearRoutine;
        ObjectPool<Projectile> _projectilePool;
        float _nextFireTime;

        bool _isMoving;
        GameState _stateBeforeMove;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Safety net: find the ARManager if it wasn't assigned in the Inspector.
            if (arManager == null) arManager = FindFirstObjectByType<ARManager>();
            if (arManager == null) Debug.LogError("[GameManager] No ARManager found - Move Base won't work.");

            CreateProjectilePool();
        }

        void Start()
        {
            SetState(GameState.Scanning);
            uiManager.SetScore(Score, animate: false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DetachBase();
            _projectilePool?.Dispose();
        }

        #endregion

        #region Game Flow

        /// <summary>Called by ARManager once the base is placed on the floor.</summary>
        public void BeginGame(GameBase gameBase)
        {
            if (State != GameState.Scanning || _isMoving) return;

            _gameBase = gameBase;
            _gameBase.TargetDestroyed += OnTargetDestroyed;

            ResetScore();
            StartWave();
        }

        /// <summary>Restart button: same base, fresh wave.</summary>
        public void Restart()
        {
            if (_gameBase == null) return;
            StopClearRoutine();

            ResetScore();
            StartWave();
        }

        /// <summary>
        /// Move Base button: pick up the existing base (same targets, same score) so the player
        /// can drop it somewhere else. The game resumes in the same state once it's dropped.
        /// </summary>
        public void MoveBase()
        {
            if (_isMoving || State == GameState.Scanning || _gameBase == null || arManager == null) return;

            _isMoving = true;
            _stateBeforeMove = State;
            arManager.BeginMove();
            SetState(GameState.Scanning);   // hides the gameplay HUD, shows the scanning hints
        }

        /// <summary>Called by ARManager when the moved base has been dropped.</summary>
        public void OnBaseMoved()
        {
            if (!_isMoving) return;
            _isMoving = false;
            SetState(_stateBeforeMove);
        }

        void StartWave()
        {
            _targetsRemaining = _gameBase.SpawnWave();
            SetState(GameState.Playing);
        }

        void OnTargetDestroyed(Target target)
        {
            // Also count hits from projectiles still in flight when Move Base was pressed.
            if (State != GameState.Playing && !_isMoving) return;

            Score += pointsPerTarget;
            uiManager.SetScore(Score, animate: true);
            uiManager.ShowHitMarker();
            Vibrate();

            if (--_targetsRemaining <= 0)
                _clearRoutine = StartCoroutine(ClearWaveAfterDelay());
        }

        IEnumerator ClearWaveAfterDelay()
        {
            yield return new WaitForSeconds(waveClearedDelay);
            _clearRoutine = null;

            // Wave finished while the base was being moved: show WAVE CLEARED once it's dropped.
            if (_isMoving) { _stateBeforeMove = GameState.WaveCleared; yield break; }

            SetState(GameState.WaveCleared);
        }

        void SetState(GameState state)
        {
            State = state;
            uiManager.ShowState(state);
        }

        void ResetScore()
        {
            Score = 0;
            uiManager.SetScore(Score, animate: false);
        }

        void StopClearRoutine()
        {
            if (_clearRoutine != null) StopCoroutine(_clearRoutine);
            _clearRoutine = null;
        }

        void DetachBase()
        {
            if (_gameBase != null) _gameBase.TargetDestroyed -= OnTargetDestroyed;
            _gameBase = null;
        }

        #endregion

        #region Weapon

        /// <summary>Fire button. Shoots straight out of the camera, where the crosshair points.</summary>
        public void Fire()
        {
            if (!CanFire || Time.time < _nextFireTime) return;
            _nextFireTime = Time.time + fireCooldown;

            Transform cam = arCamera.transform;
            Vector3 spawnPos = cam.position + cam.forward * spawnDistance;

            var projectile = _projectilePool.Get();
            projectile.Launch(spawnPos, cam.rotation, cam.forward * projectileSpeed,
                              projectileLifetime, _projectilePool.Release);
        }

        /// <summary>Reuses projectiles instead of Instantiate/Destroy on every shot (no GC spikes on mobile).</summary>
        void CreateProjectilePool()
        {
            _projectilePool = new ObjectPool<Projectile>(
                createFunc: () => Instantiate(projectilePrefab),
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: p => Destroy(p.gameObject),
                collectionCheck: false,
                defaultCapacity: 10,
                maxSize: 30);
        }

        #endregion

        #region Haptics

        void Vibrate()
        {
#if UNITY_IOS || UNITY_ANDROID
            if (vibrateOnHit) Handheld.Vibrate();
#endif
        }

        #endregion

        #region Utilities

        /// <summary>Ease-out-back: overshoots slightly past 1, then settles. Input/output 0..1.</summary>
        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t = Mathf.Clamp01(t) - 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        #endregion
    }
}