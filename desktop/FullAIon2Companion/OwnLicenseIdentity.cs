namespace FullAIon2Companion;

/// <summary>Only an own character announced by this capture session can select a license.</summary>
public static class OwnLicenseIdentity
{
    public static CharacterRef? Resolve((int Id, string Name, int ServerId)? captured, PlayerCard? card, string region)
    {
        if (captured is not { Id: > 0, ServerId: > 0 } own || card is null || card.ServerId != own.ServerId || card.Region != region
            || !card.Name.Equals(own.Name, StringComparison.OrdinalIgnoreCase)
            || region is not ("la" or "eu" or "nae" or "naw" or "as")
            || card.CharacterId is null || !System.Text.RegularExpressions.Regex.IsMatch(card.CharacterId, "^[A-Za-z0-9_=-]{10,120}$")) return null;
        return new(region, own.ServerId, card.CharacterId);
    }
}
