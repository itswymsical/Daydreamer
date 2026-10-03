using Daydreamer.Core.Models;

namespace Daydreamer.Core.Parsing;

/// <summary>Fast name → character lookup (case-insensitive, includes aliases).</summary>
public sealed class CharacterRegistry
{
    private readonly Dictionary<string, Character> _byName = new(StringComparer.OrdinalIgnoreCase);

    public static readonly CharacterRegistry Empty = new([]);

    public CharacterRegistry(IEnumerable<Character> characters)
    {
        foreach (var character in characters)
        {
            foreach (var name in character.AllNames())
                _byName.TryAdd(name, character); // if two characters share a name, the first one wins
        }
    }

    public Character? Find(string name) => _byName.GetValueOrDefault(name.Trim());
}
