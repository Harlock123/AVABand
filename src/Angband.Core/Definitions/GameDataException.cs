namespace Angband.Core.Definitions;

/// <summary>Thrown when game data files are missing, malformed or inconsistent.</summary>
public sealed class GameDataException(string message, Exception? inner = null) : Exception(message, inner);
