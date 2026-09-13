using System;
using System.Collections.Generic;
using System.Linq;

namespace Meshup.Game
{
    public enum GeneratedObjectSize
    {
        Small = 0,
        Medium = 1,
        ExtraLarge = 2
    }

    public static class GeneratedObjectSizes
    {
        public const float SmallHeight = 0.35f;
        public const float MediumHeight = 1.7f;
        public const float ExtraLargeHeight = 5f;

        public static GeneratedObjectSize Normalize(GeneratedObjectSize size)
        {
            return size is GeneratedObjectSize.Small
                or GeneratedObjectSize.Medium
                or GeneratedObjectSize.ExtraLarge
                    ? size : GeneratedObjectSize.Medium;
        }

        public static float TargetHeight(GeneratedObjectSize size)
        {
            return Normalize(size) switch
            {
                GeneratedObjectSize.Small => SmallHeight,
                GeneratedObjectSize.ExtraLarge => ExtraLargeHeight,
                _ => MediumHeight
            };
        }
    }

    public enum MeshupGamePhase
    {
        WaitingForArrival,
        CallingMime,
        ChoosingWord,
        Preparation,
        TimedGuessing,
        Result,
        Finished
    }

    [Serializable]
    public sealed class MeshupPlayerScore
    {
        public string peerId;
        public string displayName;
        public int points;
        public bool connected;
        public bool hasMimed;
    }

    [Serializable]
    public sealed class MeshupGeneratedObjectState
    {
        public string objectId;
        public string url;
        public GeneratedObjectSize size = GeneratedObjectSize.Medium;
        public UnityEngine.Vector3 position;
        public UnityEngine.Quaternion rotation;
        public UnityEngine.Vector3 scale = UnityEngine.Vector3.one;
    }

    [Serializable]
    public sealed class MeshupMatchSnapshot
    {
        public int version;
        public int phase;
        public int roundNumber;
        public string mimePeerId;
        public string maskedWord;
        public string resultWord;
        public string resultMessage;
        public int remainingSeconds;
        public int generationTokens;
        public bool generationPending;
        public MeshupPlayerScore[] scores = Array.Empty<MeshupPlayerScore>();
        public MeshupGeneratedObjectState[] generatedObjects =
            Array.Empty<MeshupGeneratedObjectState>();
    }

    /// <summary>Pure host-side rules. Networking and presentation live elsewhere.</summary>
    public sealed class MeshupMatchState
    {
        private readonly Dictionary<string, MeshupPlayerScore> players =
            new(StringComparer.Ordinal);
        private readonly List<string> mimeOrder = new();
        private readonly List<int> revealOrder = new();
        private readonly System.Random random;
        private string selectedWord = string.Empty;
        private float elapsed;
        private int version;

        public MeshupGamePhase Phase { get; private set; } =
            MeshupGamePhase.WaitingForArrival;
        public string MimePeerId { get; private set; } = string.Empty;
        public string[] WordOptions { get; private set; } = Array.Empty<string>();
        public string MaskedWord { get; private set; } = string.Empty;
        public string ResultWord { get; private set; } = string.Empty;
        public string ResultMessage { get; private set; } = string.Empty;
        public int RoundNumber { get; private set; }
        public int RemainingSeconds { get; private set; }
        public int GenerationTokens { get; private set; }
        public bool GenerationPending { get; private set; }
        public int Version => version;
        public string SelectedWord => selectedWord;
        public IEnumerable<MeshupPlayerScore> Players => players.Values;

        public MeshupMatchState(System.Random random = null)
        {
            this.random = random ?? new System.Random();
        }

        public void Begin(IEnumerable<Multiplayer.ParticipantInfo> participants)
        {
            if (participants == null)
            {
                throw new ArgumentNullException(nameof(participants));
            }

            players.Clear();
            mimeOrder.Clear();
            foreach (var participant in participants
                .Where(item => item != null && !string.IsNullOrEmpty(item.PeerId)))
            {
                players[participant.PeerId] = new MeshupPlayerScore
                {
                    peerId = participant.PeerId,
                    displayName = participant.DisplayName,
                    connected = participant.Connected
                };
                mimeOrder.Add(participant.PeerId);
            }
            Shuffle(mimeOrder);
            RoundNumber = 0;
            StartNextRound();
        }

        public bool MimeEntered(string peerId, string[] options)
        {
            if (Phase != MeshupGamePhase.CallingMime
                || !IsCurrentMime(peerId) || options == null
                || options.Length != 2 || string.IsNullOrWhiteSpace(options[0])
                || string.IsNullOrWhiteSpace(options[1])
                || string.Equals(options[0], options[1],
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            WordOptions = options.ToArray();
            Phase = MeshupGamePhase.ChoosingWord;
            Touch();
            return true;
        }

        public bool SelectWord(string peerId, int optionIndex)
        {
            if (Phase != MeshupGamePhase.ChoosingWord
                || !IsCurrentMime(peerId) || optionIndex < 0
                || optionIndex >= WordOptions.Length)
            {
                return false;
            }

            selectedWord = WordOptions[optionIndex].Trim();
            MaskedWord = new string(selectedWord.Select(character =>
                char.IsLetter(character) ? '_' : character).ToArray());
            revealOrder.Clear();
            revealOrder.AddRange(Enumerable.Range(0, selectedWord.Length)
                .Where(index => char.IsLetter(selectedWord[index])));
            Shuffle(revealOrder);
            GenerationTokens = 3;
            GenerationPending = false;
            RemainingSeconds = 120;
            Phase = MeshupGamePhase.Preparation;
            Touch();
            return true;
        }

        public bool StartTimer(string peerId)
        {
            if (Phase != MeshupGamePhase.Preparation
                || !IsCurrentMime(peerId) || GenerationPending)
            {
                return false;
            }
            elapsed = 0f;
            RemainingSeconds = 120;
            Phase = MeshupGamePhase.TimedGuessing;
            Touch();
            return true;
        }

        public bool Tick(float deltaSeconds)
        {
            if (Phase != MeshupGamePhase.TimedGuessing || deltaSeconds <= 0f)
            {
                return false;
            }

            var beforeSeconds = RemainingSeconds;
            var beforeMask = MaskedWord;
            elapsed += deltaSeconds;
            RemainingSeconds = Math.Max(0, (int)Math.Ceiling(120f - elapsed));
            RevealHints(elapsed);
            if (elapsed >= 120f)
            {
                EndRound("Time's up", selectedWord);
                return true;
            }
            if (beforeSeconds != RemainingSeconds || beforeMask != MaskedWord)
            {
                Touch();
                return true;
            }
            return false;
        }

        public bool SubmitGuess(string peerId, string transcription)
        {
            if (Phase != MeshupGamePhase.TimedGuessing
                || IsCurrentMime(peerId)
                || !players.TryGetValue(peerId, out var guesser)
                || !guesser.connected
                || !string.Equals(NormalizeGuess(transcription),
                    NormalizeGuess(selectedWord), StringComparison.Ordinal))
            {
                return false;
            }

            players[MimePeerId].points++;
            guesser.points++;
            EndRound($"{guesser.displayName} guessed", selectedWord);
            return true;
        }

        public bool MimeExited(string peerId)
        {
            if (Phase != MeshupGamePhase.Result || !IsCurrentMime(peerId))
            {
                return false;
            }
            StartNextRound();
            return true;
        }

        public bool TryBeginGeneration(string peerId)
        {
            if (Phase != MeshupGamePhase.Preparation
                || !IsCurrentMime(peerId) || GenerationTokens <= 0
                || GenerationPending)
            {
                return false;
            }
            GenerationTokens--;
            GenerationPending = true;
            Touch();
            return true;
        }

        public bool EndGeneration()
        {
            if (!GenerationPending)
            {
                return false;
            }
            GenerationPending = false;
            Touch();
            return true;
        }

        public bool Disconnect(string peerId)
        {
            if (!players.TryGetValue(peerId, out var player) || !player.connected)
            {
                return false;
            }
            player.connected = false;
            if (IsCurrentMime(peerId) && Phase != MeshupGamePhase.Finished)
            {
                player.hasMimed = true;
                StartNextRound();
            }
            else
            {
                Touch();
            }
            return true;
        }

        public MeshupMatchSnapshot CreateSnapshot(
            IEnumerable<MeshupGeneratedObjectState> generatedObjects = null)
        {
            return new MeshupMatchSnapshot
            {
                version = version,
                phase = (int)Phase,
                roundNumber = RoundNumber,
                mimePeerId = MimePeerId,
                maskedWord = MaskedWord,
                resultWord = ResultWord,
                resultMessage = ResultMessage,
                remainingSeconds = RemainingSeconds,
                generationTokens = GenerationTokens,
                generationPending = GenerationPending,
                scores = players.Values
                    .OrderByDescending(item => item.points)
                    .ThenBy(item => item.displayName, StringComparer.Ordinal)
                    .ThenBy(item => item.peerId, StringComparer.Ordinal)
                    .ToArray(),
                generatedObjects = generatedObjects?.ToArray()
                    ?? Array.Empty<MeshupGeneratedObjectState>()
            };
        }

        public static string NormalizeGuess(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            var normalized = string.Join(" ", value.Trim().Split(
                (char[])null, StringSplitOptions.RemoveEmptyEntries));
            var start = 0;
            var end = normalized.Length - 1;
            while (start <= end && char.IsPunctuation(normalized[start])) start++;
            while (end >= start && char.IsPunctuation(normalized[end])) end--;
            return start > end
                ? string.Empty
                : normalized.Substring(start, end - start + 1).ToLowerInvariant();
        }

        private void RevealHints(float timerElapsed)
        {
            var target = (int)Math.Ceiling(revealOrder.Count * 0.30f);
            if (target == 0)
            {
                return;
            }
            var count = Math.Min(target,
                (int)Math.Floor(Math.Min(timerElapsed, 90f) / (90f / target)));
            var characters = MaskedWord.ToCharArray();
            for (var index = 0; index < count; index++)
            {
                var position = revealOrder[index];
                characters[position] = selectedWord[position];
            }
            MaskedWord = new string(characters);
        }

        private void EndRound(string message, string word)
        {
            ResultMessage = message;
            ResultWord = word;
            RemainingSeconds = 0;
            GenerationPending = false;
            Phase = MeshupGamePhase.Result;
            Touch();
        }

        private void StartNextRound()
        {
            if (!string.IsNullOrEmpty(MimePeerId)
                && players.TryGetValue(MimePeerId, out var previous))
            {
                previous.hasMimed = true;
            }
            MimePeerId = mimeOrder.FirstOrDefault(peerId =>
                players.TryGetValue(peerId, out var candidate)
                && candidate.connected && !candidate.hasMimed) ?? string.Empty;
            WordOptions = Array.Empty<string>();
            selectedWord = string.Empty;
            MaskedWord = string.Empty;
            ResultWord = string.Empty;
            ResultMessage = string.Empty;
            GenerationTokens = 0;
            GenerationPending = false;
            RemainingSeconds = 0;
            if (string.IsNullOrEmpty(MimePeerId))
            {
                Phase = MeshupGamePhase.Finished;
            }
            else
            {
                RoundNumber++;
                Phase = MeshupGamePhase.CallingMime;
            }
            Touch();
        }

        private bool IsCurrentMime(string peerId) =>
            !string.IsNullOrEmpty(peerId)
            && string.Equals(peerId, MimePeerId, StringComparison.Ordinal);

        private void Shuffle<T>(IList<T> values)
        {
            for (var index = values.Count - 1; index > 0; index--)
            {
                var other = random.Next(index + 1);
                (values[index], values[other]) = (values[other], values[index]);
            }
        }

        private void Touch() => version++;
    }
}
