﻿using Leap.Unity;
using System.Collections.Generic;
using UnityEngine;

public class PinchStrokeProcessor : MonoBehaviour {

  private const float MIN_SEGMENT_LENGTH = 0.005F;

  public PinchDetector _pinchDetector;
  [Tooltip("Used to stop drawing if the pinch detector is grabbing a UI element.")]
  public WearableManager _wearableManager;
  public UndoRedoManager _undoRedoManager;

  private float _minThickness = 0.003F;
  private float _maxThickness = 0.03F;

  private bool _paintingStroke = false;
  private StrokeProcessor _strokeProcessor;
  private bool _firstStrokePointAdded = false;
  private Vector3 _lastStrokePointAdded = Vector3.zero;
  private float _timeSinceLastAddition = 0F;

  private Vector3 leftHandEulerRotation = new Vector3(0F, 180F, 0F);
  private Vector3 rightHandEulerRotation = new Vector3(0F, 180F, 0F);

  private StrokeRibbonRenderer _ribbonRenderer;

  void Start() {
    _strokeProcessor = new StrokeProcessor();

    // Set up and register filters.
    FilterPositionMovingAverage movingAvgFilter = new FilterPositionMovingAverage(6);
    _strokeProcessor.RegisterStrokeFilter(movingAvgFilter);
    FilterPitchYawRoll pitchYawRollFilter = new FilterPitchYawRoll();
    _strokeProcessor.RegisterStrokeFilter(pitchYawRollFilter);

    // Set up and register renderers.
    GameObject rendererObj = new GameObject();
    _ribbonRenderer = rendererObj.AddComponent<StrokeRibbonRenderer>();
    _ribbonRenderer.Color = Color.red; 
    _ribbonRenderer.Thickness = 0.02F;
    _ribbonRenderer.OnMeshFinalized += DoOnMeshFinalized;
    _strokeProcessor.RegisterStrokeRenderer(_ribbonRenderer);
  }

  void Update() {
    if (_pinchDetector.IsActive && !_paintingStroke) {
      if (!_wearableManager.IsPinchDetectorGrabbing(_pinchDetector)) {
        // TODO HACK FIXME
        Color color = new Color(0F, 0F, 0F, 0F);
        try {
          color = _pinchDetector.GetComponentInParent<IHandModel>().GetComponentInChildren<IndexTipColor>().GetColor();
        }
        catch (System.NullReferenceException) { }
        if (color.a > 0.99F) {
          BeginStroke(color);
          _paintingStroke = true;
        }
      }
    }
    else if (_pinchDetector.IsActive && _paintingStroke) {
      UpdateStroke();
    }
    else if (!_pinchDetector.IsActive && _paintingStroke) {
      EndStroke();
      _paintingStroke = false;
    }
  }

  public void SetThickness(float normalizedValue) {
    float value = Mathf.Clamp(normalizedValue, 0F, 1F);
    _ribbonRenderer.Thickness = Mathf.Lerp(_minThickness, _maxThickness, value);
  }

  private void BeginStroke(Color color) {
    _ribbonRenderer.Color = color;
    _strokeProcessor.BeginStroke();
  }

  private void UpdateStroke() {
    bool shouldAdd = !_firstStrokePointAdded
      || Vector3.Distance(_lastStrokePointAdded, _pinchDetector.Position) >= MIN_SEGMENT_LENGTH;

    _timeSinceLastAddition += Time.deltaTime;

    if (shouldAdd) {
      StrokePoint strokePoint = new StrokePoint();
      strokePoint.position = _pinchDetector.Position;
      strokePoint.rotation = Quaternion.identity;
      strokePoint.handOrientation = _pinchDetector.Rotation * Quaternion.Euler((_pinchDetector.HandModel.Handedness == Chirality.Left ? leftHandEulerRotation : rightHandEulerRotation));
      strokePoint.deltaTime = _timeSinceLastAddition;

      _strokeProcessor.UpdateStroke(strokePoint);

      _firstStrokePointAdded = true;
      _lastStrokePointAdded = strokePoint.position;
      _timeSinceLastAddition = 0F;
    }
  }

  private void EndStroke() {
    _strokeProcessor.EndStroke();
  }

  // TODO DELETEME FIXME
  private void DoOnMeshFinalized(Mesh mesh) {
    GameObject finishedRibbonMesh = new GameObject();
    MeshFilter filter = finishedRibbonMesh.AddComponent<MeshFilter>();
    MeshRenderer renderer = finishedRibbonMesh.AddComponent<MeshRenderer>();
    Material ribbonMat = new Material(Shader.Find("LeapMotion/RibbonShader"));
    ribbonMat.hideFlags = HideFlags.HideAndDontSave;
    renderer.material = ribbonMat;
    filter.mesh = mesh;
    _undoRedoManager.NotifyAction(finishedRibbonMesh);
  }

}