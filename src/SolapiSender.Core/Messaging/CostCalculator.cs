namespace SolapiSender.Core.Messaging;

/// <summary>
/// Đơn giá (원/건). SOLAPI không có API báo giá theo tài khoản nên giá trị này
/// do người dùng chỉnh trong Cài đặt cho khớp hợp đồng của họ.
/// </summary>
public sealed record PriceTable(decimal Sms, decimal Lms, decimal Mms)
{
    public static PriceTable Default { get; } = new(18m, 45m, 110m);

    public decimal PriceOf(MessageType type) => type switch
    {
        MessageType.SMS => Sms,
        MessageType.LMS => Lms,
        MessageType.MMS => Mms,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

public sealed record CostEstimate(int SmsCount, int LmsCount, int MmsCount, decimal Total)
{
    public int TotalCount => SmsCount + LmsCount + MmsCount;
}

public static class CostCalculator
{
    public static CostEstimate Estimate(IEnumerable<MessageType> types, PriceTable prices)
    {
        int sms = 0, lms = 0, mms = 0;
        foreach (var t in types)
        {
            switch (t)
            {
                case MessageType.SMS: sms++; break;
                case MessageType.LMS: lms++; break;
                case MessageType.MMS: mms++; break;
            }
        }
        var total = sms * prices.Sms + lms * prices.Lms + mms * prices.Mms;
        return new CostEstimate(sms, lms, mms, total);
    }
}
