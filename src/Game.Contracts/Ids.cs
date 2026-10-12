namespace Game.Contracts;

public enum CountryId { Alderia, Borvia, Cascara }

public enum ActionType
{
    Pass,
    Trade, Sanction, ExportControl, Subsidize,
    Reassure, Threaten, SignPact, BreakPact,
    Mobilize, DeployToRegion, Withdraw,
    Spy
}

public enum ActionCategory { None, Economic, Diplomatic, Military, Covert }

public static class ActionTypeExtensions
{
    public static ActionCategory Category(this ActionType t) => t switch
    {
        ActionType.Trade or ActionType.Sanction or ActionType.ExportControl or ActionType.Subsidize
            => ActionCategory.Economic,
        ActionType.Reassure or ActionType.Threaten or ActionType.SignPact or ActionType.BreakPact
            => ActionCategory.Diplomatic,
        ActionType.Mobilize or ActionType.DeployToRegion or ActionType.Withdraw
            => ActionCategory.Military,
        ActionType.Spy => ActionCategory.Covert,
        _ => ActionCategory.None,
    };
}
