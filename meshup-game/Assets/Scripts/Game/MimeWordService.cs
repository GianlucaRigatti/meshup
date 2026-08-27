using System;
using System.Collections.Generic;
using UnityEngine;

namespace Meshup.Game
{
    /// <summary>
    /// Loads and supplies words used for mime prompts.
    /// </summary>
    public sealed class MimeWordService
    {
        private const string DefaultResourcePath = "Game/mime_verbs";

        private readonly IReadOnlyList<string> verbs;
        private readonly IReadOnlyList<string> adjectives;

        public int Count => VerbCount;

        public int VerbCount => verbs.Count;

        public int AdjectiveCount => adjectives.Count;

        public IReadOnlyList<string> Verbs => verbs;

        public IReadOnlyList<string> Adjectives => adjectives;

        public MimeWordService(TextAsset wordFile)
        {
            if (wordFile == null)
            {
                throw new ArgumentNullException(nameof(wordFile));
            }

            WordCollection collection = JsonUtility.FromJson<WordCollection>(wordFile.text);
            if (collection?.verbs == null || collection.verbs.Length == 0)
            {
                throw new InvalidOperationException(
                    $"The word file '{wordFile.name}' does not contain any verbs.");
            }

            if (collection.adjectives == null || collection.adjectives.Length == 0)
            {
                throw new InvalidOperationException(
                    $"The word file '{wordFile.name}' does not contain any adjectives.");
            }

            verbs = Array.AsReadOnly(collection.verbs);
            adjectives = Array.AsReadOnly(collection.adjectives);
        }

        public static MimeWordService LoadDefault()
        {
            TextAsset wordFile = Resources.Load<TextAsset>(DefaultResourcePath);
            if (wordFile == null)
            {
                throw new InvalidOperationException(
                    $"Could not load the mime word file from Resources/{DefaultResourcePath}.json.");
            }

            return new MimeWordService(wordFile);
        }

        public string GetRandomVerb()
        {
            return verbs[UnityEngine.Random.Range(0, verbs.Count)];
        }

        public string GetRandomAdjective()
        {
            return adjectives[UnityEngine.Random.Range(0, adjectives.Count)];
        }

        [Serializable]
        private sealed class WordCollection
        {
            public string[] verbs;
            public string[] adjectives;
        }
    }
}
