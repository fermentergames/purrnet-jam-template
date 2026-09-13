using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jam
{
    /// <summary>
    /// A single split prompt: two halves that are funny together but drawable
    /// apart. Player A sees <see cref="subject"/>, Player B sees <see cref="modifier"/>.
    /// The combined prompt is what guessers must name.
    /// </summary>
    [Serializable]
    public struct SplitPrompt
    {
        [TextArea] public string subject;
        [TextArea] public string modifier;

        public SplitPrompt(string subject, string modifier)
        {
            this.subject = subject;
            this.modifier = modifier;
        }

        public string Combined => $"{subject} {modifier}".Trim();
    }

    /// <summary>
    /// A deck of split prompts for Split Canvas. Theme-agnostic: swap this out at
    /// kickoff with theme-specific prompts. If no deck is assigned, the game falls
    /// back to a built-in list so you can test immediately.
    ///
    /// Create one: right-click in Project > Create > Jam > Split Prompt Deck.
    /// </summary>
    [CreateAssetMenu(menuName = "Jam/Split Prompt Deck", fileName = "SplitPromptDeck")]
    public class SplitPromptDeck : ScriptableObject
    {
        public List<SplitPrompt> prompts = new List<SplitPrompt>();

        /// <summary>Built-in fallback so the game works with no deck assigned.</summary>
        public static readonly SplitPrompt[] Fallback =
        {
            new SplitPrompt("An octopus", "at its first birthday"),
            new SplitPrompt("A penguin", "running for president"),
            new SplitPrompt("The Statue of Liberty", "on a diet"),
            new SplitPrompt("A dragon", "afraid of heights"),
            new SplitPrompt("A vampire", "who loves garlic bread"),
            new SplitPrompt("A mermaid", "stuck in traffic"),
            new SplitPrompt("A ghost", "on a Zoom call"),
            new SplitPrompt("A T-rex", "playing piano"),
            new SplitPrompt("A snowman", "on a tropical vacation"),
            new SplitPrompt("A robot", "at its first day of school"),
            new SplitPrompt("A cat", "riding a Roomba"),
            new SplitPrompt("A wizard", "whose wand is a toaster"),
            new SplitPrompt("A firefighter", "who is afraid of fire"),
            new SplitPrompt("A knight", "who is secretly a chicken"),
            new SplitPrompt("A shark", "wearing a tiny tuxedo"),
            new SplitPrompt("A cactus", "in a romantic comedy"),
        };

        public SplitPrompt GetRandom()
        {
            if (prompts != null && prompts.Count > 0)
                return prompts[UnityEngine.Random.Range(0, prompts.Count)];
            return Fallback[UnityEngine.Random.Range(0, Fallback.Length)];
        }
    }
}