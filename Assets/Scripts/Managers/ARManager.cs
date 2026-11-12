using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace RealityDefender
{
    /// <summary>
    /// Everything AR: placement reticle, tap-to-place + anchoring, re-placing the base,
    /// tracking-quality messages and real-world light estimation. Lives on XR Origin.
    /// </summary>
    [RequireComponent(typeof(ARRaycastManager))]
    public class ARManager : MonoBehaviour
    {
        #region Inspector

        [SerializeField] GameBase gameBasePrefab;
        [SerializeField] ARPlaneManager planeManager;
        [SerializeField] Camera arCamera;
        [SerializeField] UIManager uiManager;

        [Header("Placement Reticle")]
        [Tooltip("Scene object shown on the floor where the base will go. Must have no colliders.")]
        [SerializeField] Transform placementReticle;
        [Tooltip("Higher = reticle follows the floor hit more tightly.")]
        [SerializeField] float reticleSmoothing = 15f;

        [Header("Light Estimation (optional)")]
        [Tooltip("The AR camera's ARCameraManager (Light Estimation must be enabled on it).")]
        [SerializeField] ARCameraManager cameraManager;
        [Tooltip("The scene's Directional Light, adjusted to match the room.")]
        [SerializeField] Light mainLight;
        [SerializeField] float minBrightness = 0.3f;
        [SerializeField] float maxBrightness = 1.3f;
        [Tooltip("0..1 per frame. Lower = smoother, slower light changes.")]
        [SerializeField] float lightSmoothing = 0.1f;

        #endregion

        #region Private Fields

        static readonly List<ARRaycastHit> s_Hits = new();
        readonly List<RaycastResult> _uiHits = new();

        ARRaycastManager _raycastManager;
        GameBase _placedBase;
        GameBase _movingBase;   // set while the player is relocating the base

        bool _hasValidPose;
        Pose _targetPose;
        bool _reticleSnapped;

        string _lastTrackingMessage = "";
        string _lastPlacementHint = "";
        float _baseLightIntensity = 1f;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            _raycastManager = GetComponent<ARRaycastManager>();
            if (placementReticle != null) placementReticle.gameObject.SetActive(false);

            // Safety net: find the UIManager if it wasn't assigned in the Inspector.
            if (uiManager == null) uiManager = FindFirstObjectByType<UIManager>();

            if (mainLight != null)
            {
                _baseLightIntensity = mainLight.intensity;
                GraphicsSettings.lightsUseColorTemperature = true;
                mainLight.useColorTemperature = true;
            }
        }

        void OnEnable()
        {
            if (cameraManager != null) cameraManager.frameReceived += OnCameraFrame;
        }

        void OnDisable()
        {
            if (cameraManager != null) cameraManager.frameReceived -= OnCameraFrame;
        }

        void Update()
        {
            UpdateTrackingMessage();

            if (_placedBase != null) return;

            UpdateReticle();
            UpdatePlacementHint();

            // Moving an existing base: it rides on the reticle until the player taps to drop it.
            if (_movingBase != null && _hasValidPose)
            {
                var pose = ReticlePose();
                _movingBase.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }

            if (!WasTapped(out Vector2 tapPos) || IsOverButton(tapPos)) return;

            if (_hasValidPose)
            {
                if (_movingBase != null) DropMovedBase(ReticlePose());
                else PlaceBase(ReticlePose());
            }
            else if (_movingBase == null && _raycastManager.Raycast(tapPos, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                // Fallback: reticle not on a plane, but the user tapped one directly.
                var hit = s_Hits[0].pose;
                PlaceBase(new Pose(hit.position, FacePlayer(hit)));
            }
        }

        #endregion

        #region Reticle

        /// <summary>Raycasts from the screen centre and moves the reticle smoothly onto the floor.</summary>
        void UpdateReticle()
        {
            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _hasValidPose = _raycastManager.Raycast(centre, s_Hits, TrackableType.PlaneWithinPolygon);

            if (_hasValidPose)
            {
                var hit = s_Hits[0].pose;
                _targetPose = new Pose(hit.position, FacePlayer(hit));
            }

            if (placementReticle == null) return;

            if (!_hasValidPose)
            {
                placementReticle.gameObject.SetActive(false);
                _reticleSnapped = false;
                return;
            }

            placementReticle.gameObject.SetActive(true);

            if (!_reticleSnapped)
            {
                placementReticle.SetPositionAndRotation(_targetPose.position, _targetPose.rotation);
                _reticleSnapped = true;
            }
            else
            {
                // Frame-rate independent smoothing.
                float t = 1f - Mathf.Exp(-reticleSmoothing * Time.deltaTime);
                placementReticle.SetPositionAndRotation(
                    Vector3.Lerp(placementReticle.position, _targetPose.position, t),
                    Quaternion.Slerp(placementReticle.rotation, _targetPose.rotation, t));
            }
        }

        /// <summary>
        /// Guides the player step by step: scan first, then aim at the floor, then tap.
        /// Hidden while a tracking problem message is showing, so only one instruction is on screen.
        /// </summary>
        void UpdatePlacementHint()
        {
            string hint;
            if (_lastTrackingMessage != null)
                hint = null; // a tracking warning is already on screen
            else if (planeManager.trackables.count == 0)
                hint = "<b>SCAN THE AREA</b>\n<size=70%>Move your phone slowly over the floor</size>";
            else if (!_hasValidPose)
                hint = "<b>AIM AT THE FLOOR</b>\n<size=70%>Point the centre of the screen at the scanned floor</size>";
            else if (_movingBase != null)
                hint = "<b>TAP TO DROP THE BASE</b>\n<size=70%>Aim where you want to move it</size>";
            else
                hint = "<b>TAP TO PLACE THE BASE</b>\n<size=70%>Tap anywhere on the screen</size>";

            if (hint == _lastPlacementHint) return;
            _lastPlacementHint = hint;
            if (uiManager != null) uiManager.SetPlacementHint(hint);
        }

        /// <summary>Place exactly where the player sees the reticle.</summary>
        Pose ReticlePose() => placementReticle != null
            ? new Pose(placementReticle.position, placementReticle.rotation)
            : _targetPose;

        #endregion

        #region Placement

        void PlaceBase(Pose pose)
        {
            _placedBase = Instantiate(gameBasePrefab, pose.position, pose.rotation);

            // Anchor so the base doesn't drift as tracking refines.
            if (!_placedBase.TryGetComponent<ARAnchor>(out _))
                _placedBase.gameObject.AddComponent<ARAnchor>();

            if (placementReticle != null) placementReticle.gameObject.SetActive(false);

            SetPlaneDetection(false);
            GameManager.Instance.BeginGame(_placedBase);
        }

        /// <summary>
        /// Called by GameManager.MoveBase(): picks up the EXISTING base (targets, score and progress
        /// are kept). It follows the reticle until the player taps to drop it somewhere else.
        /// </summary>
        public void BeginMove()
        {
            if (_placedBase == null) return;

            _movingBase = _placedBase;
            _placedBase = null;

            // An anchor pins the object in place every frame, so release it while moving.
            if (_movingBase.TryGetComponent<ARAnchor>(out var anchor)) Destroy(anchor);

            _reticleSnapped = false;
            _lastPlacementHint = ""; // force the hint to be re-sent
            SetPlaneDetection(true);
        }

        void DropMovedBase(Pose pose)
        {
            _placedBase = _movingBase;
            _movingBase = null;

            _placedBase.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _placedBase.gameObject.AddComponent<ARAnchor>(); // re-anchor at the new spot

            if (placementReticle != null) placementReticle.gameObject.SetActive(false);

            SetPlaneDetection(false);
            GameManager.Instance.OnBaseMoved();
        }

        /// <summary>Flat on the floor, rotated (yaw only) to face the camera.</summary>
        Quaternion FacePlayer(Pose pose)
        {
            Vector3 toCamera = arCamera.transform.position - pose.position;
            toCamera.y = 0f;
            return toCamera.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(toCamera, Vector3.up)
                : pose.rotation;
        }

        void SetPlaneDetection(bool enabled)
        {
            planeManager.enabled = enabled;
            foreach (var plane in planeManager.trackables)
                plane.gameObject.SetActive(enabled);
        }

        #endregion

        #region Tracking Messages

        void UpdateTrackingMessage()
        {
            string message = GetTrackingMessage();
            if (message == _lastTrackingMessage) return;

            _lastTrackingMessage = message;
            if (uiManager != null) uiManager.SetTrackingMessage(message);
        }

        /// <summary>Null when tracking is good; otherwise a short hint for the player.</summary>
        static string GetTrackingMessage()
        {
            switch (ARSession.state)
            {
                case ARSessionState.Unsupported:
                    return "AR is not supported on this device";

                case ARSessionState.NeedsInstall:
                case ARSessionState.Installing:
                    return "Installing AR support...";

                case ARSessionState.SessionTracking:
                    return null;

                default: // None, CheckingAvailability, Ready, SessionInitializing (limited tracking)
                    return ReasonToMessage(ARSession.notTrackingReason) ?? "Move your phone slowly";
            }
        }

        static string ReasonToMessage(NotTrackingReason reason) => reason switch
        {
            NotTrackingReason.Initializing         => "Move your phone slowly to start",
            NotTrackingReason.Relocalizing         => "Hold still - recovering tracking",
            NotTrackingReason.InsufficientLight    => "Too dark - turn on a light",
            NotTrackingReason.InsufficientFeatures => "Point at a textured surface",
            NotTrackingReason.ExcessiveMotion      => "Too fast - move more slowly",
            NotTrackingReason.CameraUnavailable    => "Camera unavailable",
            _                                      => null
        };

        #endregion

        #region Light Estimation

        /// <summary>Matches the Directional Light to the real room's brightness and colour.</summary>
        void OnCameraFrame(ARCameraFrameEventArgs args)
        {
            if (mainLight == null) return;
            var estimate = args.lightEstimation;

            // ARCore gives averageBrightness (0..1); ARKit gives lumens (~1000 = normal indoor light).
            float? brightness = estimate.averageBrightness;
            if (!brightness.HasValue && estimate.averageIntensityInLumens.HasValue)
                brightness = estimate.averageIntensityInLumens.Value / 1000f;

            if (brightness.HasValue)
            {
                float target = _baseLightIntensity * Mathf.Clamp(brightness.Value, minBrightness, maxBrightness);
                mainLight.intensity = Mathf.Lerp(mainLight.intensity, target, lightSmoothing);
            }

            if (estimate.averageColorTemperature.HasValue)
                mainLight.colorTemperature = Mathf.Lerp(mainLight.colorTemperature,
                                                        estimate.averageColorTemperature.Value, lightSmoothing);

            if (estimate.colorCorrection.HasValue)
                mainLight.color = estimate.colorCorrection.Value;
        }

        #endregion

        #region Helpers

        /// <summary>Touch on device, mouse in the editor / XR Simulation.</summary>
        static bool WasTapped(out Vector2 screenPos)
        {
            screenPos = default;
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame) return false;

            screenPos = pointer.position.ReadValue();
            return true;
        }

        /// <summary>Only buttons block placement; labels like the hint text don't.</summary>
        bool IsOverButton(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;

            var data = new PointerEventData(EventSystem.current) { position = screenPos };
            _uiHits.Clear();
            EventSystem.current.RaycastAll(data, _uiHits);

            foreach (var hit in _uiHits)
                if (hit.gameObject.GetComponentInParent<Selectable>() != null)
                    return true;

            return false;
        }

        #endregion
    }
}