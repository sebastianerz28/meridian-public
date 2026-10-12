namespace Game.Contracts;

public abstract record DomainEvent(
    int Turn,
    CountryId? Actor,
    Visibility Visibility);
