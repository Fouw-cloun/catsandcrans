using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace CatCooks.Masonry
{
    [DisallowMultipleComponent,RequireComponent(typeof(MeshFilter),typeof(MeshRenderer))]
    public sealed class CCMasonryWall:MonoBehaviour
    {
        [Range(2,40)] public int Columns=12;
        [Range(1,30)] public int Rows=10;
        [Min(.1f)] public float Width=6,Height=2.5f;
        [Min(.05f)] public float Thickness=.25f;
        [Range(0,.03f)] public float Joint=.012f;
        public int Total=>Mathf.Clamp(Columns,2,40)*Mathf.Clamp(Rows,1,30)+Mathf.Clamp(Rows,1,30)/2;
        Mesh _mesh;
        int _shown=-1;
        readonly List<Vector3> _v=new List<Vector3>();
        readonly List<int> _t=new List<int>();
        static readonly Vector3[] Cube={new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f)};
        static readonly int[] Faces={0,3,2,0,2,1,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,3,7,6,3,6,2,0,1,5,0,5,4};
        public void Show(int count)
        {
            count=Mathf.Clamp(count,0,Total);if(_shown==count&&_mesh!=null)return;
            if(_mesh==null){_mesh=new Mesh{name="Masonry wall (runtime)"};_mesh.indexFormat=IndexFormat.UInt32;GetComponent<MeshFilter>().sharedMesh=_mesh;}
            _shown=count;_v.Clear();_t.Clear();
            int columns=Mathf.Clamp(Columns,2,40),rows=Mathf.Clamp(Rows,1,30),placed=0;
            float unit=Width/columns,h=Height/rows;
            for(int row=0;row<rows&&placed<count;row++)
            {
                bool odd=row%2==1;int cells=columns+(odd?1:0);float x=-Width*.5f;
                for(int col=0;col<cells&&placed<count;col++,placed++)
                {
                    float w=odd&&(col==0||col==cells-1)?unit*.5f:unit;
                    Box(new Vector3(x+w*.5f,(row+.5f)*h,0),new Vector3(Mathf.Max(.01f,w-Joint),Mathf.Max(.01f,h-Joint),Thickness));x+=w;
                }
            }
            _mesh.Clear();_mesh.SetVertices(_v);_mesh.SetTriangles(_t,0);_mesh.RecalculateNormals();_mesh.RecalculateBounds();
        }
        void Box(Vector3 centre,Vector3 size)
        {
            // Separate triangle vertices keep flat faces, one renderer for the complete wall.
            foreach(int corner in Faces){_t.Add(_v.Count);_v.Add(centre+Vector3.Scale(Cube[corner],size));}
        }
        void OnDestroy(){if(_mesh!=null){if(Application.isPlaying)Destroy(_mesh);else DestroyImmediate(_mesh);}}
        void OnDrawGizmos(){Gizmos.color=new Color(1,.6f,.2f,.8f);Matrix4x4 old=Gizmos.matrix;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.DrawWireCube(Vector3.up*Height*.5f,new Vector3(Width,Height,Thickness));Gizmos.matrix=old;}
    }
}
