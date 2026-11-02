using SolapiSender.Core.Messaging;
using SolapiSender.Core.Models;
using SolapiSender.Core.Models.Solapi;

namespace SolapiSender.Core.Tests;

public class ByteCounterTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("hello", 5)]
    [InlineData("안녕하세요", 10)]   // Hangul = 2 byte EUC-KR
    [InlineData("A가\nB", 5)]
    [InlineData("A가\r\nB", 5)]     // CRLF tính như 1 ký tự xuống dòng
    public void Counts_euc_kr_bytes(string text, int expected) => Assert.Equal(expected, ByteCounter.CountBytes(text));

    [Fact]
    public void Emoji_is_reported_as_unsupported()
    {
        var r = ByteCounter.Count("가😀");
        Assert.Equal(4, r.Bytes);
        Assert.Equal(["😀"], r.UnsupportedChars);
    }

    [Fact]
    public void Sms_boundary_is_90_bytes()
    {
        Assert.Equal(MessageType.SMS, MessageComposer.DetectType(ByteCounter.CountBytes(new string('가', 45)), false));
        Assert.Equal(MessageType.LMS, MessageComposer.DetectType(ByteCounter.CountBytes(new string('가', 45) + "a"), false));
        Assert.Equal(MessageType.MMS, MessageComposer.DetectType(10, true));
    }
}

public class MessageComposerTests
{
    private static Recipient R(string? name, Dictionary<string, string>? vars = null) => new()
    {
        RowNumber = 1, RawPhone = "01012345678", Phone = "01012345678", Name = name,
        Variables = vars ?? [], Status = RecipientStatus.Valid,
    };

    [Fact]
    public void Replaces_name_and_custom_variables()
    {
        var text = MessageComposer.ApplyVariables("#{이름}님, 쿠폰 #{쿠폰} / #{없음}", R("홍길동", new() { ["쿠폰"] = "A1" }));
        Assert.Equal("홍길동님, 쿠폰 A1 / ", text);
    }

    [Fact]
    public void Extracts_variables_in_order_without_duplicates()
    {
        Assert.Equal(["이름", "쿠폰"], MessageComposer.ExtractVariables("#{이름} #{쿠폰} #{이름}"));
    }

    [Fact]
    public void Ad_format_adds_prefix_and_opt_out_once()
    {
        var ad = new AdOptions { SenderName = "행복폰", OptOutNumber = "080-123-4567" };
        var once = MessageComposer.ApplyAdFormat("신상 입고!", ad);
        Assert.Equal("(광고)행복폰\n신상 입고!\n무료거부 0801234567", once);

        // Áp dụng lại không bị lặp
        Assert.Equal(once, MessageComposer.ApplyAdFormat("(광고) 신상 입고!\n무료거부 0801234567", ad));
    }

    [Fact]
    public void Compose_combines_variables_and_ad()
    {
        var ad = new AdOptions { OptOutNumber = "0801234567" };
        Assert.Equal("(광고)\n김님 안녕\n무료거부 0801234567", MessageComposer.Compose("#{이름}님 안녕", R("김"), ad));
    }

    [Theory]
    [InlineData("a.jpg", 100_000, true)]
    [InlineData("a.JPEG", 204_800, true)]
    [InlineData("a.jpg", 204_801, false)]
    [InlineData("a.png", 1000, false)]
    public void Mms_image_rules(string path, long size, bool ok) =>
        Assert.Equal(ok, MessageComposer.IsValidMmsImage(path, size, out _));
}

public class AdPolicyTests
{
    private static DateTimeOffset Kst(int day, int hour, int minute = 0) =>
        new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(9));

    [Theory]
    [InlineData(20, 59, false)]
    [InlineData(21, 0, true)]
    [InlineData(23, 30, true)]
    [InlineData(0, 0, true)]
    [InlineData(7, 59, true)]
    [InlineData(8, 0, false)]
    public void Quiet_hours_are_21_to_08_kst(int hour, int minute, bool quiet) =>
        Assert.Equal(quiet, AdPolicy.IsQuietHours(Kst(15, hour, minute)));

    [Fact]
    public void Quiet_hours_use_korea_time_even_from_other_zone()
    {
        // 13:00 UTC = 22:00 KST
        Assert.True(AdPolicy.IsQuietHours(new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Next_allowed_time_is_8am_kst()
    {
        Assert.Equal(Kst(16, 8), AdPolicy.NextAllowedTime(Kst(15, 22)));
        Assert.Equal(Kst(16, 8), AdPolicy.NextAllowedTime(Kst(16, 3)));
        Assert.Equal(Kst(16, 12), AdPolicy.NextAllowedTime(Kst(16, 12)));
    }
}

public class CostCalculatorTests
{
    [Fact]
    public void Sums_per_type()
    {
        var est = CostCalculator.Estimate(
            [MessageType.SMS, MessageType.SMS, MessageType.LMS, MessageType.MMS], new PriceTable(10, 30, 100));
        Assert.Equal((2, 1, 1, 150m), (est.SmsCount, est.LmsCount, est.MmsCount, est.Total));
    }
}

public class BulkSenderMappingTests
{
    [Fact]
    public void Maps_accepted_and_failed_by_custom_field()
    {
        OutgoingMessage[] batch =
        [
            new(1, "01011112222", "a", MessageType.SMS),
            new(2, "01033334444", "b", MessageType.SMS),
            new(3, "01055556666", "c", MessageType.SMS),
        ];
        var res = new SendManyResponse
        {
            FailedMessageList =
            [
                new FailedMessage { To = "01033334444", StatusCode = "1062", StatusMessage = "blocked", CustomFields = new() { ["rid"] = "2" } },
            ],
            MessageList =
            [
                new AcceptedMessage { MessageId = "M1", StatusCode = "2000", CustomFields = new() { ["rid"] = "1" } },
            ],
        };

        var results = BulkSender.MapBatchResult(batch, res).ToList();

        Assert.Equal(RecipientSendState.Accepted, results[0].State);
        Assert.Equal("M1", results[0].MessageId);
        Assert.Equal(RecipientSendState.Rejected, results[1].State);
        Assert.Equal("1062", results[1].StatusCode);
        Assert.Equal(RecipientSendState.Accepted, results[2].State);
    }
}
