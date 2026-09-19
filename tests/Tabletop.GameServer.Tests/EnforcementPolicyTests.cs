using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class EnforcementPolicyTests
{
    [Theory]
    [InlineData(Enforcement.Silent, Legality.OutsideLegalSet, true, false)]
    [InlineData(Enforcement.Warn, Legality.OutsideLegalSet, true, true)]
    [InlineData(Enforcement.Strict, Legality.OutsideLegalSet, false, false)]
    [InlineData(Enforcement.Strict, Legality.Illegal, false, false)]
    [InlineData(Enforcement.Warn, Legality.Illegal, true, true)]
    [InlineData(Enforcement.Silent, Legality.Illegal, true, false)]
    [InlineData(Enforcement.Strict, Legality.Legal, true, false)]
    [InlineData(Enforcement.Strict, Legality.AlwaysAccepted, true, false)]
    [InlineData(Enforcement.Warn, Legality.AlwaysAccepted, true, false)]
    public void Three_positions(Enforcement enforcement, Legality legality, bool accepted, bool warned)
    {
        var verdict = EnforcementPolicy.Decide(enforcement, legality, "unit already activated");
        Assert.Equal(accepted, verdict is Accept);
        Assert.Equal(warned, verdict is Accept { Warning: not null });
        if (verdict is Refuse r) Assert.Equal(legality == Legality.Illegal ? RefusalReason.Illegal : RefusalReason.OutsideLegalSet, r.Reason);
    }
}
