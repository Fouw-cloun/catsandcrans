using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Put this file in an Editor folder. Select a scene instance of the rigged cat.
public static class CatHammerSetup
{
    [MenuItem("Tools/Cat Hammer/Equip and play wall strike")]
    static void Wall() => Setup("Wall_Strike");

    [MenuItem("Tools/Cat Hammer/Equip and play floor strike")]
    static void Floor() => Setup("Floor_Strike");

    static void Setup(string strike)
    {
        var selection = Selection.activeGameObject;
        if (!selection || EditorUtility.IsPersistent(selection))
        {
            Debug.LogWarning("Выбери кота в Hierarchy.");
            return;
        }
        var animator = selection.GetComponentInParent<Animator>() ?? selection.GetComponentInChildren<Animator>(true);
        if (!animator) { Debug.LogWarning("На выбранном коте нужен Animator."); return; }
        string name = "Cat_Cartoon_" + strike;
        string path = AssetDatabase.FindAssets(name + " t:Model")
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => p.EndsWith("/" + name + ".fbx", StringComparison.OrdinalIgnoreCase));
        if (path == null) { Debug.LogWarning("Импортируй " + name + ".fbx."); return; }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (!clip) { Debug.LogWarning("Включи Import Animation для " + path); return; }

        var rig = animator.GetComponentsInChildren<Transform>(true)
            .GroupBy(t => t.name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
        var parts = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(p => p.name.StartsWith("Sledgehammer_", StringComparison.Ordinal)).ToArray();
        if (parts.Length != 2 || parts.Any(p => p.bones.Any(b => !b || !rig.ContainsKey(b.name))))
        {
            Debug.LogWarning("Не удалось сопоставить кувалду с костями. Проверь Generic и выключи Optimize Game Objects на модели кота.");
            return;
        }
        // Keep the target's existing fur, clothing and materials. Both hammer pieces
        // share the target's bones, while the mesh carries the authored bind poses.
        var existing = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var container = animator.transform.Find("CartoonHammer");
        foreach (var part in parts)
        {
            if (existing.Any(p => p.name == part.name)) continue;
            if (!container)
            {
                var holder = new GameObject("CartoonHammer");
                Undo.RegisterCreatedObjectUndo(holder, "Equip cat");
                holder.transform.SetParent(animator.transform, false);
                container = holder.transform;
            }
            var obj = new GameObject(part.name);
            Undo.RegisterCreatedObjectUndo(obj, "Equip cat");
            obj.transform.SetParent(container, false);
            var renderer = Undo.AddComponent<SkinnedMeshRenderer>(obj);
            renderer.sharedMesh = part.sharedMesh;
            renderer.sharedMaterials = part.sharedMaterials;
            renderer.bones = part.bones.Select(b => rig[b.name]).ToArray();
            renderer.rootBone = part.rootBone && rig.ContainsKey(part.rootBone.name)
                ? rig[part.rootBone.name] : rig["DEF-hand.R"];
            renderer.localBounds = part.localBounds;
            renderer.updateWhenOffscreen = true;
        }
        var player = animator.GetComponent<CatHammerLoop>() ?? Undo.AddComponent<CatHammerLoop>(animator.gameObject);
        var settings = new SerializedObject(player);
        settings.FindProperty("animationClip").objectReferenceValue = clip;
        settings.ApplyModifiedProperties();
        EditorUtility.SetDirty(player);
        Debug.Log("Кувалда готова. Назначен клип " + clip.name, animator);
    }
}
