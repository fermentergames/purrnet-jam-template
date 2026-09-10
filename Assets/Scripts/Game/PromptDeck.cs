using System.Collections.Generic;
using UnityEngine;

namespace Jam
{
    /// <summary>
    /// A deck of prompts for the party game. Theme-agnostic: swap this out at
    /// kickoff with theme-specific prompts. If no deck is assigned to GameFlow,
    /// it falls back to a built-in list so you can test immediately.
    ///
    /// Create one: right-click in Project > Create > Jam > Prompt Deck.
    /// </summary>
    [CreateAssetMenu(menuName = "Jam/Prompt Deck", fileName = "PromptDeck")]
    public class PromptDeck : ScriptableObject
    {
        [TextArea]
        public List<string> prompts = new List<string>();
    }
}