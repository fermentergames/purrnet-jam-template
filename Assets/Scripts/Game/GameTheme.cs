using System.Collections.Generic;
using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Central style system for the game UI. Every UI element reads its colors,
    /// sprites and fonts from here — UI construction code must NOT hardcode colors
    /// or sprites, so the whole game can be re-skinned by swapping this asset.
    ///
    /// Create one: right-click in Project > Create > Jam > Game Theme.
    /// </summary>
    [CreateAssetMenu(menuName = "Jam/Game Theme", fileName = "GameTheme")]
    public class GameTheme : ScriptableObject
    {
        [Header("Colors")]
        public Color background = new Color(0.08f, 0.08f, 0.1f, 1f);
        public Color panel = new Color(0.16f, 0.16f, 0.22f, 0.95f);
        public Color panelAlt = new Color(0.22f, 0.22f, 0.3f, 0.95f);
        public Color text = Color.white;
        public Color textMuted = new Color(0.7f, 0.7f, 0.75f, 1f);
        public Color accent = new Color(0.9f, 0.7f, 0.1f, 1f);
        public Color positive = new Color(0.25f, 0.75f, 0.3f, 1f);   // "correct!" green
        public Color negative = new Color(0.8f, 0.25f, 0.25f, 1f);
        public Color canvasBorder = new Color(0.9f, 0.7f, 0.1f, 1f);

        [Header("Sprites (optional — falls back to flat color)")]
        public Sprite panelSprite;
        public Sprite borderSprite;
        public Sprite shadowSprite;
        public Sprite brushCursorSprite;
        public Sprite pawSprite;
        public Sprite armSprite;

        [Header("Fonts (optional — falls back to built-in)")]
        public Font boldFont;   // important text (headers, prompts, scores)
        public Font lightFont;  // secondary text (labels, status, placeholders) 

        [Header("Movement Mode Backgrounds (indexed by MovementMode)")]
        public Sprite[] modeBackgrounds;

        [Header("Movement Mode Props (optional — props hide if unset)")]
        public Sprite wheelSprite;
        public Sprite boatSprite;
        public Sprite waterSprite;
        public Sprite spindleSprite;
        public Sprite platterSprite;
        public Sprite springSprite;
        public Sprite trampolineSprite;

        [Header("Player Palette (dark swatches)")]
        public Color[] playerColors =
        {
            new Color(0.78f, 0.18f, 0.18f), // red
            new Color(0.18f, 0.38f, 0.78f), // blue
            new Color(0.16f, 0.55f, 0.26f), // green
            new Color(0.68f, 0.48f, 0.06f), // amber
            new Color(0.48f, 0.18f, 0.62f), // purple
            new Color(0.10f, 0.55f, 0.60f), // teal
            new Color(0.72f, 0.36f, 0.10f), // orange
            new Color(0.58f, 0.16f, 0.42f), // magenta
        };

        [Header("Player Icons (optional — art TBD)")]
        public Sprite[] playerIcons;

        /// <summary>Dark brush/identity color for a player index (wraps around the palette).</summary>
        public Color GetPlayerColor(int index)
        {
            if (playerColors == null || playerColors.Length == 0)
                return Color.black;
            if (index < 0)
                return Color.black;
            return playerColors[index % playerColors.Length];
        }

        /// <summary>Brighter version for text tinting on dark backgrounds.</summary>
        public Color GetPlayerBrightColor(int index)
        {
            return Color.Lerp(GetPlayerColor(index), Color.white, 0.45f);
        }

        /// <summary>Icon sprite for a player index, or null if none configured.</summary>
        public Sprite GetPlayerIcon(int index)
        {
            if (playerIcons == null || playerIcons.Length == 0 || index < 0)
                return null;
            return playerIcons[index % playerIcons.Length];
        }

        /// <summary>Background sprite for a movement mode, or null if none configured.</summary>
        public Sprite GetModeBackground(MovementMode mode)
        {
            if (modeBackgrounds == null || modeBackgrounds.Length == 0)
                return null;
            var i = (int)mode;
            if (i < 0 || i >= modeBackgrounds.Length)
                return null;
            return modeBackgrounds[i];
        }

        public int PlayerColorCount => playerColors != null ? playerColors.Length : 0;
        public int PlayerIconCount => playerIcons != null ? playerIcons.Length : 0;
    }
}