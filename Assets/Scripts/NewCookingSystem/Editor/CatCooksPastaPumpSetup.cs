#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using CatCooks.Food.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;

namespace CatCooks.Food
{
    /// <summary>
    /// ПАСТА: растворонасос (штукатурная станция) — модель в сцену + стойка с 6 бронзовыми фильерами.
    /// Только вид и коллайдеры; механика (замес, давление, нож, шланг) — потом. Префабы не создаём.
    ///   Pasta_Pump        — рама на колёсах, бункер с решёткой, шнековая труба, мотор, пульт с рычагом скорости,
    ///                       шланг в жёлтую полоску, на конце — Nozzle_Head (бронзовая головка с ножом).
    ///   Pasta_Die_Rack    — фанерная стойка с фильерами: спагетти, пенне, ригатони, фузилли, лингвини, звёздочки.
    /// Цвет — в вершинах (мультяшный материал игры).
    /// </summary>
    public static class CatCooksPastaPumpSetup
    {
        private const string Root = "CatCooks/Паста/";
        private const string ModelDir = "Assets/CatCooks/Food/Pasta";

        private static readonly Color Yellow = new Color(0.98f, 0.76f, 0.10f), Orange = new Color(0.96f, 0.45f, 0.08f),
                                      Steel = new Color(0.62f, 0.64f, 0.66f), DarkSteel = new Color(0.30f, 0.31f, 0.33f),
                                      Rubber = new Color(0.12f, 0.12f, 0.13f), Black = new Color(0.08f, 0.08f, 0.09f),
                                      Bronze = new Color(0.74f, 0.52f, 0.26f), BronzeDark = new Color(0.55f, 0.36f, 0.16f),
                                      Hole = new Color(0.07f, 0.06f, 0.05f), Ply = new Color(0.80f, 0.64f, 0.42f),
                                      Red = new Color(0.86f, 0.12f, 0.10f), Green = new Color(0.20f, 0.70f, 0.25f), White = new Color(0.95f, 0.95f, 0.93f);

        [MenuItem(Root + "1. Растворонасос с фильерами — модель в сцену", priority = 280)]
        public static void MakePump()
        {
            var log = new StringBuilder();
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            Material mat = DS.PlateMat(log);

            // ─── насос ───
            GameObject pump = SceneRoot("Pasta_Pump", at);
            Part(pump.transform, "Frame", BuildFrame(), mat, log, true);
            Part(pump.transform, "Hopper", BuildHopper(), mat, log, true);
            Part(pump.transform, "Auger_Motor", BuildAuger(), mat, log, true);
            GameObject box = Part(pump.transform, "Control_Box", BuildControlBox(), mat, log, true);
            box.transform.localPosition = new Vector3(0.28f, 0.50f, -0.33f);
            GameObject lever = Part(box.transform, "Speed_Lever", BuildLever(), mat, log, false);
            lever.transform.localPosition = new Vector3(0.05f, 0.11f, 0f);
            lever.transform.localRotation = Quaternion.Euler(0f, 0f, -30f);

            List<Vector3> hosePath = HosePath();
            Part(pump.transform, "Hose", BuildHose(hosePath), mat, log, false);
            GameObject head = Part(pump.transform, "Nozzle_Head", BuildHead(), mat, log, true);
            Vector3 end = hosePath[hosePath.Count - 1], dir = (end - hosePath[hosePath.Count - 2]).normalized;
            head.transform.localPosition = end;
            head.transform.localRotation = Quaternion.FromToRotation(Vector3.right, dir);
            Transform outP = DS.FindOrCreate(head.transform, "Die_Socket");
            outP.localPosition = new Vector3(0.17f, 0f, 0f);
            outP.localRotation = Quaternion.identity;
            Transform hopIn = DS.FindOrCreate(pump.transform, "Hopper_In");
            hopIn.localPosition = new Vector3(0f, 0.95f, 0f);
            log.AppendLine("✔ «Pasta_Pump» — растворонасос: жёлтая рама на колёсах, оранжевый бункер с решёткой, шнековая труба и мотор,");
            log.AppendLine("   пульт (красная — стоп, зелёная — пуск, рычаг скорости Speed_Lever), шланг в жёлтую полоску, на конце Nozzle_Head");
            log.AppendLine("   (бронзовая головка с ножом). Точки на будущее: Hopper_In (куда сыпать), Nozzle_Head/Die_Socket (куда ставится фильера).");

            // ─── стойка с фильерами ───
            GameObject rack = SceneRoot("Pasta_Die_Rack", at + new Vector3(0f, 0f, 0.9f));
            Part(rack.transform, "Board", BuildRackBoard(), mat, log, true);
            var dies = new[]
            {
                new KeyValuePair<PastaShape, Color>(PastaShape.Spaghetti, new Color(0.95f, 0.85f, 0.25f)),
                new KeyValuePair<PastaShape, Color>(PastaShape.Penne, new Color(0.90f, 0.30f, 0.15f)),
                new KeyValuePair<PastaShape, Color>(PastaShape.Rigatoni, new Color(0.55f, 0.30f, 0.75f)),
                new KeyValuePair<PastaShape, Color>(PastaShape.Fusilli, new Color(0.20f, 0.65f, 0.30f)),
                new KeyValuePair<PastaShape, Color>(PastaShape.Linguine, new Color(0.20f, 0.50f, 0.90f)),
                new KeyValuePair<PastaShape, Color>(PastaShape.Stelline, new Color(0.95f, 0.95f, 0.95f)),
            };
            for (int i = 0; i < dies.Length; i++)
            {
                PastaShape s = dies[i].Key;
                GameObject d = Part(rack.transform, "Die_" + s, BuildDie(s, dies[i].Value), mat, log, false);
                d.transform.localPosition = new Vector3(-0.30f + i * 0.12f, 0.115f, 0f);
                d.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);       // лицом вперёд (+Z)
                foreach (Collider c in d.GetComponents<Collider>()) if (!(c is MeshCollider)) Object.DestroyImmediate(c);
                MeshCollider mc = DS.Need<MeshCollider>(d); mc.sharedMesh = d.GetComponent<MeshFilter>().sharedMesh; mc.convex = true;
                PastaDie pd = DS.Need<PastaDie>(d);
                pd.EditorSet(s, dies[i].Value);
                EditorUtility.SetDirty(pd);
            }
            log.AppendLine("✔ «Pasta_Die_Rack» — фанерная стойка, 6 бронзовых фильер (метка цветом на ободе):");
            log.AppendLine("   Спагетти (жёлтая) · Пенне (красная) · Ригатони (фиолетовая) · Фузилли (зелёная) · Лингвини (синяя) · Звёздочки (белая)");

            Selection.objects = new Object[] { pump, rack };
            EditorSceneManager.MarkSceneDirty(pump.scene);
            log.AppendLine("\nПовторный запуск обновляет модели на месте. Меши — в " + ModelDir + ".");
            log.AppendLine("Механики пока нет: это вид и коллайдеры. Сохрани префабами, если нужно.");
            DS.Done("Паста — растворонасос", log);
        }

        // ───────────────────────── части ─────────────────────────

        private static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, StringBuilder log, bool boxCollider)
        {
            Transform t = DS.FindOrCreate(parent, name);
            GameObject go = t.gameObject;
            Mesh saved = SaveMesh(mesh, "PastaPump_" + parent.name + "_" + name, log);
            DS.Need<MeshFilter>(go).sharedMesh = saved;
            DS.Need<MeshRenderer>(go).sharedMaterials = new[] { mat };
            if (boxCollider)
            {
                BoxCollider bc = DS.Need<BoxCollider>(go);
                bc.center = saved.bounds.center; bc.size = saved.bounds.size;
            }
            return go;
        }

        private static Mesh BuildFrame()
        {
            var b = new MB();
            // рама: два лонжерона, поперечины, полоса «осторожно» спереди
            for (int s = -1; s <= 1; s += 2)
                b.Box(M(new Vector3(0f, 0.25f, s * 0.25f)), new Vector3(1.3f, 0.05f, 0.05f), Yellow);
            b.Box(M(new Vector3(-0.6f, 0.25f, 0f)), new Vector3(0.05f, 0.05f, 0.55f), Yellow);
            for (int k = 0; k < 8; k++)
                b.Box(M(new Vector3(0.62f, 0.25f, -0.2625f + k * 0.075f)), new Vector3(0.06f, 0.06f, 0.075f), k % 2 == 0 ? Yellow : Black);
            // передние ноги с пятками
            for (int s = -1; s <= 1; s += 2)
            {
                b.Box(M(new Vector3(0.55f, 0.125f, s * 0.25f)), new Vector3(0.05f, 0.25f, 0.05f), Yellow);
                b.Box(M(new Vector3(0.55f, 0.01f, s * 0.25f)), new Vector3(0.12f, 0.02f, 0.12f), DarkSteel);
            }
            // ось и колёса сзади
            b.Cyl(M(new Vector3(-0.5f, 0.16f, -0.36f), Quaternion.Euler(90f, 0f, 0f)), 0.02f, 0.02f, 0.72f, 10, DarkSteel, DarkSteel);
            for (int s = -1; s <= 1; s += 2)
            {
                Matrix4x4 w = M(new Vector3(-0.5f, 0.16f, s * 0.33f - 0.04f), Quaternion.Euler(90f, 0f, 0f));
                b.Cyl(w, 0.16f, 0.16f, 0.08f, 24, Rubber, Rubber);
                b.Cyl(M(new Vector3(-0.5f, 0.16f, s * 0.33f + (s > 0 ? 0.041f : -0.081f)), Quaternion.Euler(90f, 0f, 0f)), 0.08f, 0.08f, 0.04f, 16, Yellow, Steel);
                b.Box(M(new Vector3(-0.5f, 0.21f, s * 0.25f)), new Vector3(0.05f, 0.1f, 0.05f), Yellow);
            }
            // ручки для перевозки
            for (int s = -1; s <= 1; s += 2)
                b.Tube(new List<Vector3> { new Vector3(-0.63f, 0.25f, s * 0.22f), new Vector3(-0.72f, 0.5f, s * 0.22f), new Vector3(-0.8f, 0.82f, s * 0.22f) }, 0.018f, 10, i => Yellow);
            b.Cyl(M(new Vector3(-0.8f, 0.82f, -0.24f), Quaternion.Euler(90f, 0f, 0f)), 0.022f, 0.022f, 0.48f, 10, Black, Black);
            // держатель головки шланга (сбоку)
            b.Box(M(new Vector3(0.05f, 0.36f, 0.33f)), new Vector3(0.05f, 0.03f, 0.18f), Yellow);
            b.Box(M(new Vector3(0.05f, 0.3f, 0.26f)), new Vector3(0.05f, 0.12f, 0.03f), Yellow);
            b.Box(M(new Vector3(0.05f, 0.4f, 0.41f)), new Vector3(0.05f, 0.06f, 0.02f), Yellow);
            // опоры под трубу
            for (int s = -1; s <= 1; s += 2)
                b.Box(M(new Vector3(s * 0.3f, 0.3f, 0f)), new Vector3(0.05f, 0.12f, 0.5f), Yellow);
            return b.Build();
        }

        private static Mesh BuildAuger()
        {
            var b = new MB();
            Quaternion alongX = Quaternion.Euler(0f, 0f, -90f);
            // шнековая труба (статор) и фланцы
            b.Cyl(M(new Vector3(-0.42f, 0.42f, 0f), alongX), 0.11f, 0.11f, 0.86f, 24, Steel, DarkSteel);
            foreach (float x in new[] { -0.42f, 0.0f, 0.4f })
                b.Cyl(M(new Vector3(x, 0.42f, 0f), alongX), 0.13f, 0.13f, 0.03f, 24, Orange, Orange);
            // выход и кулачковый замок
            b.Cyl(M(new Vector3(0.44f, 0.42f, 0f), alongX), 0.07f, 0.05f, 0.12f, 16, DarkSteel, DarkSteel);
            b.Cyl(M(new Vector3(0.56f, 0.42f, 0f), alongX), 0.06f, 0.06f, 0.06f, 16, Orange, DarkSteel);
            for (int s = -1; s <= 1; s += 2)
                b.Box(M(new Vector3(0.59f, 0.42f + s * 0.075f, 0f), Quaternion.Euler(0f, 0f, s * 20f)), new Vector3(0.06f, 0.015f, 0.02f), Orange);
            // мотор: корпус с рёбрами, кожух вентилятора, кабель
            b.Box(M(new Vector3(-0.56f, 0.45f, 0f)), new Vector3(0.22f, 0.24f, 0.26f), Orange);
            for (int k = 0; k < 6; k++)
                b.Box(M(new Vector3(-0.56f, 0.45f, -0.105f + k * 0.042f)), new Vector3(0.24f, 0.26f, 0.012f), DarkSteel);
            b.Cyl(M(new Vector3(-0.68f, 0.45f, 0f), Quaternion.Euler(0f, 0f, 90f)), 0.1f, 0.1f, 0.04f, 20, DarkSteel, Black);
            b.Box(M(new Vector3(-0.56f, 0.6f, 0f)), new Vector3(0.12f, 0.06f, 0.1f), DarkSteel);         // клеммная коробка
            b.Box(M(new Vector3(-0.56f, 0.6f, 0.055f)), new Vector3(0.08f, 0.04f, 0.005f), White);        // табличка
            return b.Build();
        }

        private static Mesh BuildHopper()
        {
            var b = new MB();
            // бункер — усечённая пирамида (снаружи оранжевая, внутри тёмная), горловина в трубу
            b.Frustum(new Vector3(0f, 0.52f, 0f), new Vector2(0.28f, 0.2f), new Vector3(0f, 0.95f, 0f), new Vector2(0.62f, 0.52f), Orange, DarkSteel);
            b.Box(M(new Vector3(0f, 0.95f, 0.26f)), new Vector3(0.66f, 0.03f, 0.03f), Yellow);
            b.Box(M(new Vector3(0f, 0.95f, -0.26f)), new Vector3(0.66f, 0.03f, 0.03f), Yellow);
            b.Box(M(new Vector3(0.31f, 0.95f, 0f)), new Vector3(0.03f, 0.03f, 0.52f), Yellow);
            b.Box(M(new Vector3(-0.31f, 0.95f, 0f)), new Vector3(0.03f, 0.03f, 0.52f), Yellow);
            // решётка сверху
            for (int k = -4; k <= 4; k++)
                b.Box(M(new Vector3(k * 0.065f, 0.955f, 0f)), new Vector3(0.012f, 0.012f, 0.5f), Steel);
            b.Box(M(new Vector3(0f, 0.955f, 0f)), new Vector3(0.6f, 0.012f, 0.012f), Steel);
            // полоса «осторожно» на передней стенке
            for (int k = 0; k < 6; k++)
                b.Box(M(new Vector3(0.235f, 0.86f, -0.2f + k * 0.08f), Quaternion.Euler(0f, 0f, 38f)), new Vector3(0.005f, 0.06f, 0.08f), k % 2 == 0 ? Black : Yellow);
            b.Cyl(M(new Vector3(0f, 0.47f, 0f)), 0.1f, 0.13f, 0.06f, 20, Orange, Orange);
            return b.Build();
        }

        private static Mesh BuildControlBox()
        {
            var b = new MB();
            b.Box(M(new Vector3(0f, 0f, 0f)), new Vector3(0.18f, 0.22f, 0.08f), Steel);
            b.Box(M(new Vector3(0f, 0.115f, 0f)), new Vector3(0.2f, 0.01f, 0.1f), DarkSteel);
            Quaternion face = Quaternion.Euler(-90f, 0f, 0f);       // кнопки смотрят наружу (−Z)
            b.Cyl(M(new Vector3(-0.04f, 0.03f, -0.04f), face), 0.025f, 0.025f, 0.02f, 14, Red, Red);      // стоп
            b.Cyl(M(new Vector3(-0.04f, 0.03f, -0.06f), face), 0.03f, 0.03f, 0.008f, 14, Red, Red);
            b.Cyl(M(new Vector3(0.04f, 0.03f, -0.04f), face), 0.018f, 0.018f, 0.012f, 14, Green, Green);  // пуск
            b.Box(M(new Vector3(0f, -0.06f, -0.041f)), new Vector3(0.12f, 0.04f, 0.002f), Yellow);         // табличка
            // кронштейн к раме
            b.Box(M(new Vector3(0f, -0.17f, 0.06f)), new Vector3(0.04f, 0.14f, 0.04f), Yellow);
            return b.Build();
        }

        private static Mesh BuildLever()
        {
            var b = new MB();
            b.Cyl(M(Vector3.zero), 0.008f, 0.008f, 0.09f, 8, Steel, Steel);
            b.Cyl(M(new Vector3(0f, 0.09f, 0f)), 0.02f, 0.02f, 0.035f, 12, Orange, Orange);
            return b.Build();
        }

        private static List<Vector3> HosePath()
        {
            var pts = new List<Vector3>
            {
                new Vector3(0.6f, 0.42f, 0f), new Vector3(0.78f, 0.36f, 0.02f), new Vector3(0.92f, 0.14f, 0.15f),
                new Vector3(0.88f, 0.05f, 0.45f), new Vector3(0.55f, 0.05f, 0.62f), new Vector3(0.2f, 0.07f, 0.6f),
                new Vector3(-0.12f, 0.18f, 0.5f), new Vector3(-0.15f, 0.38f, 0.38f), new Vector3(-0.05f, 0.42f, 0.36f)
            };
            return CatmullRom(pts, 8);
        }

        private static Mesh BuildHose(List<Vector3> path)
        {
            var b = new MB();
            int n = path.Count;
            b.Tube(path, 0.038f, 14, i => (i % 9 < 2) ? Yellow : Rubber);
            // хомуты на концах
            b.TubeSlice(path, 1, 3, 0.044f, 14, Steel);
            b.TubeSlice(path, n - 4, n - 2, 0.044f, 14, Steel);
            return b.Build();
        }

        private static Mesh BuildHead()
        {
            var b = new MB();
            Quaternion alongX = Quaternion.Euler(0f, 0f, -90f);
            b.Cyl(M(new Vector3(-0.08f, 0f, 0f), alongX), 0.045f, 0.045f, 0.1f, 16, Rubber, Rubber);          // рукоять
            for (int k = 0; k < 4; k++)
                b.Cyl(M(new Vector3(-0.07f + k * 0.022f, 0f, 0f), alongX), 0.048f, 0.048f, 0.008f, 16, Black, Black);
            b.Cyl(M(new Vector3(0.02f, 0f, 0f), alongX), 0.055f, 0.065f, 0.12f, 20, Bronze, BronzeDark);       // корпус головки
            b.Cyl(M(new Vector3(0.14f, 0f, 0f), alongX), 0.075f, 0.075f, 0.03f, 20, Orange, BronzeDark);       // байонет под фильеру
            for (int s = -1; s <= 1; s += 2)
                b.Box(M(new Vector3(0.155f, s * 0.08f, 0f)), new Vector3(0.02f, 0.02f, 0.03f), Orange);
            // нож-срезатель на кронштейне
            b.Box(M(new Vector3(0.18f, 0.05f, 0.06f)), new Vector3(0.012f, 0.012f, 0.06f), DarkSteel);
            b.Box(M(new Vector3(0.18f, 0f, 0.09f), Quaternion.Euler(0f, 0f, 0f)), new Vector3(0.006f, 0.14f, 0.03f), Steel);
            b.Cyl(M(new Vector3(0.12f, 0.06f, 0.06f), Quaternion.Euler(90f, 0f, 0f)), 0.025f, 0.025f, 0.04f, 12, Yellow, DarkSteel);   // моторчик ножа
            // курок
            b.Box(M(new Vector3(-0.05f, -0.06f, 0f), Quaternion.Euler(0f, 0f, 15f)), new Vector3(0.02f, 0.05f, 0.015f), Orange);
            return b.Build();
        }

        private static Mesh BuildRackBoard()
        {
            var b = new MB();
            b.Box(M(new Vector3(0f, 0.02f, 0f)), new Vector3(0.78f, 0.04f, 0.2f), Ply);
            b.Box(M(new Vector3(0f, 0.09f, -0.06f)), new Vector3(0.78f, 0.1f, 0.025f), Ply);
            for (int i = 0; i < 6; i++)
                b.Box(M(new Vector3(-0.30f + i * 0.12f, 0.05f, 0f)), new Vector3(0.09f, 0.02f, 0.05f), Ply * 0.85f);
            for (int k = 0; k < 10; k++)
                b.Box(M(new Vector3(-0.351f + k * 0.078f, 0.02f, 0.101f)), new Vector3(0.078f, 0.04f, 0.002f), k % 2 == 0 ? Yellow : Black);
            return b.Build();
        }

        /// <summary>Фильера: бронзовый диск (ось Y, лицо сверху), отверстия-узор, два ушка байонета, цветная метка на ободе.</summary>
        private static Mesh BuildDie(PastaShape s, Color tag)
        {
            var b = new MB();
            const float R = 0.055f, T = 0.03f;
            float top = T * 0.5f, y = top + 0.0008f;
            b.Cyl(M(new Vector3(0f, -T * 0.5f, 0f)), R, R, T, 32, Bronze, Bronze);
            b.Cyl(M(new Vector3(0f, top, 0f)), R * 0.94f, R * 0.94f, 0.0006f, 32, BronzeDark, BronzeDark);     // фаска
            b.Cyl(M(new Vector3(0f, top + 0.0006f, 0f)), R * 0.9f, R * 0.9f, 0.0001f, 32, Bronze, Bronze);
            for (int k = -1; k <= 1; k += 2)
                b.Box(M(new Vector3(k * (R + 0.006f), 0f, 0f)), new Vector3(0.014f, T * 0.6f, 0.02f), BronzeDark);
            b.Box(M(new Vector3(0f, 0f, -(R + 0.001f))), new Vector3(0.02f, T * 0.8f, 0.004f), tag);           // метка (после поворота — сверху)
            switch (s)
            {
                case PastaShape.Spaghetti:
                    for (int q = -2; q <= 2; q++)
                        for (int r = -2; r <= 2; r++)
                        {
                            float x = (q + r * 0.5f) * 0.017f, z = r * 0.0147f;
                            if (x * x + z * z > 0.042f * 0.042f) continue;
                            b.Disc(new Vector3(x, y, z), 0.0045f, 12, Hole);
                        }
                    break;
                case PastaShape.Penne:
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                        b.Ring(new Vector3(Mathf.Cos(a) * 0.024f, y, Mathf.Sin(a) * 0.024f), 0.006f, 0.012f, 20, Hole);
                    }
                    break;
                case PastaShape.Rigatoni:
                    for (int k = 0; k < 3; k++)
                    {
                        float a = k * Mathf.PI * 2f / 3f + Mathf.PI * 0.5f;
                        Vector3 c = new Vector3(Mathf.Cos(a) * 0.022f, y, Mathf.Sin(a) * 0.022f);
                        b.Ring(c, 0.008f, 0.0155f, 24, Hole);
                        for (int g = 0; g < 10; g++)
                        {
                            float ga = g / 10f * Mathf.PI * 2f;
                            b.Slot(c + new Vector3(Mathf.Cos(ga) * 0.0155f, 0.0003f, Mathf.Sin(ga) * 0.0155f), 0.004f, 0.0018f, ga, Bronze);   // бороздки
                        }
                    }
                    break;
                case PastaShape.Fusilli:
                    for (int arm = 0; arm < 3; arm++)
                        for (int k = 0; k < 9; k++)
                        {
                            float u = k / 8f;
                            float a = arm * Mathf.PI * 2f / 3f + u * 2.4f, r = 0.006f + u * 0.034f;
                            b.Slot(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), 0.009f, 0.0045f, a + Mathf.PI * 0.5f, Hole);
                        }
                    b.Disc(new Vector3(0f, y, 0f), 0.005f, 12, Hole);
                    break;
                case PastaShape.Linguine:
                    for (int k = -2; k <= 2; k++)
                    {
                        float z = k * 0.014f;
                        float half = Mathf.Sqrt(Mathf.Max(0f, 0.044f * 0.044f - z * z));
                        b.Slot(new Vector3(0f, y, z), half * 2f, 0.0042f, 0f, Hole);
                    }
                    break;
                case PastaShape.Stelline:
                    b.Star(new Vector3(0f, y, 0f), 0.009f, 0.004f, 5, Hole);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * Mathf.PI / 3f;
                        b.Star(new Vector3(Mathf.Cos(a) * 0.028f, y, Mathf.Sin(a) * 0.028f), 0.008f, 0.0035f, 5, Hole);
                    }
                    break;
            }
            return b.Build();
        }

        // ───────────────────────── общее ─────────────────────────

        private static Matrix4x4 M(Vector3 p) => Matrix4x4.Translate(p);
        private static Matrix4x4 M(Vector3 p, Quaternion q) => Matrix4x4.TRS(p, q, Vector3.one);

        private static List<Vector3> CatmullRom(List<Vector3> p, int perSeg)
        {
            var o = new List<Vector3>();
            for (int i = 0; i < p.Count - 1; i++)
            {
                Vector3 p0 = p[Mathf.Max(0, i - 1)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(p.Count - 1, i + 2)];
                for (int k = 0; k < perSeg; k++)
                {
                    float t = k / (float)perSeg, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(p[p.Count - 1]);
            return o;
        }

        private static GameObject SceneRoot(string name, Vector3 at)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null && t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.position = at;
            return go;
        }

        private static Mesh SaveMesh(Mesh m, string name, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".asset";
            DS.EnsureDir(ModelDir);
            m.name = name;
            Mesh have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null)
            {
                EditorUtility.CopySerialized(m, have);
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(have);
                return have;
            }
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ меш {path}");
            return m;
        }

        /// <summary>Сборщик меша: цвет в вершинах; грани коробок и торцы — свои вершины (резкие), бока цилиндров и трубок — общие (гладкие).</summary>
        private sealed class MB
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Color> _c = new List<Color>();
            private readonly List<int> _t = new List<int>();

            private int V(Vector3 p, Color c) { _v.Add(p); _c.Add(c); return _v.Count - 1; }

            private void Tri(int a, int b, int c, Vector3 nn)
            {
                Vector3 fn = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
                if (Vector3.Dot(fn, nn) >= 0f) { _t.Add(a); _t.Add(b); _t.Add(c); } else { _t.Add(a); _t.Add(c); _t.Add(b); }
            }

            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn, Color col)
            {
                int i0 = V(a, col), i1 = V(b, col), i2 = V(c, col), i3 = V(d, col);
                Tri(i0, i1, i2, nn); Tri(i0, i2, i3, nn);
            }

            public void Box(Matrix4x4 m, Vector3 size, Color col)
            {
                Vector3 h = size * 0.5f;
                for (int ax = 0; ax < 3; ax++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        Vector3 n = Vector3.zero; n[ax] = sg;
                        Vector3 u = Vector3.zero, w = Vector3.zero;
                        u[(ax + 1) % 3] = 1f; w[(ax + 2) % 3] = 1f;
                        Vector3 c = Vector3.Scale(n, h);
                        Vector3 uu = Vector3.Scale(u, h), ww = Vector3.Scale(w, h);
                        Quad(m.MultiplyPoint3x4(c - uu - ww), m.MultiplyPoint3x4(c + uu - ww), m.MultiplyPoint3x4(c + uu + ww), m.MultiplyPoint3x4(c - uu + ww),
                             m.MultiplyVector(n), col);
                    }
            }

            /// <summary>Цилиндр/конус вдоль локальной Y от 0 до h.</summary>
            public void Cyl(Matrix4x4 m, float r0, float r1, float h, int seg, Color side, Color cap)
            {
                int b0 = _v.Count;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    V(m.MultiplyPoint3x4(d * r0), side);
                    V(m.MultiplyPoint3x4(d * r1 + Vector3.up * h), side);
                }
                for (int j = 0; j < seg; j++)
                {
                    int a = b0 + j * 2;
                    float ang = (j + 0.5f) / seg * Mathf.PI * 2f;
                    Vector3 nn = m.MultiplyVector(new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)));
                    Tri(a, a + 2, a + 1, nn); Tri(a + 1, a + 2, a + 3, nn);
                }
                for (int e = 0; e < 2; e++)
                {
                    float y = e == 0 ? 0f : h, r = e == 0 ? r0 : r1;
                    Vector3 nn = m.MultiplyVector(e == 0 ? Vector3.down : Vector3.up);
                    int ctr = V(m.MultiplyPoint3x4(Vector3.up * y), cap);
                    int r0i = _v.Count;
                    for (int j = 0; j < seg; j++)
                    {
                        float a = j / (float)seg * Mathf.PI * 2f;
                        V(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)), cap);
                    }
                    for (int j = 0; j < seg; j++) Tri(ctr, r0i + j, r0i + (j + 1) % seg, nn);
                }
            }

            /// <summary>Трубка по пути (гладкая), цвет — по номеру кольца.</summary>
            public void Tube(List<Vector3> path, float r, int seg, System.Func<int, Color> col)
            {
                TubeRange(path, 0, path.Count - 1, r, seg, col, true);
            }

            public void TubeSlice(List<Vector3> path, int from, int to, float r, int seg, Color col)
            {
                TubeRange(path, Mathf.Max(0, from), Mathf.Min(path.Count - 1, to), r, seg, i => col, true);
            }

            private void TubeRange(List<Vector3> path, int from, int to, float r, int seg, System.Func<int, Color> col, bool caps)
            {
                if (to - from < 1) return;
                Vector3 prevN = Vector3.zero;
                int b0 = _v.Count;
                for (int i = from; i <= to; i++)
                {
                    Vector3 tan = (path[Mathf.Min(to, i + 1)] - path[Mathf.Max(from, i - 1)]).normalized;
                    Vector3 nrm = prevN.sqrMagnitude < 1e-6f ? Vector3.Cross(tan, Mathf.Abs(tan.y) > 0.9f ? Vector3.right : Vector3.up).normalized
                                                             : Vector3.ProjectOnPlane(prevN, tan).normalized;
                    if (nrm.sqrMagnitude < 1e-6f) nrm = Vector3.Cross(tan, Vector3.forward).normalized;
                    prevN = nrm;
                    Vector3 bin = Vector3.Cross(tan, nrm);
                    Color cc = col(i);
                    for (int j = 0; j <= seg; j++)
                    {
                        float a = j / (float)seg * Mathf.PI * 2f;
                        V(path[i] + (nrm * Mathf.Cos(a) + bin * Mathf.Sin(a)) * r, cc);
                    }
                }
                int rings = to - from + 1;
                for (int i = 0; i < rings - 1; i++)
                    for (int j = 0; j < seg; j++)
                    {
                        int a = b0 + i * (seg + 1) + j, b = a + 1, a2 = a + seg + 1, b2 = a2 + 1;
                        Vector3 mid = (_v[a] + _v[b2]) * 0.5f;
                        Vector3 ctr = (path[from + i] + path[from + i + 1]) * 0.5f;
                        Vector3 nn = mid - ctr;
                        Tri(a, a2, b, nn); Tri(b, a2, b2, nn);
                    }
                if (!caps) return;
                for (int e = 0; e < 2; e++)
                {
                    int i = e == 0 ? from : to;
                    Vector3 tan = (path[Mathf.Min(to, i + 1)] - path[Mathf.Max(from, i - 1)]).normalized * (e == 0 ? -1f : 1f);
                    int ring = b0 + (i - from) * (seg + 1);
                    Color cc = _c[ring];
                    int ctr = V(path[i], cc * 0.7f);
                    int s0 = _v.Count;
                    for (int j = 0; j < seg; j++) V(_v[ring + j], cc * 0.7f);
                    for (int j = 0; j < seg; j++) Tri(ctr, s0 + j, s0 + (j + 1) % seg, tan);
                }
            }

            /// <summary>Усечённая пирамида (бункер): низ и верх — прямоугольники (x, z). Снаружи один цвет, внутри — другой.</summary>
            public void Frustum(Vector3 bc, Vector2 bs, Vector3 tc, Vector2 ts, Color outer, Color inner)
            {
                Vector3[] lo = Rect(bc, bs), hi = Rect(tc, ts);
                Vector3 mid = (bc + tc) * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    int k2 = (k + 1) % 4;
                    Vector3 fc = (lo[k] + lo[k2] + hi[k] + hi[k2]) * 0.25f;
                    Vector3 outN = new Vector3(fc.x - mid.x, 0f, fc.z - mid.z);
                    Quad(lo[k], lo[k2], hi[k2], hi[k], outN, outer);
                    // внутренняя сторона — чуть ближе к оси (не мерцает с наружной, даже если материал двусторонний)
                    Quad(In(lo[k], bc), In(lo[k2], bc), In(hi[k2], tc), In(hi[k], tc), -outN, inner);
                }
            }

            private static Vector3 In(Vector3 p, Vector3 c) => c + new Vector3((p.x - c.x) * 0.97f, p.y - c.y, (p.z - c.z) * 0.97f);

            private static Vector3[] Rect(Vector3 c, Vector2 s)
            {
                float x = s.x * 0.5f, z = s.y * 0.5f;
                return new[] { c + new Vector3(-x, 0f, -z), c + new Vector3(x, 0f, -z), c + new Vector3(x, 0f, z), c + new Vector3(-x, 0f, z) };
            }

            // ─ узоры на лице фильеры (плоско, нормаль вверх) ─

            public void Disc(Vector3 c, float r, int seg, Color col)
            {
                int ctr = V(c, col), s0 = _v.Count;
                for (int j = 0; j < seg; j++) { float a = j / (float)seg * Mathf.PI * 2f; V(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), col); }
                for (int j = 0; j < seg; j++) Tri(ctr, s0 + j, s0 + (j + 1) % seg, Vector3.up);
            }

            public void Ring(Vector3 c, float ri, float ro, int seg, Color col)
            {
                int s0 = _v.Count;
                for (int j = 0; j < seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    V(c + d * ri, col); V(c + d * ro, col);
                }
                for (int j = 0; j < seg; j++)
                {
                    int a = s0 + j * 2, b = s0 + ((j + 1) % seg) * 2;
                    Tri(a, a + 1, b, Vector3.up); Tri(b, a + 1, b + 1, Vector3.up);
                }
            }

            public void Slot(Vector3 c, float len, float w, float angle, Color col)
            {
                Vector3 u = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * len * 0.5f;
                Vector3 v = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * w * 0.5f;
                Quad(c - u - v, c + u - v, c + u + v, c - u + v, Vector3.up, col);
            }

            public void Star(Vector3 c, float ro, float ri, int points, Color col)
            {
                int ctr = V(c, col), s0 = _v.Count, n = points * 2;
                for (int j = 0; j < n; j++)
                {
                    float a = j / (float)n * Mathf.PI * 2f + Mathf.PI * 0.5f, r = j % 2 == 0 ? ro : ri;
                    V(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), col);
                }
                for (int j = 0; j < n; j++) Tri(ctr, s0 + j, s0 + (j + 1) % n, Vector3.up);
            }

            public Mesh Build()
            {
                var m = new Mesh();
                if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(_v); m.SetColors(_c); m.SetTriangles(_t, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
#endif
