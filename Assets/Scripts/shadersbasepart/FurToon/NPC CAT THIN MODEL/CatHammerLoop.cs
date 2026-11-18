using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Repeats one assigned attack clip on this cat without an Animator Controller.
/// Attach to the same GameObject as the character's Animator.
/// FBX import: Generic rig, Import Animation enabled. Bake root rotation and
/// root position (Y/XZ) into the pose to keep the authored bend and crouch.
/// No Animator Controller is required. Assign the clip before entering Play Mode.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class CatHammerLoop : MonoBehaviour
{
    [SerializeField, Tooltip("Перетащи сюда клип удара из раскрытого FBX.")]
    private AnimationClip animationClip;

    [SerializeField, Min(0.01f), Tooltip("1 — обычная скорость, 2 — вдвое быстрее.")]
    private float playbackSpeed = 1f;

    [SerializeField, Min(0f), Tooltip("Пауза в секундах после каждого удара.")]
    private float pauseBetweenHits = 0.25f;

    [SerializeField, Min(0f), Tooltip("Задержка перед первым ударом. Можно задать разную двум котам.")]
    private float startDelay = 0f;

    [SerializeField, Tooltip("Сохранять положение и поворот объекта кота в сцене.")]
    private bool stayInPlace = true;

    [SerializeField, Tooltip("Сохранять исходный масштаб модели и костей. Для ударов оставь включённым.")]
    private bool keepOriginalScales = true;

    private Animator targetAnimator;
    private PlayableGraph graph;
    private AnimationClipPlayable clipPlayable;
    private double elapsed;
    private double clipLength;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private bool previousApplyRootMotion;
    private AnimatorCullingMode previousCullingMode;
    private bool animatorSettingsSaved;
    private Transform[] scaleTargets;
    private Vector3[] originalScales;

    private void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        if (animationClip == null || animationClip.length <= 0f || animationClip.legacy)
        {
            Debug.LogError(
                "CatHammerLoop: назначь обычный Animation Clip из FBX (Rig = Generic, не Legacy).",
                this);
            enabled = false;
            return;
        }

        targetAnimator = GetComponent<Animator>();
        if (!targetAnimator.enabled)
        {
            Debug.LogError("CatHammerLoop: включи компонент Animator на коте.", this);
            enabled = false;
            return;
        }

        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        scaleTargets = GetComponentsInChildren<Transform>(true);
        originalScales = new Vector3[scaleTargets.Length];
        for (int i = 0; i < scaleTargets.Length; i++)
            originalScales[i] = scaleTargets[i].localScale;
        previousApplyRootMotion = targetAnimator.applyRootMotion;
        previousCullingMode = targetAnimator.cullingMode;
        animatorSettingsSaved = true;
        targetAnimator.applyRootMotion = false;
        targetAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        clipLength = animationClip.length;
        elapsed = -Math.Max(0d, startDelay);
        graph = PlayableGraph.Create("CatHammerLoop_" + GetInstanceID());
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        clipPlayable = AnimationClipPlayable.Create(graph, animationClip);
        clipPlayable.SetApplyFootIK(false);
        clipPlayable.SetApplyPlayableIK(false);
        clipPlayable.SetSpeed(0d); // The script controls clip time explicitly.
        clipPlayable.SetDuration(double.PositiveInfinity);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(
            graph, "Cat attack", targetAnimator);
        output.SetSourcePlayable(clipPlayable);
        graph.Play();
        SamplePose();
    }

    private void LateUpdate()
    {
        if (!graph.IsValid())
            return;

        elapsed += Time.deltaTime;
        SamplePose();

        if (stayInPlace)
        {
            // Lock only the character object; bones can still bend and crouch.
            transform.localPosition = initialLocalPosition;
            transform.localRotation = initialLocalRotation;
        }
    }

    private void SamplePose()
    {
        double sampleTime = 0d;
        if (elapsed >= 0d)
        {
            double speed = Math.Max(0.01d, playbackSpeed);
            double cycleDuration = clipLength / speed + Math.Max(0d, pauseBetweenHits);
            double timeInCycle = elapsed % cycleDuration;
            sampleTime = Math.Min(timeInCycle * speed, clipLength);
        }

        // During the pause, hold the last pose; then restart the same clip.
        // Each component owns its own graph, so two cats are independent.
        clipPlayable.SetTime(sampleTime);
        graph.Evaluate(0f);
        RestoreScales();
    }

    private void RestoreScales()
    {
        if (!keepOriginalScales || scaleTargets == null)
            return;

        for (int i = 0; i < scaleTargets.Length; i++)
        {
            if (scaleTargets[i] != null)
                scaleTargets[i].localScale = originalScales[i];
        }
    }

    private void OnDisable()
    {
        ReleaseGraph();
    }

    private void OnDestroy()
    {
        ReleaseGraph();
    }

    private void ReleaseGraph()
    {
        if (graph.IsValid())
            graph.Destroy();

        RestoreScales();

        if (animatorSettingsSaved && targetAnimator != null)
        {
            targetAnimator.applyRootMotion = previousApplyRootMotion;
            targetAnimator.cullingMode = previousCullingMode;
        }
        animatorSettingsSaved = false;
    }
}
