using System.Collections.Generic;
using UnityEngine;

namespace Jam
{
    /// <summary>
    /// A deck of simple single-word prompts for Moving Canvas. One shared bucket
    /// (no per-movement-mode curation for now). Guessing matches if a guess CONTAINS
    /// the prompt word (case-insensitive).
    ///
    /// Create one: right-click in Project > Create > Jam > Moving Prompt Deck.
    /// </summary>
    [CreateAssetMenu(menuName = "Jam/Moving Prompt Deck", fileName = "MovingPromptDeck")]
    public class MovingPromptDeck : ScriptableObject
    {
        [TextArea]
        public List<string> prompts = new List<string>();

        /// <summary>Built-in fallback so the game works with no deck assigned.</summary>
        public static readonly string[] Fallback =
        {
            "boat", "burger", "cat", "skateboarder", "pizza", "rocket", "umbrella",
            "guitar", "dinosaur", "lighthouse", "snowman", "cactus", "robot",
            "castle", "pineapple", "bicycle", "volcano", "octopus", "cowboy",
            "submarine", "camera", "tornado", "penguin", "teapot", "dragon",
            "ladder", "crown", "anchor", "kangaroo", "lighthouse", "mermaid",
            "spaceship", "telescope", "windmill", "zebra", "hotdog", "igloo",
            "jellyfish", "kite", "lighthouse", "mushroom", "nest", "owl",
            "pancake", "quilt", "rainbow", "saddle", "tractor", "unicorn",
            "vase", "wagon", "xylophone"
        };

        public string GetRandom()
        {
            if (prompts != null && prompts.Count > 0)
                return prompts[Random.Range(0, prompts.Count)];
            return Fallback[Random.Range(0, Fallback.Length)];
        }
    }
}