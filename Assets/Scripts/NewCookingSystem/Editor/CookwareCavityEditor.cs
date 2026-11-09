#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Инспектор и ручки в сцене для Cookware Cavity.
    ///   • красная сфера + стрелки — точка дна (тащи);
    ///   • жёлтый кубик сверху — глубина (край перелива);
    ///   • голубые кубики на краю/у дна — ширина воды (ковш — по X и Z, рюмка — радиус);
    ///   • жёлтый кружок в центре края — сдвиг горловины (ковш).
    /// </summary>
    [CustomEditor(typeof(CookwareCavity))]
    public sealed class CookwareCavityEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var c = (CookwareCavity)target;
            EditorGUILayout.HelpBox("1) Body Renderers — только корпус ёмкости (без проушин, штоков, ног).\n" +
                                    "2) «Найти полость» → проверь рамки в сцене, подтяни ручками.\n" +
                                    "3) «Собрать» — коллайдеры, приём, горловина, горка, жидкость.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("1. Найти полость", GUILayout.Height(28))) { c.EditorScan(); SceneView.RepaintAll(); }
                if (GUILayout.Button("2. Собрать", GUILayout.Height(28))) c.EditorBuild();
            }
            if (GUILayout.Button("Найти и собрать")) { c.EditorScan(); c.EditorBuild(); SceneView.RepaintAll(); }
            EditorGUILayout.LabelField($"Объём полости: {c.Volume * 1000f:0} л");
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        private void OnSceneGUI()
        {
            var c = (CookwareCavity)target;
            var so = new SerializedObject(c);
            SerializedProperty pBottom = so.FindProperty("_bottomLocal");
            SerializedProperty pDepth = so.FindProperty("_depth");
            SerializedProperty pTopHalf = so.FindProperty("_topHalf");
            SerializedProperty pBotHalf = so.FindProperty("_bottomHalf");
            SerializedProperty pShift = so.FindProperty("_topShift");
            bool cone = c.Form == CookwareCavity.Shape.Cone;

            Quaternion F = c.FrameWorld;
            Vector3 right = F * Vector3.right, fwd = F * Vector3.forward, up = F * Vector3.up;
            Vector3 B = c.BottomWorld;
            float D = pDepth.floatValue;
            float size = HandleUtility.GetHandleSize(B) * 0.08f;

            EditorGUI.BeginChangeCheck();

            // Дно
            Vector3 nb = Handles.PositionHandle(B, F);
            Handles.color = Color.red;
            Handles.SphereHandleCap(0, B, F, size * 1.4f, EventType.Repaint);

            // Глубина
            Vector2 sh = cone ? Vector2.zero : pShift.vector2Value;
            Vector3 topC = B + up * D + F * new Vector3(sh.x, 0f, sh.y);
            Handles.color = new Color(1f, 0.85f, 0.2f);
            Vector3 nTop = Handles.Slider(topC, up, size * 1.5f, Handles.CubeHandleCap, 0f);
            Handles.Label(topC + up * size * 3f, $"глубина {D:0.00} м");

            // Сдвиг горловины (ковш)
            Vector3 nShift = topC;
            if (!cone)
                nShift = Handles.Slider2D(topC, up, right, fwd, size * 1.2f, Handles.CircleHandleCap, Vector2.zero);

            // Ширина у края
            Vector2 th = pTopHalf.vector2Value, bh = pBotHalf.vector2Value;
            Handles.color = new Color(0.3f, 0.9f, 1f);
            Vector3 tx = Handles.Slider(topC + right * th.x, right, size, Handles.CubeHandleCap, 0f);
            Vector3 tz = cone ? Vector3.zero : Handles.Slider(topC + fwd * th.y, fwd, size, Handles.CubeHandleCap, 0f);
            // Ширина у дна
            Vector3 bx = Handles.Slider(B + right * bh.x, right, size, Handles.CubeHandleCap, 0f);
            Vector3 bz = cone ? Vector3.zero : Handles.Slider(B + fwd * bh.y, fwd, size, Handles.CubeHandleCap, 0f);

            if (EditorGUI.EndChangeCheck())
            {
                Transform t = c.transform;
                if ((nb - B).sqrMagnitude > 1e-10f)
                {
                    // Дно тянут вверх/вниз — край остаётся на месте (это «срез» дна);
                    // вбок — сдвигается вся полость.
                    float along = Vector3.Dot(nb - B, up);
                    pBottom.vector3Value = t.InverseTransformPoint(nb);
                    pDepth.floatValue = Mathf.Max(0.01f, D - along);
                    B = nb;
                }
                else pDepth.floatValue = Mathf.Max(0.01f, Vector3.Dot(nTop - B, up));
                if (!cone && (nShift - topC).sqrMagnitude > 1e-10f)
                {
                    Vector3 d = nShift - (B + up * D);
                    pShift.vector2Value = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, fwd));
                }
                float ntx = Mathf.Max(0.005f, Vector3.Dot(tx - topC, right));
                float nbx = Mathf.Max(0.005f, Vector3.Dot(bx - B, right));
                if (cone)
                {
                    pTopHalf.vector2Value = new Vector2(ntx, ntx);
                    pBotHalf.vector2Value = new Vector2(nbx, nbx);
                }
                else
                {
                    pTopHalf.vector2Value = new Vector2(ntx, Mathf.Max(0.005f, Vector3.Dot(tz - topC, fwd)));
                    pBotHalf.vector2Value = new Vector2(nbx, Mathf.Max(0.005f, Vector3.Dot(bz - B, fwd)));
                }
                so.ApplyModifiedProperties();
            }
        }
    }
}
#endif
