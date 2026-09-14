using System;
using System.Collections;
using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Lightweight coroutine tween helpers for legacy uGUI (no DOTween dependency).
    /// All methods start a coroutine on the provided MonoBehaviour runner and support
    /// an optional easing curve (defaults to linear).
    /// </summary>
    public static class UITween
    {
        public static Coroutine MoveTo(MonoBehaviour runner, RectTransform rt, Vector2 target, float duration,
            AnimationCurve curve = null, Action onComplete = null)
            => runner.StartCoroutine(MoveToRoutine(rt, target, duration, curve, onComplete));

        public static Coroutine ScaleTo(MonoBehaviour runner, RectTransform rt, Vector3 target, float duration,
            AnimationCurve curve = null, Action onComplete = null)
            => runner.StartCoroutine(ScaleToRoutine(rt, target, duration, curve, onComplete));

        public static Coroutine FadeTo(MonoBehaviour runner, CanvasGroup group, float target, float duration,
            AnimationCurve curve = null, Action onComplete = null)
            => runner.StartCoroutine(FadeToRoutine(group, target, duration, curve, onComplete));

        private static IEnumerator MoveToRoutine(RectTransform rt, Vector2 target, float duration, AnimationCurve curve, Action onComplete)
        {
            if (rt == null)
                yield break;
            var start = rt.anchoredPosition;
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (rt == null) // destroyed mid-tween (e.g. a rebuilt row)
                    yield break;
                var p = Mathf.Clamp01(t / duration);
                var e = curve != null ? curve.Evaluate(p) : p;
                rt.anchoredPosition = Vector2.Lerp(start, target, e);
                yield return null;
            }
            if (rt == null)
                yield break;
            rt.anchoredPosition = target;
            onComplete?.Invoke();
        }

        private static IEnumerator ScaleToRoutine(RectTransform rt, Vector3 target, float duration, AnimationCurve curve, Action onComplete)
        {
            if (rt == null)
                yield break;
            var start = rt.localScale;
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (rt == null) // destroyed mid-tween (e.g. a rebuilt row)
                    yield break;
                var p = Mathf.Clamp01(t / duration);
                var e = curve != null ? curve.Evaluate(p) : p;
                rt.localScale = Vector3.Lerp(start, target, e);
                yield return null;
            }
            if (rt == null)
                yield break;
            rt.localScale = target;
            onComplete?.Invoke();
        }

        private static IEnumerator FadeToRoutine(CanvasGroup group, float target, float duration, AnimationCurve curve, Action onComplete)
        {
            if (group == null)
                yield break;
            var start = group.alpha;
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (group == null) // destroyed mid-tween (e.g. a rebuilt row)
                    yield break;
                var p = Mathf.Clamp01(t / duration);
                var e = curve != null ? curve.Evaluate(p) : p;
                group.alpha = Mathf.Lerp(start, target, e);
                yield return null;
            }
            if (group == null)
                yield break;
            group.alpha = target;
            onComplete?.Invoke();
        }
    }
}