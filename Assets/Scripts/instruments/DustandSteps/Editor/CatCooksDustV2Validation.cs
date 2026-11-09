using System;
using UnityEditor;
using UnityEngine;

public static class CatCooksDustV2Validation
{
    [MenuItem("Tools/CatCooks/Dust/Validate expanded profiles and preview")]
    public static void Run()
    {
        if(Application.isPlaying) { Debug.LogWarning("Запусти проверку вне Play Mode."); return; }
        var shader=Shader.Find("CatCooks/Dust and Smoke/Toon Puff");
        if(shader==null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Toon Puff shader is missing or has compiler errors.");
        string root=System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(shader))).Replace('\\','/');
        GameObject go=new GameObject("CCD validation (temporary)") { hideFlags=HideFlags.HideAndDontSave };
        go.SetActive(false);
        var world=go.AddComponent<CatCooksDustSystem>(); world.isolatedPreview=true; world.puffShader=shader;
        int profiles=0,emitters=0;
        try
        {
            go.SetActive(true); world.BeginIsolatedPreview();
            if(CatCooksDustSystem.Active==world) throw new InvalidOperationException("Preview must not become gameplay Active.");
            foreach(string id in AssetDatabase.FindAssets("t:CatCooksDustProfile",new[]{root}))
            {
                var profile=AssetDatabase.LoadAssetAtPath<CatCooksDustProfile>(AssetDatabase.GUIDToAssetPath(id));
                world.ClearParticles(); world.SimulateIsolatedPreview(0);
                world.Emit(CatCooksDustSystem.Effect.Burst,Vector3.zero,Vector3.up,Vector3.zero,1,profile);
                for(int i=0;i<4;i++) world.SimulateIsolatedPreview(.04f);
                Inspect(world,profile.name); profiles++;
            }
            foreach(string id in AssetDatabase.FindAssets("t:Prefab",new[]{root}))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(id));
                var emitter=prefab.GetComponent<CatCooksDustEmitter>(); if(emitter==null) continue;
                world.ClearParticles(); world.SimulateIsolatedPreview(0);
                emitter.EmitInto(world,Vector3.zero,Quaternion.identity);
                for(int i=0;i<8;i++)
                { if(emitter.loop) emitter.EmitInto(world,Vector3.zero,Quaternion.identity); world.SimulateIsolatedPreview(.02f); }
                Inspect(world,prefab.name); emitters++;
            }
            world.ClearParticles(); world.SimulateIsolatedPreview(0);
            world.Emit(CatCooksDustSystem.Effect.Burst,Vector3.zero,Vector3.up,Vector3.zero,delay:.3f);
            world.SimulateIsolatedPreview(.1f);
            if(world.GetComponentInChildren<MeshRenderer>().enabled) throw new InvalidOperationException("Delayed particles rendered too early.");
            for(int i=0;i<7;i++) world.SimulateIsolatedPreview(.05f);
            Inspect(world,"delayed burst");
            Debug.Log($"Dust expanded: {profiles} profiles, {emitters} emitters, delay, mesh channels and isolated preview passed. Inspect artistic appearance in the preview.");
        }
        finally { world.StopPreview(); UnityEngine.Object.DestroyImmediate(go); }
    }
    static void Inspect(CatCooksDustSystem world,string label)
    {
        var filter=world.GetComponentInChildren<MeshFilter>(); var mesh=filter==null ? null : filter.sharedMesh;
        if(mesh==null || mesh.vertexCount==0) throw new InvalidOperationException(label+": no visible geometry");
        var style=new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(3,style);
        if(style.Count!=mesh.vertexCount || mesh.colors.Length!=mesh.vertexCount || mesh.normals.Length!=mesh.vertexCount)
            throw new InvalidOperationException(label+": mismatched vertex channels");
        foreach(var v in mesh.vertices) if(!CatCooksDustUtility.Finite(v)) throw new InvalidOperationException(label+": non-finite vertex");
        foreach(var n in mesh.normals) if(!CatCooksDustUtility.Finite(n) || n.sqrMagnitude<.5f) throw new InvalidOperationException(label+": invalid normal");
        foreach(var i in mesh.triangles) if(i<0 || i>=mesh.vertexCount) throw new InvalidOperationException(label+": invalid index");
    }
}
