using SolapiSender.Core.Messaging;
using SolapiSender.Core.Models;

namespace SolapiSender.Core.Tests;

public class PhoneNumberHelperTests
{
    [Theory]
    [InlineData("010-1234-5678", "01012345678")]
    [InlineData("010 1234 5678", "01012345678")]
    [InlineData("01012345678", "01012345678")]
    [InlineData("+82 10-1234-5678", "01012345678")]
    [InlineData("82-10-1234-5678", "01012345678")]
    [InlineData("0082 10 1234 5678", "01012345678")]
    [InlineData("1012345678", "01012345678")]       // Excel làm mất số 0 đầu
    [InlineData("０１０-１２３４-５６７８", "01012345678")] // full-width
    [InlineData("011-123-4567", "0111234567")]
    [InlineData("016-1234-5678", "01612345678")]
    public void Normalize_valid(string raw, string expected) =>
        Assert.Equal(expected, PhoneNumberHelper.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("010-123-4567")]   // 010 phải đủ 11 số
    [InlineData("02-123-4567")]    // số bàn
    [InlineData("010-1234-56789")]
    [InlineData("015-1234-5678")]
    public void Normalize_invalid(string? raw) => Assert.Null(PhoneNumberHelper.Normalize(raw));

    [Theory]
    [InlineData("01012345678", "010-1234-5678")]
    [InlineData("0111234567", "011-123-4567")]
    public void Format(string digits, string expected) => Assert.Equal(expected, PhoneNumberHelper.Format(digits));
}

public class RecipientListBuilderTests
{
    [Fact]
    public void ParsePasted_supports_number_and_name()
    {
        var raws = RecipientListBuilder.ParsePasted("010-1111-2222,김철수\r\n\r\n01033334444\n010 5555 6666\t이영희\n");
        Assert.Equal(3, raws.Count);
        Assert.Equal("김철수", raws[0].Name);
        Assert.Null(raws[1].Name);
        Assert.Equal("이영희", raws[2].Name);
        Assert.Equal(4, raws[2].RowNumber);
    }

    [Fact]
    public void Build_marks_invalid_and_duplicates()
    {
        var list = RecipientListBuilder.Build(RecipientListBuilder.ParsePasted(
            "010-1111-2222,A\n01011112222,B\n12345\n010-3333-4444"));

        Assert.Equal(RecipientStatus.Valid, list[0].Status);
        Assert.Equal(RecipientStatus.Duplicate, list[1].Status);
        Assert.Equal(RecipientStatus.Invalid, list[2].Status);
        Assert.Equal(RecipientStatus.Valid, list[3].Status);

        var s = RecipientListBuilder.Summarize(list);
        Assert.Equal((4, 2, 1, 1), (s.Total, s.Valid, s.Invalid, s.Duplicate));
    }
}
