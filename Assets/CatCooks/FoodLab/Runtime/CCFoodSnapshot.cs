using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CatCooks.FoodLab
{
    public static class CCFoodSnapshot
    {
        const int Magic=0x43434631;
        // Fixed record: id 4 + batch 4 + grams 4 + profile 1 + flags 1 + quantized xyz 6 = 20 bytes.
        // Do not feed a partially received snapshot to Apply().
        public static byte[] Encode(CCFoodWorld world)
        {
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream))
            {
                w.Write(Magic);w.Write(1);w.Write(Fingerprint(world));w.Write(world.Solver.NextId);w.Write(world.Solver.NextBatch);
                w.Write(world.Solver.particles.Count);
                Vector3 min=world.domain.min,size=world.domain.size;
                foreach(var p in world.Solver.particles)
                {
                    w.Write(p.id);w.Write(p.batch);w.Write(p.grams);w.Write((byte)p.profile);
                    w.Write((byte)((p.pinned?1:0)|(p.heldBy>=0?2:0)));
                    WriteAxis(w,p.position.x,min.x,size.x);WriteAxis(w,p.position.y,min.y,size.y);WriteAxis(w,p.position.z,min.z,size.z);
                }
                return stream.ToArray();
            }
        }
        static void WriteAxis(BinaryWriter w,float v,float min,float size){w.Write((ushort)Mathf.RoundToInt(Mathf.Clamp01((v-min)/Mathf.Max(size,.001f))*65535));}
        static float ReadAxis(BinaryReader r,float min,float size)=>min+r.ReadUInt16()/65535f*size;
        public static bool Apply(CCFoodWorld world,byte[] bytes,out string error)
        {
            error=null;
            if(bytes==null||bytes.Length<24||bytes.Length>24+20*4096){error="Неверный размер слепка";return false;}
            try
            {
                using(var stream=new MemoryStream(bytes))using(var r=new BinaryReader(stream))
                {
                    if(r.ReadInt32()!=Magic||r.ReadInt32()!=1||r.ReadUInt32()!=Fingerprint(world))throw new InvalidDataException("Версия, профили или границы стендов не совпадают");
                    int nextId=r.ReadInt32(),nextBatch=r.ReadInt32(),count=r.ReadInt32();
                    if(count<0||count>world.maxParticles||bytes.Length!=24+count*20)throw new InvalidDataException("Число частиц вне бюджета");
                    var pending=new List<FoodParticle>(count);var ids=new HashSet<int>();
                    var old=new Dictionary<int,FoodParticle>();foreach(var p in world.Solver.particles)old[p.id]=p;
                    Vector3 min=world.domain.min,size=world.domain.size;
                    for(int k=0;k<count;k++)
                    {
                        int id=r.ReadInt32(),batch=r.ReadInt32(),grams=r.ReadInt32(),profile=r.ReadByte(),flags=r.ReadByte();
                        Vector3 pos=new Vector3(ReadAxis(r,min.x,size.x),ReadAxis(r,min.y,size.y),ReadAxis(r,min.z,size.z));
                        if(id<=0||id>=nextId||batch<=0||batch>=nextBatch||grams<=0||grams>200000||profile>=world.Profiles.Length||!ids.Add(id))throw new InvalidDataException("Повреждена запись частицы");
                        pending.Add(new FoodParticle{id=id,batch=batch,grams=grams,profile=profile,position=pos,target=pos,display=old.TryGetValue(id,out var previous)?previous.display:pos,pinned=(flags&1)!=0});
                    }
                    // Commit only after the ENTIRE snapshot has validated; failure leaves the old state untouched.
                    world.Solver.Clear();world.Solver.NextId=nextId;world.Solver.NextBatch=nextBatch;world.Solver.particles.AddRange(pending);
                    return true;
                }
            }
            catch(Exception e) when(e is IOException||e is ArgumentException||e is OverflowException)
            {error=e.Message;return false;}
        }
        public static uint Fingerprint(CCFoodWorld world)
        {
            unchecked
            {
                // Binary floats: no locale-dependent ToString or randomized string.GetHashCode.
                using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream))
                {
                    Vector3 min=world.domain.min,size=world.domain.size;
                    w.Write(min.x);w.Write(min.y);w.Write(min.z);w.Write(size.x);w.Write(size.y);w.Write(size.z);w.Write(world.particleSpacing);
                    foreach(var p in world.presets)
                    {
                        w.Write(p==null?"null":p.name);if(p==null)continue;
                        w.Write((int)p.behaviour);w.Write(p.body.r);w.Write(p.body.g);w.Write(p.body.b);
                        w.Write(p.fleck.r);w.Write(p.fleck.g);w.Write(p.fleck.b);w.Write(p.roughness);w.Write(p.inclusions);
                    }
                    uint h=2166136261;foreach(byte b in stream.ToArray())h=(h^b)*16777619;return h;
                }
            }
        }
    }
    public sealed class FoodSnapshotAssembler
    {
        uint sequence;int received,total;byte[][] pieces;
        public void Reset(){sequence=0;received=total=0;pieces=null;}
        public byte[] Push(uint seq,int index,int count,byte[] data)
        {
            if(count<1||count>128||index<0||index>=count||data==null||data.Length>900)return null;
            if(pieces==null || (int)(seq-sequence)>0){sequence=seq;pieces=new byte[count][];received=total=0;}
            if(seq!=sequence||pieces.Length!=count||pieces[index]!=null)return null;
            pieces[index]=data;received++;total+=data.Length;
            if(received!=count)return null;
            var result=new byte[total];int offset=0;foreach(var piece in pieces){Buffer.BlockCopy(piece,0,result,offset,piece.Length);offset+=piece.Length;}
            return result;
        }
    }
}
