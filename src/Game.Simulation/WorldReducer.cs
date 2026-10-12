using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation;

public static class WorldReducer
{
    public static void Apply(WorldState world, DomainEvent @event)
    {
        switch (@event)
        {
            case TurnStarted e:
                world.Turn = e.NewTurn;
                break;

            case GdpChanged e:
                world.Countries[e.Country].Gdp += e.Delta;
                break;

            case InflationChanged e:
                world.Countries[e.Country].Inflation += e.Delta;
                break;

            case TreasuryChanged e:
                world.Countries[e.Country].Treasury = Math.Max(0, world.Countries[e.Country].Treasury + e.Delta);
                break;

            case ApprovalChanged e:
                world.Countries[e.Country].Approval = Clamp(world.Countries[e.Country].Approval + e.Delta, 0, 100);
                break;

            case DependenceChanged e:
                world.Countries[e.Country].ResourceDependence = Clamp(world.Countries[e.Country].ResourceDependence + e.Delta, 0, 1);
                break;

            case StockpileChanged e:
                world.Countries[e.Country].ResourceStockpile = Math.Max(0, world.Countries[e.Country].ResourceStockpile + e.Delta);
                break;

            case ReadinessChanged e:
                world.Countries[e.Country].Readiness = Clamp(world.Countries[e.Country].Readiness + e.Delta, 0, 100);
                break;

            case ForcesInStraitChanged e:
                world.Countries[e.Country].ForcesInStrait = Math.Max(0, world.Countries[e.Country].ForcesInStrait + e.Delta);
                break;

            case RareEarthPriceChanged e:
                world.RareEarthPrice = Math.Max(0.1, world.RareEarthPrice + e.Delta);
                break;

            case StraitTensionChanged e:
                world.StraitTension = Clamp(world.StraitTension + e.Delta, 0, 100);
                break;

            case ElectionClockTicked e:
                world.Countries[e.Country].ElectionCountdown--;
                break;

            case TrustChanged e:
                {
                    var rel = world.Rel(e.Source, e.Target);
                    rel.Trust = Clamp(rel.Trust + e.Delta, -100, 100);
                    break;
                }

            case ThreatChanged e:
                {
                    var rel = world.Rel(e.Source, e.Target);
                    rel.Threat = Clamp(rel.Threat + e.Delta, 0, 100);
                    break;
                }

            case ExportRestrictionSet e:
                {
                    var link = world.Link(e.Exporter, e.Importer);
                    if (link != null)
                    {
                        link.ExportRestriction = Clamp(e.Level, 0, 1);
                    }
                    break;
                }

            case SanctionLevelSet e:
                {
                    var clampedLevel = Clamp(e.Level, 0, 1);
                    if (clampedLevel == 0)
                    {
                        // Remove if level is 0 (after clamping)
                        world.Sanctions.RemoveAll(s => s.Source == e.Source && s.Target == e.Target);
                    }
                    else
                    {
                        // Find existing sanction
                        var existing = world.Sanctions.FirstOrDefault(s => s.Source == e.Source && s.Target == e.Target);
                        if (existing != null)
                        {
                            existing.Level = clampedLevel;
                        }
                        else
                        {
                            // Create new sanction
                            world.Sanctions.Add(new SanctionState
                            {
                                Source = e.Source,
                                Target = e.Target,
                                Level = clampedLevel
                            });
                        }
                    }
                    break;
                }

            case TradeFlowChanged e:
                {
                    var link = world.Link(e.Exporter, e.Importer);
                    if (link != null)
                    {
                        link.BaseFlow = Math.Max(0, link.BaseFlow + e.Delta);
                    }
                    break;
                }

            case PactSigned e:
                {
                    var key = WorldState.PactKey(e.A, e.B);
                    world.Pacts.Add(key);
                    break;
                }

            case PactBroken e:
                {
                    var key = WorldState.PactKey(e.A, e.B);
                    world.Pacts.Remove(key);
                    break;
                }

            case GovernmentChanged e:
                world.Countries[e.Country].GovernmentTemplateId = e.NewTemplateId;
                world.Countries[e.Country].ElectionCountdown = 48;
                break;

            case ElectionHeld e:
                if (e.IncumbentRetained)
                {
                    world.Countries[e.Country].ElectionCountdown = 48;
                }
                break;

            case ShortageStarted e:
                world.Countries[e.Country].InShortage = true;
                break;

            case ShortageEnded e:
                world.Countries[e.Country].InShortage = false;
                break;

            // Marker events: no-ops (these events carry information only, do not mutate state)
            case SubsidyEnacted:
            case ThreatIssued:
            case ReassuranceGiven:
            case ForcesDeployedNotice:
            case StraitIncident:
            case SpySucceeded:
            case SpyCaught:
            case IntentRejected:
                // No state change for marker events
                break;

            default:
                // Unknown event type, no-op
                break;
        }
    }

    public static void ApplyAll(WorldState world, IEnumerable<DomainEvent> events)
    {
        foreach (var @event in events)
        {
            Apply(world, @event);
        }
    }

    public static WorldState Replay(IEnumerable<DomainEvent> stream)
    {
        var world = Scenario.CreateMeridianStrait();
        ApplyAll(world, stream);
        return world;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
