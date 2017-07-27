﻿using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Leap.Unity.Query;

namespace Leap.Unity.Recording {

  public class HierarchyRecorder : MonoBehaviour {

    public KeyCode beginRecordingKey = KeyCode.F5;
    public KeyCode finishRecordingKey = KeyCode.F6;

    private AnimationClip _clip;

    private List<PropertyRecorder> _recorders;
    private List<Transform> _transforms;
    private HashSet<Transform> _recordedTransforms;

    private List<AudioSource> _audioSources;
    private Dictionary<AudioSource, RecordedAudio> _audioData;

    private Dictionary<EditorCurveBinding, AnimationCurve> _curves;

    private bool _isRecording = false;
    private float _startTime = 0;

    private void LateUpdate() {
      if (Input.GetKeyDown(beginRecordingKey)) {
        beginRecording();
      }

      if (Input.GetKeyDown(finishRecordingKey)) {
        finishRecording();
      }

      if (_isRecording) {
        recordData();
      }
    }

    private void beginRecording() {
      if (_isRecording) return;
      _isRecording = true;
      _startTime = Time.time;

      _recorders = new List<PropertyRecorder>();
      _transforms = new List<Transform>();
      _recordedTransforms = new HashSet<Transform>();
      _audioSources = new List<AudioSource>();
      _audioData = new Dictionary<AudioSource, RecordedAudio>();
      _curves = new Dictionary<EditorCurveBinding, AnimationCurve>();
    }

    private void finishRecording() {
      if (!_isRecording) return;
      _isRecording = false;

      GetComponentsInChildren(true, _recorders);
      foreach (var recorder in _recorders) {
        DestroyImmediate(recorder);
      }

      //Patch up renderer references to materials
      var allMaterials = Resources.FindObjectsOfTypeAll<Material>().
                                   Query().
                                   Where(AssetDatabase.IsMainAsset).
                                   ToList();
      foreach (var renderer in GetComponentsInChildren<Renderer>(includeInactive: true)) {
        var materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++) {
          var material = materials[i];
          if (!AssetDatabase.IsMainAsset(material)) {
            var matchingMaterial = allMaterials.Query().FirstOrDefault(m => material.name.Contains(m.name) &&
                                                                            material.shader == m.shader);

            if (matchingMaterial != null) {
              materials[i] = matchingMaterial;
            }
          }
        }
        renderer.sharedMaterials = materials;
      }

      foreach (var pair in _curves) {
        //First do a lossless compression
        var curve = AnimationCurveUtil.Compress(pair.Value, Mathf.Epsilon);

        //But if the curve is constant, just get rid of it!
        if (curve.IsConstant()) {
          continue;
        }

        Transform targetTransform = null;
        var targetObj = AnimationUtility.GetAnimatedObject(gameObject, pair.Key);
        if (targetObj is GameObject) {
          targetTransform = (targetObj as GameObject).transform;
        } else if (targetObj is Component) {
          targetTransform = (targetObj as Component).transform;
        } else {
          Debug.LogError("Target obj was of type " + targetObj.GetType().Name);
        }

        var dataRecorder = targetTransform.GetComponent<RecordedData>();
        if (dataRecorder == null) {
          dataRecorder = targetTransform.gameObject.AddComponent<RecordedData>();
        }

        dataRecorder.data.Add(new RecordedData.EditorCurveBindingData() {
          path = pair.Key.path,
          propertyName = pair.Key.propertyName,
          typeName = pair.Key.type.Name,
          curve = curve
        });
      }

      gameObject.AddComponent<HierarchyPostProcess>();

      GameObject myGameObject = gameObject;

      DestroyImmediate(this);

      PrefabUtility.CreatePrefab("Assets/LeapMotion/Modules/HierarchyRecording/RawRecording.prefab", myGameObject);
    }

    private void recordData() {
      GetComponentsInChildren(true, _recorders);
      GetComponentsInChildren(true, _transforms);
      GetComponentsInChildren(true, _audioSources);

      //Update all audio sources
      foreach (var source in _audioSources) {
        RecordedAudio data;
        if (!_audioData.TryGetValue(source, out data)) {
          data = source.gameObject.AddComponent<RecordedAudio>();
          data.target = source;
          _audioData[source] = data;
        }
      }

      //Record all properties specified by recorders
      foreach (var recorder in _recorders) {
        foreach (var bindings in recorder.GetBindings(gameObject)) {
          if (!_curves.ContainsKey(bindings)) {
            _curves[bindings] = new AnimationCurve();
          }
        }
      }

      //Record ALL transform and gameObject data, no matter what
      foreach (var transform in _transforms) {
        if (!_recordedTransforms.Contains(transform)) {
          _recordedTransforms.Add(transform);

          var bindings = AnimationUtility.GetAnimatableBindings(transform.gameObject, gameObject);
          foreach (var binding in bindings) {
            if (binding.type != typeof(GameObject) &&
                binding.type != typeof(Transform)) {
              continue;
            }

            _curves.Add(binding, new AnimationCurve());
          }
        }
      }

      foreach (var pair in _curves) {
        float value;
        bool gotValue = AnimationUtility.GetFloatValue(gameObject, pair.Key, out value);
        if (gotValue) {
          pair.Value.AddKey(Time.time - _startTime, value);
        } else {
          Debug.Log(pair.Key.path + " : " + pair.Key.propertyName + " : " + pair.Key.type.Name);
        }
      }
    }
  }


}
