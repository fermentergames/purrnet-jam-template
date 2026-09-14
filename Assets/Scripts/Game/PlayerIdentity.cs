using System;
using UnityEngine;

namespace Jam
{
    /// <summary>
    /// A player's visual identity: a color index + icon index into the GameTheme
    /// palette/icon lists. Stored as indices so it serializes cleanly over the
    /// network (SyncList<int>). Resolve to a Color/Sprite via the theme.
    /// </summary>
    [Serializable]
    public struct PlayerIdentity
    {
        public int colorIndex;
        public int iconIndex;

        public PlayerIdentity(int colorIndex, int iconIndex)
        {
            this.colorIndex = colorIndex;
            this.iconIndex = iconIndex;
        }

        public Color GetColor(GameTheme theme)
        {
            return theme != null ? theme.GetPlayerColor(colorIndex) : Color.black;
        }

        public Color GetBrightColor(GameTheme theme)
        {
            return theme != null ? theme.GetPlayerBrightColor(colorIndex) : Color.white;
        }

        public Sprite GetIcon(GameTheme theme)
        {
            return theme != null ? theme.GetPlayerIcon(iconIndex) : null;
        }
    }
}