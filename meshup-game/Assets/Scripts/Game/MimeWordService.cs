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

        public string[] GetDistinctRandomVerbs(int count, System.Random random)
        {
            if (count < 0 || count > verbs.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var indices = new int[verbs.Count];
            for (var index = 0; index < indices.Length; index++)
            {
                indices[index] = index;
            }
            for (var index = 0; index < count; index++)
            {
                var other = random.Next(index, indices.Length);
                (indices[index], indices[other]) = (indices[other], indices[index]);
            }

            var result = new string[count];
            for (var index = 0; index < count; index++)
            {
                result[index] = verbs[indices[index]];
            }
            return result;
        }

        [Serializable]
        private sealed class WordCollection
        {
            public string[] verbs;
            public string[] adjectives;
        }
    }
}
