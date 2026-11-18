#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Scoped to the corrected FBX files; does not modify other character assets.
public sealed class CatHammerSyncedImporter : AssetPostprocessor
{
    public override uint GetVersion() { return 3; }
    private bool IsSyncedCat
    {
        get
        {
            return Path.GetExtension(assetPath).Equals(".fbx", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(assetPath).StartsWith(
                    "Cat_Sledgehammer_Synced_", StringComparison.Ordinal);
        }
    }

    private void OnPreprocessModel()
    {
        if (!IsSyncedCat)
            return;

        ModelImporter importer = (ModelImporter)assetImporter;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = null;
        importer.importAnimation = true;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
    }

    private void OnPreprocessAnimation()
    {
        if (!IsSyncedCat)
            return;

        ModelImporter importer = (ModelImporter)assetImporter;
        // These assets contain one full take. Re-read it so metadata from an
        // older FBX cannot reference a take that no longer exists.
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;

        if (clips.Length == 1)
            clips[0].name = Path.GetFileNameWithoutExtension(assetPath);

        foreach (ModelImporterClipAnimation clip in clips)
        {
            clip.loopTime = false; // CatHammerLoop repeats the clip explicitly.
            clip.loopPose = false;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }

    private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
    {
        if (!IsSyncedCat)
            return;

        // Clean Unity's imported clip using Unity's own supported API.
        // The FBX itself is left exactly as Blender exported it.
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Transform))
                continue;

            string path = binding.path ?? string.Empty;
            string nodeName = path.Substring(path.LastIndexOf('/') + 1);
            bool isScale = binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal);
            bool isBone = nodeName.StartsWith("DEF-", StringComparison.Ordinal);
            if (isScale || !isBone)
                AnimationUtility.SetEditorCurve(clip, binding, null);
        }
    }
}
#endif
