namespace Game.Contracts;

public enum Confidence { Unknown, Low, Medium, High }

public enum EstimateSource { Initial, PublicEvent, TradeIntelligence, Espionage, Statement }

public record Estimate(
    string Property,
    double Low,
    double High,
    Confidence Confidence,
    int ObservedTurn,
    EstimateSource Source);
