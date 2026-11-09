using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// НАСТРОЙКА ХОДЬБЫ КОТА В ОДИН КЛИК. Берёт FBX с моделью кота и FBX с анимацией ходьбы,
    /// настраивает импорт (Generic, аватар кота у анимации, цикл, без сдвига корня), вешает на кота в сцене
    /// CatCircleWalker и кладёт туда клип. Префабы не создаются.
    /// </summary>
    public class CatWalkSetupWindow : EditorWindow
    {
        private Object _model, _anim;
        private GameObject _cat;
        private float _radius = 4f, _speed = 1.2f;

        [MenuItem("CatCooks/Кот/Ходьба по кругу: настроить (импорт + скрипт)…", priority = 1000)]
        public static void Open()
        {
            GetWindow<CatWalkSetupWindow>("Кот: ходьба").minSize = new Vector2(420, 230);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("1) FBX с моделью кота, 2) FBX с анимацией ходьбы, 3) кот в сцене. Дальше одна кнопка.", MessageType.Info);
            _model = EditorGUILayout.ObjectField("FBX модели кота", _model, typeof(Object), false);
            _anim = EditorGUILayout.ObjectField("FBX анимации ходьбы", _anim, typeof(Object), false);
            _cat = (GameObject)EditorGUILayout.ObjectField("Кот в сцене", _cat, typeof(GameObject), true);
            _radius = EditorGUILayout.FloatField("Радиус круга, м", _radius);
            _speed = EditorGUILayout.FloatField("Скорость, м/с", _speed);
            using (new EditorGUI.DisabledScope(_model == null || _anim == null))
            {
                if (GUILayout.Button("Настроить импорт анимации", GUILayout.Height(26))) SetupImport();
            }
            using (new EditorGUI.DisabledScope(_anim == null || _cat == null))
            {
                if (GUILayout.Button("Повесить скрипт на кота и назначить клип", GUILayout.Height(26))) Assign();
            }
        }

        private void SetupImport()
        {
            string mp = AssetDatabase.GetAssetPath(_model), ap = AssetDatabase.GetAssetPath(_anim);
            ModelImporter mi = AssetImporter.GetAtPath(mp) as ModelImporter;
            ModelImporter ai = AssetImporter.GetAtPath(ap) as ModelImporter;
            if (mi == null || ai == null) { Debug.LogError("[Кот] выбранные файлы — не FBX-модели."); return; }

            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.SaveAndReimport();
            Avatar av = null;
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(mp);
            for (int i = 0; i < all.Length; i++) if (all[i] is Avatar) { av = (Avatar)all[i]; break; }

            ai.importAnimation = true;
            ai.useFileScale = false; // в файле анимации единица 1 (в модели 100): без этого кривые смещений ужимаются в 100 раз
            ai.animationType = ModelImporterAnimationType.Generic;
            if (av != null) { ai.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; ai.sourceAvatar = av; }
            else Debug.LogWarning("[Кот] у модели не нашёлся Avatar — у анимации будет свой аватар (обычно тоже работает, имена костей совпадают).");
            ModelImporterClipAnimation[] clips = ai.defaultClipAnimations;
            if (clips.Length > 0)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i].name = i == 0 ? "CatWalk" : clips[i].name;
                    clips[i].loopTime = true;
                    clips[i].loopPose = true;
                    clips[i].lockRootRotation = true;
                    clips[i].lockRootHeightY = true;
                    clips[i].lockRootPositionXZ = true;
                }
                ai.clipAnimations = clips;
            }
            ai.SaveAndReimport();
            Debug.Log("[Кот] импорт настроен: Generic, аватар кота, клип «CatWalk» зациклен, корень не двигается (кота двигает скрипт).");
        }

        /// <summary>Копия клипа без кривых корня «gamerig»: в файле анимации у него масштаб 100, из-за этого кот вырастал в 100 раз.</summary>
        private static AnimationClip MakeCleanClip(AnimationClip src, string animFbxPath)
        {
            string dir = System.IO.Path.GetDirectoryName(animFbxPath).Replace('\\', '/');
            string path = dir + "/CatWalk_Clean.anim";
            AnimationClip dst = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (dst == null) { dst = new AnimationClip(); AssetDatabase.CreateAsset(dst, path); }
            dst.ClearCurves();
            dst.frameRate = src.frameRate;
            EditorCurveBinding[] bs = AnimationUtility.GetCurveBindings(src);
            int kept = 0, dropped = 0;
            for (int i = 0; i < bs.Length; i++)
            {
                if (bs[i].path == "gamerig") { dropped++; continue; }
                AnimationCurve c = AnimationUtility.GetEditorCurve(src, bs[i]);
                if (c == null) continue;
                AnimationUtility.SetEditorCurve(dst, bs[i], c);
                kept++;
            }
            AnimationClipSettings st = AnimationUtility.GetAnimationClipSettings(dst);
            st.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(dst, st);
            EditorUtility.SetDirty(dst);
            AssetDatabase.SaveAssets();
            Debug.Log("[Кот] чистый клип «CatWalk_Clean» (" + path + "): кривых оставлено " + kept + ", выброшено у корня gamerig " + dropped + " (там был масштаб ×100).");
            return dst;
        }

        private void Assign()
        {
            string ap = AssetDatabase.GetAssetPath(_anim);
            AnimationClip clip = null;
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(ap);
            for (int i = 0; i < all.Length; i++)
            {
                AnimationClip c = all[i] as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview__")) { clip = c; break; }
            }
            if (clip == null) { Debug.LogError("[Кот] в файле анимации не найден клип."); return; }
            clip = MakeCleanClip(clip, ap);
            CatCircleWalker w = _cat.GetComponent<CatCircleWalker>();
            if (w == null) w = Undo.AddComponent<CatCircleWalker>(_cat);
            Undo.RecordObject(w, "Кот: ходьба");
            w.WalkClip = clip;
            w.Radius = Mathf.Max(0.2f, _radius);
            w.Speed = Mathf.Max(0f, _speed);
            if (w.Anim == null) w.Anim = _cat.GetComponentInChildren<Animator>(true);
            if (w.Anim == null)
            {
                Animator an = Undo.AddComponent<Animator>(_cat);
                w.Anim = an;
                Debug.Log("[Кот] на коте не было Animator — добавил.");
            }
            EditorUtility.SetDirty(w);
            Debug.Log("[Кот] CatCircleWalker на «" + _cat.name + "», клип «" + clip.name + "» (" + clip.length.ToString("0.00") + " с). Нажми Play. Ноги скользят — подкрути «Clip Natural Speed».", w);
        }
    }
}
