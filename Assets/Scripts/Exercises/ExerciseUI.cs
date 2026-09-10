using UnityEngine;
using UnityEngine.UI;

namespace Jam.Exercises
{
    /// <summary>
    /// Tiny runtime UI helper for the exercises: builds a Canvas + a big Text
    /// label at runtime so you can SEE the networked state without hand-assembling
    /// UI in the editor. Uses legacy uGUI Text (always available, no TMP dependency).
    /// </summary>
    public static class ExerciseUI
    {
        public static Text CreateOverlay(string objectName, int fontSize = 48)
        {
            var canvasGo = new GameObject($"{objectName} Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var textGo = new GameObject($"{objectName} Label");
            textGo.transform.SetParent(canvasGo.transform, false);

            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return text;
        }
    }
}