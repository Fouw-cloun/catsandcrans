using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CatCooks.FoodLab.Editor
{
    public static class CCFoodLabChecks
    {
        [MenuItem("Tools/CatCooks/Food Lab/Run core checks")]
        static void Run()
        {
            var objects=new List<GameObject>();var assets=new List<CCFoodProfile>();int passed=0;
            try
            {
                var profile=ScriptableObject.CreateInstance<CCFoodProfile>();profile.name="TestDough";profile.behaviour=FoodBehaviour.Dough;profile.compliance=.00002f;profile.plasticity=0;profile.tearStretch=2;assets.Add(profile);
                var world=Make(objects,profile);var client=Make(objects,profile);
                int batch=world.Spawn(0,3500,new Vector3(0,.1f,0));
                Require(batch>0&&world.Solver.TotalGrams==3500,"Exact spawn mass");passed++;
                int count=world.Solver.particles.Count;world.maxParticles=count;
                Require(world.Spawn(0,3500,Vector3.zero)==-1&&world.Solver.TotalGrams==3500,"Capacity failure is atomic");passed++;
                world.maxParticles=1280;
                Vector3 gripPoint=world.Solver.particles[0].position;
                world.Execute(new FoodCommand{kind=FoodCommandKind.Scoop,point=gripPoint,radius=.07f},17);
                long held=world.HeldGrams(17);Require(held>0&&world.Solver.TotalGrams==3500,"Scoop doesn't clone/delete mass");passed++;
                Require(!world.Solver.bonds.Any(b=>(b.a.heldBy==17)!=(b.b.heldBy==17)),"Scoop detaches crossing bonds");passed++;
                world.Release(17);Require(world.HeldGrams(17)==0&&world.Solver.TotalGrams==3500,"Release preserves all mass");passed++;
                var bytes=CCFoodSnapshot.Encode(world);
                Require(CCFoodSnapshot.Apply(client,bytes,out string error)&&client.Solver.TotalGrams==3500,"Snapshot round trip: "+error);passed++;
                for(int i=0;i<count;i++)Require(Vector3.Distance(world.Solver.particles[i].position,client.Solver.particles[i].position)<.0002f,"Position quantization");passed++;
                byte[] corrupt=(byte[])bytes.Clone();corrupt[0]=0;
                Require(!CCFoodSnapshot.Apply(client,corrupt,out error)&&client.Solver.TotalGrams==3500,"Malformed snapshot leaves old state intact");passed++;
                var assembler=new FoodSnapshotAssembler();int cut=bytes.Length/2;
                // Separate small transfer test avoids exceeding protocol chunk length.
                byte[] a={1,2,3},b2={4,5,6};Require(assembler.Push(1,1,2,b2)==null,"Partial snapshot");
                Require(assembler.Push(1,1,2,b2)==null,"Duplicate chunk");
                Require(assembler.Push(1,0,2,a).SequenceEqual(new byte[]{1,2,3,4,5,6}),"Out-of-order atomic assembly");passed++;
                int originalBonds=world.Solver.bonds.Count;var node=world.Solver.particles[0];node.position+=Vector3.up*1;node.pinned=true;
                world.Solver.Step(1f/180,world.Profiles,world.boundaries,world.domain);
                Require(world.Solver.bonds.Count<originalBonds&&world.Solver.TotalGrams==3500,"Tear preserves mass");passed++;
                world.Solver.Clear();world.Spawn(0,1750,new Vector3(0,.1f,0));
                var box=new GameObject("CheckFloor");objects.Add(box);var boundary=box.AddComponent<CCFoodBoundary>();boundary.boxSize=new Vector3(3,.1f,3);boundary.transform.position=new Vector3(0,-.05f,0);world.Register(boundary);
                float initialY=world.Solver.particles.Average(p=>p.position.y);
                for(int i=0;i<360;i++)world.Solver.Step(1f/180,world.Profiles,world.boundaries,world.domain);
                Require(world.Solver.particles.All(p=>CCFoodSolver.Finite(p.position)&&p.position.y>=world.particleSpacing*.44f),"Floor collision / finite settlement");passed++;
                Require(world.Solver.particles.Average(p=>p.position.y)<initialY&&world.Solver.TotalGrams==1750,"Gravity and mass conservation");passed++;
                var bowl=new GameObject("CheckBowl");objects.Add(bowl);var bowlBoundary=bowl.AddComponent<CCFoodBoundary>();bowlBoundary.shape=FoodBoundaryShape.Bowl;bowlBoundary.radius=.3f;bowlBoundary.height=.3f;bowlBoundary.Prepare(.01f);
                Vector3 test=new Vector3(.295f,.15f,0);Require(bowlBoundary.Project(ref test,.02f,out _,out _)&&test.x<=.28001f,"Bowl inside wall");passed++;
                test=new Vector3(0,-.001f,0);Require(bowlBoundary.Project(ref test,.02f,out _,out _)&&test.y>=.0199f,"Bowl base");passed++;
                world.Solver.RemoveBatch(world.Solver.particles[0].batch);Require(world.Solver.TotalGrams==0&&world.Solver.bonds.Count==0,"Batch removal cleans links");passed++;
                Debug.Log("Food Lab: PASS "+passed+" проверок. Это проверки ядра; отдельно проверь вид, сеть Host+Client и FPS в Play Mode.");
            }
            catch(Exception e){Debug.LogError("Food Lab checks FAILED after "+passed+" checks: "+e);}
            finally
            {
                foreach(var go in objects)if(go!=null)
                {var w=go.GetComponent<CCFoodWorld>();if(w!=null&&w.Profiles!=null)foreach(var p in w.Profiles)if(p!=null)UnityEngine.Object.DestroyImmediate(p);UnityEngine.Object.DestroyImmediate(go);}
                foreach(var p in assets)UnityEngine.Object.DestroyImmediate(p);
            }
        }
        static CCFoodWorld Make(List<GameObject> objects,CCFoodProfile profile)
        {
            var go=new GameObject("FoodLab temporary check");objects.Add(go);go.SetActive(false);
            var w=go.AddComponent<CCFoodWorld>();w.presets=new[]{profile};w.domain=new Bounds(new Vector3(0,1,0),new Vector3(4,4,4));w.Initialize();return w;
        }
        static void Require(bool ok,string label){if(!ok)throw new InvalidOperationException(label);}
    }
}
