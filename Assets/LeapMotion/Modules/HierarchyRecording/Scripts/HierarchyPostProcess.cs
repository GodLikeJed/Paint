﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEditor;
using Leap.Unity;
using Leap.Unity.Query;
using Leap.Unity.Attributes;

public class HierarchyPostProcess : MonoBehaviour {

  [MinValue(0)]
  public float positionMaxError = 0.005f;

  [Range(0, 90)]
  public float rotationMaxError = 1;

  [MinValue(1)]
  public float scaleMaxError = 1.1f;

  [Range(0, 1)]
  public float colorHueMaxError = 0.05f;

  [Range(0, 1)]
  public float colorSaturationMaxError = 0.05f;

  [Range(0, 1)]
  public float colorValueMaxError = 0.05f;

  [Range(0, 1)]
  public float colorAlphaMaxError = 0.05f;

  [MinValue(0)]
  public float genericMaxError = 0.05f;

  public void BuildPlaybackPrefab() {
    var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

    var track = timeline.CreateTrack<AnimationTrack>(null, "Playback Animation");

    var clip = generateCompressedClip();

    var timelineClip = track.CreateClip(clip);
    timelineClip.asset = clip;
    timelineClip.underlyingAsset = clip;

    AssetDatabase.CreateAsset(timeline, "Assets/LeapMotion/Modules/HierarchyRecording/RecordingTimeline.asset");
    AssetDatabase.AddObjectToAsset(track, timeline);
    AssetDatabase.AddObjectToAsset(clip, timeline);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    foreach (var recording in GetComponentsInChildren<RecordedData>(includeInactive: true)) {
      DestroyImmediate(recording);
    }

    GameObject myGameObject = gameObject;

    DestroyImmediate(this);

    var director = myGameObject.AddComponent<PlayableDirector>();
    director.playableAsset = timeline;

    var animator = myGameObject.AddComponent<Animator>();
    director.SetGenericBinding(track.outputs.Query().First().sourceObject, animator);

    PrefabUtility.CreatePrefab("Assets/LeapMotion/Modules/HierarchyRecording/Recording.prefab", myGameObject);
  }

  private AnimationClip generateCompressedClip() {
    var bindingMap = new Dictionary<EditorCurveBinding, AnimationCurve>();


    try {
      var recordings = GetComponentsInChildren<RecordedData>(includeInactive: true);

      for (int i = 0; i < recordings.Length; i++) {
        var recordingData = recordings[i];
        var toCompress = new Dictionary<EditorCurveBinding, AnimationCurve>();

        foreach (var bindingData in recordingData.data) {
          EditorUtility.DisplayProgressBar("Compressing data",
                                           "Compressing " + recordingData.name + "\n" + bindingData.propertyName,
                                           i / (float)recordings.Length);

          Type type = recordingData.GetComponents<Component>().
                                    Query().
                                    Select(c => c.GetType()).
                                    Concat(typeof(GameObject)).
                                    First(t => t.Name == bindingData.typeName);

          var binding = EditorCurveBinding.FloatCurve(bindingData.path, type, bindingData.propertyName);
          toCompress[binding] = bindingData.curve;
        }

        doCompression(recordingData, toCompress, bindingMap);
      }
    } finally {
      EditorUtility.ClearProgressBar();
    }

    var clip = new AnimationClip();
    foreach (var binding in bindingMap) {
      AnimationUtility.SetEditorCurve(clip, binding.Key, binding.Value);
    }

    return clip;
  }

  private void doCompression(RecordedData recordingData,
                             Dictionary<EditorCurveBinding, AnimationCurve> toCompress,
                             Dictionary<EditorCurveBinding, AnimationCurve> bindingMap) {
    var propertyToMaxError = calculatePropertyErrors(recordingData);

    List<EditorCurveBinding> bindings;

    //First do rotations
    bindings = toCompress.Keys.Query().ToList();
    foreach (var wBinding in bindings) {
      if (!wBinding.propertyName.EndsWith(".w")) {
        continue;
      }

      string property = wBinding.propertyName.Substring(0, wBinding.propertyName.Length - 2);
      string xProp = property + ".x";
      string yProp = property + ".y";
      string zProp = property + ".z";

      var xMaybe = toCompress.Keys.Query().FirstOrNone(t => t.propertyName == xProp);
      var yMaybe = toCompress.Keys.Query().FirstOrNone(t => t.propertyName == yProp);
      var zMaybe = toCompress.Keys.Query().FirstOrNone(t => t.propertyName == zProp);

      Maybe.MatchAll(xMaybe, yMaybe, zMaybe, (xBinding, yBinding, zBinding) => {
        float maxAngleError;
        if (!propertyToMaxError.TryGetValue(property, out maxAngleError)) {
          maxAngleError = rotationMaxError;
        }

        AnimationCurve compressedX, compressedY, compressedZ, compressedW;
        AnimationCurveUtil.CompressRotations(toCompress[xBinding],
                                             toCompress[yBinding],
                                             toCompress[zBinding],
                                             toCompress[wBinding],
                                         out compressedX,
                                         out compressedY,
                                         out compressedZ,
                                         out compressedW,
                                             maxAngleError);

        bindingMap[xBinding] = compressedX;
        bindingMap[yBinding] = compressedY;
        bindingMap[zBinding] = compressedZ;
        bindingMap[wBinding] = compressedW;

        toCompress.Remove(xBinding);
        toCompress.Remove(yBinding);
        toCompress.Remove(zBinding);
        toCompress.Remove(wBinding);
      });
    }

    //Next do scales
    bindings = toCompress.Keys.Query().ToList();
    foreach (var binding in bindings) {
      if (!binding.propertyName.EndsWith(".x") &&
          !binding.propertyName.EndsWith(".y") &&
          !binding.propertyName.EndsWith(".z")) {
        continue;
      }

      if (!binding.propertyName.Contains("LocalScale")) {
        continue;
      }

      bindingMap[binding] = AnimationCurveUtil.CompressScale(toCompress[binding], scaleMaxError);
      toCompress.Remove(binding);
    }

    //Next do positions
    bindings = toCompress.Keys.Query().ToList();
    foreach (var xBinding in bindings) {
      if (!xBinding.propertyName.EndsWith(".x")) {
        continue;
      }

      string property = xBinding.propertyName.Substring(0, xBinding.propertyName.Length - 2);
      string yProp = property + ".y";
      string zProp = property + ".z";

      var yMaybe = toCompress.Keys.Query().FirstOrNone(t => t.propertyName == yProp);
      var zMaybe = toCompress.Keys.Query().FirstOrNone(t => t.propertyName == zProp);

      Maybe.MatchAll(yMaybe, zMaybe, (yBinding, zBinding) => {
        float maxDistanceError;
        if (!propertyToMaxError.TryGetValue(property, out maxDistanceError)) {
          maxDistanceError = positionMaxError;
        }

        AnimationCurve compressedX, compressedY, compressedZ;
        AnimationCurveUtil.CompressPositions(toCompress[xBinding],
                                              toCompress[yBinding],
                                              toCompress[zBinding],
                                          out compressedX,
                                          out compressedY,
                                          out compressedZ,
                                              maxDistanceError);

        bindingMap[xBinding] = compressedX;
        bindingMap[yBinding] = compressedY;
        bindingMap[zBinding] = compressedZ;

        toCompress.Remove(xBinding);
        toCompress.Remove(yBinding);
        toCompress.Remove(zBinding);
      });
    }

    //Next do positions
    bindings = toCompress.Keys.Query().ToList();
    foreach (var rBinding in bindings) {
      if (!rBinding.propertyName.EndsWith(".r")) {
        continue;
      }

      string property = rBinding.propertyName.Substring(0, rBinding.propertyName.Length - 2);
      string gProp = property + ".g";
      string bProp = property + ".b";

      var gMaybe = toCompress.Keys.Query().FirstOrNone(t => t.propertyName == gProp);
      var bMaybe = toCompress.Keys.Query().FirstOrNone(t => t.propertyName == bProp);

      Maybe.MatchAll(gMaybe, bMaybe, (gBinding, bBinding) => {
        AnimationCurve compressedR, compressedG, compressedB;
        AnimationCurveUtil.CompressColorsHSV(toCompress[rBinding],
                                             toCompress[gBinding],
                                             toCompress[bBinding],
                                         out compressedR,
                                         out compressedG,
                                         out compressedB,
                                             colorHueMaxError,
                                             colorSaturationMaxError,
                                             colorValueMaxError);

        bindingMap[rBinding] = compressedR;
        bindingMap[gBinding] = compressedG;
        bindingMap[bBinding] = compressedB;
      });
    }

    //Then to color alpha
    bindings = toCompress.Keys.Query().ToList();
    foreach (var aBinding in bindings) {
      if (!aBinding.propertyName.EndsWith(".a")) {
        continue;
      }

      var compressedA = AnimationCurveUtil.Compress(toCompress[aBinding], colorAlphaMaxError);

      toCompress.Remove(aBinding);
      bindingMap[aBinding] = compressedA;
    }

    //Then everything else
    bindings = toCompress.Keys.Query().ToList();
    foreach (var binding in bindings) {
      float maxError;
      if (!propertyToMaxError.TryGetValue(binding.propertyName, out maxError)) {
        maxError = genericMaxError;
      }

      var compressedCurve = AnimationCurveUtil.Compress(toCompress[binding], maxError);

      toCompress.Remove(binding);
      bindingMap[binding] = compressedCurve;
    }
  }

  private Dictionary<string, float> calculatePropertyErrors(RecordedData recordingData) {
    var propertyToMaxError = new Dictionary<string, float>();
    Transform currTransform = recordingData.transform;
    while (currTransform != null) {
      var compressionSettings = currTransform.GetComponent<PropertyCompression>();
      if (compressionSettings != null) {
        foreach (var setting in compressionSettings.compressionOverrides) {
          if (!propertyToMaxError.ContainsKey(setting.propertyName)) {
            propertyToMaxError.Add(setting.propertyName, setting.maxError);
          }
        }
      }
      currTransform = currTransform.parent;
    }

    return propertyToMaxError;
  }
}
