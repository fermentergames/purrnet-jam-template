using UnityEngine;

namespace Jam
{
    /// <summary>
    /// A reusable movement config for Moving Canvas. Because this is a ScriptableObject
    /// ASSET (not a scene component field), edits made during PLAY MODE persist — so you
    /// can live-tune the movement curves and keep the changes.
    ///
    /// Create one: right-click in Project > Create > Jam > Movement Config, then assign it
    /// to the MovingCanvasGame component's "Movement Config" field.
    /// </summary>
    [CreateAssetMenu(menuName = "Jam/Movement Config", fileName = "MovementConfig")]
    public class MovementConfig : ScriptableObject
    {
        [Header("Sway")]
        public MovementParams sway = new MovementParams { baseAmplitude = 120f, baseFrequency = 1.5f };

        [Header("Wobble")]
        public MovementParams wobble = new MovementParams { baseAmplitude = 18f, baseFrequency = 1.2f };

        [Header("Shake")]
        public MovementParams shake = new MovementParams { baseAmplitude = 20f, baseFrequency = 40f };

        [Header("Spin (full rotation)")]
        [Tooltip("baseAmplitude = total degrees to rotate clockwise. amplitudeCurve controls rotation speed.")]
        public MovementParams spin = new MovementParams { baseAmplitude = 1080f, baseFrequency = 1f };

        [Header("ZoomOut")]
        [Tooltip("Zoom back and forth. baseAmplitude = how much to shrink (1 - minScale). baseFrequency = oscillation speed. amplitudeCurve scales the zoom amount over the round.")]
        public MovementParams zoom = new MovementParams { baseAmplitude = 0.65f, baseFrequency = 1f };

        /// <summary>
        /// Convert every curve in this config to WEIGHTED tangents so the handles are
        /// draggable in the Inspector. Right-click this asset and choose
        /// "Set Weighted Tangents".
        /// </summary>
        [ContextMenu("Set Weighted Tangents")]
        public void SetWeightedTangents()
        {
            foreach (var p in new[] { sway, wobble, shake, spin, zoom })
            {
                SetWeighted(p.amplitudeCurve);
                SetWeighted(p.frequencyCurve);
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private static void SetWeighted(AnimationCurve curve)
        {
            if (curve == null)
                return;
            for (var i = 0; i < curve.keys.Length; i++)
            {
                var k = curve.keys[i];
                k.weightedMode = WeightedMode.Both;
                k.inWeight = 1f / 3f;
                k.outWeight = 1f / 3f;
                curve.MoveKey(i, k);
            }
        }
    }
}