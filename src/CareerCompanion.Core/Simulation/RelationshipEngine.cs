using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Simulation;

public sealed class RelationshipEngine
{
    public Relationship Apply(Relationship current, int scoreDelta, int trustDelta, int respectDelta,
        int friendlinessDelta = 0, int rivalryDelta = 0, int tensionDelta = 0, int familiarityDelta = 1)
    {
        static int SafeDelta(int value) => Math.Clamp(value, -5, 5);
        static int Bound(int value) => Math.Clamp(value, -100, 100);
        return current with
        {
            Score = Bound(current.Score + SafeDelta(scoreDelta)),
            Trust = Bound(current.Trust + SafeDelta(trustDelta)),
            Respect = Bound(current.Respect + SafeDelta(respectDelta)),
            Friendliness = Bound(current.Friendliness + SafeDelta(friendlinessDelta)),
            Rivalry = Bound(current.Rivalry + SafeDelta(rivalryDelta)),
            Tension = Bound(current.Tension + SafeDelta(tensionDelta)),
            Familiarity = Bound(current.Familiarity + Math.Clamp(familiarityDelta, 0, 3))
        };
    }
}
