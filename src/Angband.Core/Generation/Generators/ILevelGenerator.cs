namespace Angband.Core.Generation.Generators;

/// <summary>One level-building algorithm. Returns false to request a retry with fresh randomness.</summary>
internal interface ILevelGenerator
{
    string Id { get; }
    bool Generate(GenContext ctx);
}
