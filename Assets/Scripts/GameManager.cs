using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.ARFoundation;
using Google.XR.ARCoreExtensions;

using TMPro;

namespace Google.XR.ARCoreExtensions.Samples.PersistentCloudAnchors
{
    public class GameManager : MonoBehaviour
    {
        public TMP_Text _displayText;
        public TMP_Text _successText;
        public TMP_Text _qualityText;
        public TMP_Text _instructionText;
        public TMP_Text _anchorSizeText;

        public GameObject CloudAnchorPrefab;
        public GameObject ToyPrefab;
        public GameObject MapQualityIndicatorPrefab;

        private ARAnchor _anchor;
        private MapQualityIndicator _qualityIndicator = null;
        private List<ARCloudAnchor> _pendingCloudAnchors = new List<ARCloudAnchor>();
        private List<ARCloudAnchor> _cachedCloudAnchors = new List<ARCloudAnchor>();

        private float _timeSinceStart;
        private const float _startPrepareTime = 3.0f;

        public PersistentCloudAnchorsController Controller;
        private CloudAnchorHistory _hostedCloudAnchor;

        private Pose GetCameraPose()
        {
            return new Pose(Controller.MainCamera.transform.position,
                Controller.MainCamera.transform.rotation);
        }

        private void PerformHitTest(Vector2 touchPos)
        {
            List<ARRaycastHit> hitResults = new List<ARRaycastHit>();
            Controller.RaycastManager.Raycast(
                touchPos, hitResults, TrackableType.PlaneWithinPolygon);

            // If there was an anchor placed, then instantiate the corresponding object.
            var planeType = PlaneAlignment.HorizontalUp;
            if (hitResults.Count > 0)
            {
                ARPlane plane = Controller.PlaneManager.GetPlane(hitResults[0].trackableId);
                if (plane == null)
                {
                    Debug.LogWarningFormat("Failed to find the ARPlane with TrackableId {0}",
                        hitResults[0].trackableId);
                    _displayText.SetText("Failed to find the ARPlane with TrackableId {0} "+ hitResults[0].trackableId);
                    return;
                }

                planeType = plane.alignment;
                var hitPose = hitResults[0].pose;
                if (Application.platform == RuntimePlatform.IPhonePlayer)
                {
                    // Point the hitPose rotation roughly away from the raycast/camera
                    // to match ARCore.
                    hitPose.rotation.eulerAngles =
                        new Vector3(0.0f, Controller.MainCamera.transform.eulerAngles.y, 0.0f);
                }

                _anchor = Controller.AnchorManager.AttachAnchor(plane, hitPose);
            }

            if (_anchor != null)
            {
                Instantiate(CloudAnchorPrefab, _anchor.transform);
                var indicatorGO =
                    Instantiate(MapQualityIndicatorPrefab, _anchor.transform);
                _qualityIndicator = indicatorGO.GetComponent<MapQualityIndicator>();
                _qualityIndicator.DrawIndicator(planeType, Controller.MainCamera);
                _displayText.SetText("Waiting for sufficient mapping quaility...");
            }
        }

        private void HostingCloudAnchors()
        {
            if (_anchor == null)
            {
                return;
            }

            // There is a pending or finished hosting task.
            if (_cachedCloudAnchors.Count > 0 || _pendingCloudAnchors.Count > 0)
            {
                return;
            }

            //Update Map Quality
            FeatureMapQuality quality = Controller.AnchorManager.EstimateFeatureMapQualityForHosting(GetCameraPose());
            _qualityText.SetText("Current Map Quality: " + quality);
           
            // Hosting instructions:
            var cameraDist = (_qualityIndicator.transform.position - Controller.MainCamera.transform.position).magnitude;
            if (cameraDist < _qualityIndicator.Radius * 1.5f)
            {
                _instructionText.SetText("You are too close, move backward.");
                return;
            }
            else if (cameraDist > 10.0f)
            {
                _instructionText.SetText("You are too far, come closer.");
                return;
            }
            else if (_qualityIndicator.ReachTopviewAngle)
            {
                _instructionText.SetText("You are looking from the top view, move around from all sides.");
                return;
            }
            else if (!_qualityIndicator.ReachQualityThreshold && quality != FeatureMapQuality.Sufficient)
            {
                _instructionText.SetText("Save the object here by capturing it from all sides.");
                return;
            }

            _instructionText.SetText("Starting Hosting..");
            //Start Hosting
            ARCloudAnchor cloudAnchor = Controller.AnchorManager.HostCloudAnchor(_anchor, 1);
            if (cloudAnchor == null)
            {
                _successText.SetText("Failed to create a Cloud Anchor");
            }
            else
            {
                _pendingCloudAnchors.Add(cloudAnchor);
            }
        }

        private void UpdatePendingCloudAnchors()
        {
            foreach (var cloudAnchor in _pendingCloudAnchors)
            {
                if (cloudAnchor.cloudAnchorState == CloudAnchorState.Success)
                {
                    if (Controller.Mode == PersistentCloudAnchorsController.ApplicationMode.Hosting)
                    {
                        Debug.LogFormat("Succeed to host the Cloud Anchor: {0}.",
                            cloudAnchor.cloudAnchorId);
                        int count = Controller.LoadCloudAnchorHistory().Collection.Count;
                        _hostedCloudAnchor = new CloudAnchorHistory("CloudAnchor" + count,
                            cloudAnchor.cloudAnchorId);
                        _instructionText.SetText("Succeed to host the Cloud Anchor: {0}.");
                    }
                    else if (Controller.Mode ==
                        PersistentCloudAnchorsController.ApplicationMode.Resolving)
                    {
                        Debug.LogFormat("Succeed to resolve the Cloud Anchor: {0}",
                            cloudAnchor.cloudAnchorId);
                        _instructionText.SetText("Succeed to resolve the Cloud Anchor: {0}");
                        Instantiate(ToyPrefab, cloudAnchor.transform);
                    }

                    _cachedCloudAnchors.Add(cloudAnchor);
                }
                else if (cloudAnchor.cloudAnchorState != CloudAnchorState.TaskInProgress)
                {
                    if (Controller.Mode == PersistentCloudAnchorsController.ApplicationMode.Hosting)
                    {
                        Debug.LogFormat("Failed to host the Cloud Anchor with error {0}.",
                            cloudAnchor.cloudAnchorState);
                        _instructionText.SetText("Failed to host the Cloud Anchor with error {0}.");
                    }
                    else if (Controller.Mode ==
                        PersistentCloudAnchorsController.ApplicationMode.Resolving)
                    {
                        Debug.LogFormat("Failed to resolve the Cloud Anchor {0} with error {1}.",
                            cloudAnchor.cloudAnchorId, cloudAnchor.cloudAnchorState);
                        _instructionText.SetText("Failed to resolve the Cloud Anchor {0} with error {1}.");
                    }

                    _cachedCloudAnchors.Add(cloudAnchor);
                }
            }

            _pendingCloudAnchors.RemoveAll(
                x => x.cloudAnchorState != CloudAnchorState.TaskInProgress);
        }

        private void ResolvingCloudAnchors()
        {
            _pendingCloudAnchors.Add(_cachedCloudAnchors[0]);

        }
        private bool _isDone;
        private void OnEnable()
        {
            _timeSinceStart = 0.0f;
            _isDone = false;
        }

        private void UpdatePlaneVisibility(bool visible)
        {
            foreach (var plane in Controller.PlaneManager.trackables)
            {
                plane.gameObject.SetActive(visible);
            }
        }

        private void Update()
        {
            if (_isDone == false)
            {
                if (_cachedCloudAnchors.Count == 0)
                {
                    //Hosting
                    if (_timeSinceStart < _startPrepareTime)
                    {
                        _timeSinceStart += Time.deltaTime;
                        return;
                    }

                    if (_anchor == null)
                    {
                        Touch touch = new Touch();
                        if (Input.touchCount < 1 ||
                                (touch = Input.GetTouch(0)).phase != TouchPhase.Began)
                        {
                            _instructionText.SetText("Tap On Surface");
                            return;
                        }
                        PerformHitTest(touch.position);
                    }
                    HostingCloudAnchors();
                    UpdatePendingCloudAnchors();
                }
                else
                {
                    //Resolving
                    ResolvingCloudAnchors();
                    foreach (var cloudAnchor in _pendingCloudAnchors)
                    {
                        _instructionText.SetText("Succeed to resolve the Cloud Anchor: {0}");
                        Instantiate(ToyPrefab, cloudAnchor.transform);
                    }
                    _isDone = true;
                }
            }
            else
            {
                UpdatePlaneVisibility(false);
            }
            _anchorSizeText.SetText("Cached Anchor Size: "+_cachedCloudAnchors.Count);
        }
    }

}
