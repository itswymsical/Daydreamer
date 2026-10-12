using Daydreamer.Core.Models;

namespace Daydreamer.Core.Parsing;

/// <summary>Fast name → character lookup (case-insensitive, includes aliases).</summary>
public sealed class CharacterRegistry
{
    private readonly Dictionary<string, Character> _byName = new(StringComparer.OrdinalIgnoreCase);

    // Every name in capitals, longest first, so "DR. TANAKA" is tried before "DR".
    private readonly List<(string Name, Character Character)> _capsNames;

    public static readonly CharacterRegistry Empty = new([]);

    public CharacterRegistry(IEnumerable<Character> characters)
    {
        foreach (var character in characters)
        {
            foreach (var name in character.AllNames())
                _byName.TryAdd(name, character); // if two characters share a name, the first one wins
        }
        _capsNames = _byName
            .Select(entry => (Name: entry.Key.ToUpperInvariant(), Character: entry.Value))
            .Where(entry => entry.Name.Any(char.IsLetterOrDigit))
            .OrderByDescending(entry => entry.Name.Length)
            .ToList();
    }

    public Character? Find(string name) => _byName.GetValueOrDefault(name.Trim());

    /// <summary>
    /// A character whose name is written in capitals at <paramref name="start"/>, followed by a space or "(" —
    /// the way many scripts write a speaker without a colon ("KAITO It's late."). Also returns the name's length.
    /// </summary>
    public (Character Character, int Length)? FindCapsName(string text, int start)
    {
        foreach (var (name, character) in _capsNames)
        {
            if (start + name.Length >= text.Length) continue; // there must be something after the name
            if (string.CompareOrdinal(text, start, name, 0, name.Length) != 0) continue;
            char next = text[start + name.Length];
            if (char.IsWhiteSpace(next) || next == '(') return (character, name.Length);
        }
        return null;
    }
}
