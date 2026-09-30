namespace Angband.Core.Game;

/// <summary>AVABand: asks the shop you're in for its service (the Armoury's gem removal, the Alchemist's identifying). Takes no time.</summary>
public sealed record StoreServicesCommand : GameCommand;

// AVABand's shop services, offered from inside the shop (the shop screen's Services button, or !)
// rather than asked on the way in, so going into a shop is never interrupted: the Armoury takes gems
// out of bracers (GameSession.Gems.cs), the Alchemist identifies (GameSession.Identify.cs).
public sealed partial class GameSession
{
    /// <summary>What the shop you're in offers you now (for the button), or null when nothing.</summary>
    public string? StoreServiceLabel => StoreHere?.Id switch
    {
        "armoury" when GemHosts().Count > 0 => "Remove a gem",
        "alchemist" when Unidentified().Count > 0 => "Identify something",
        _ => null,
    };

    private int StoreServices()
    {
        switch (StoreServiceLabel is null ? null : StoreHere?.Id)
        {
            case "armoury": OfferGemRemoval(); break;
            case "alchemist": OfferIdentify(); break;
            default: Publish(new MessageEvent("There's nothing more they can do for you here.")); break;
        }
        return 0;
    }
}
