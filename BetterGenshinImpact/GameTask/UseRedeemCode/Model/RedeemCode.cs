namespace BetterGenshinImpact.GameTask.UseRedeemCode.Model;

public class RedeemCode
{
    public string Code { get; set; }

    public string? Items { get; set; }

    public RedeemCode(string code, string? items)
    {
        Code = code;
        Items = items;
    }
}