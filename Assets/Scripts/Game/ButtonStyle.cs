using UnityEngine;

namespace Jam
{
    /// <summary>Central styling for buttons (font, drop shadow, pill radius).</summary>
    [CreateAssetMenu(menuName = "Jam/Button Style", fileName = "ButtonStyle")]
    public class ButtonStyle : ScriptableObject
    {
        [Header("Text")]
        public int fontSize = 32;
        public Color fontColor = Color.black;

        [Header("Drop Shadow")]
        public Color shadowColor = new Color(0, 0, 0, 0.4f);
        public Vector2 shadowOffset = new Vector2(0, -6);

        [Header("Pill")]
        [Range(1, 64)]
        public int pillRadius = 64;
    }
}