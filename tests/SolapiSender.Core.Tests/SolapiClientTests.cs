using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SolapiSender.Core.Models.Solapi;
using SolapiSender.Core.Solapi;

namespace SolapiSender.Core.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        return respond(request, Requests.Count);
    }

    public static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed class StaticCredentials(SolapiCredentials? c) : ISolapiCredentialProvider
{
    public SolapiCredentials? GetCredentials() => c;
}

public class SolapiSignerTests
{
    [Fact]
    public void Signature_matches_openssl_reference_vector()
    {
        // printf '%s' '2024-01-01T00:00:00.000Zabcdef0123456789abcdef0123456789' | openssl dgst -sha256 -hmac 'TEST_SECRET'
        var sig = SolapiSigner.ComputeSignature("TEST_SECRET", "2024-01-01T00:00:00.000Z", "abcdef0123456789abcdef0123456789");
        Assert.Equal("e5df0f816b824b7fea5ba8ad64fea9fde7007b1d24f7a8e26ede992458528acf", sig);
    }

    [Fact]
    public void Header_has_expected_format()
    {
        var header = SolapiSigner.CreateAuthorization("KEY", "SECRET",
            new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.FromHours(9)), "abcdef0123456789abcdef0123456789");
        Assert.StartsWith("HMAC-SHA256 apiKey=KEY, date=2024-01-01T00:00:00.000Z, salt=abcdef0123456789abcdef0123456789, signature=", header);
    }

    [Fact]
    public void Salt_is_32_hex_and_random()
    {
        var a = SolapiSigner.CreateSalt();
        Assert.Matches("^[0-9a-f]{32}$", a);
        Assert.NotEqual(a, SolapiSigner.CreateSalt());
    }
}

public class SolapiClientTests
{
    private static (SolapiClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri(SolapiClient.DefaultBaseUrl) };
        return (new SolapiClient(http, new StaticCredentials(new SolapiCredentials("K", "S"))), handler);
    }

    [Fact]
    public async Task GetBalance_signs_request_and_parses()
    {
        var (client, handler) = Create((_, _) => FakeHandler.Json(HttpStatusCode.OK, """{"balance":12345.5,"point":"300","deposit":0}"""));

        var b = await client.GetBalanceAsync();

        Assert.Equal(12345.5m, b.Balance);
        Assert.Equal(300m, b.Point);
        var req = handler.Requests.Single().Request;
        Assert.Equal("https://api.solapi.com/cash/v1/balance", req.RequestUri!.ToString());
        var auth = req.Headers.GetValues("Authorization").Single();
        Assert.Matches(new Regex("^HMAC-SHA256 apiKey=K, date=[^,]+, salt=[0-9a-f]{32}, signature=[0-9a-f]{64}$"), auth);
    }

    [Fact]
    public async Task Invalid_key_maps_to_friendly_kind_without_retry()
    {
        var (client, handler) = Create((_, _) => FakeHandler.Json(HttpStatusCode.Forbidden,
            """{"errorCode":"InvalidApiKey","errorMessage":"Invalid API Key"}"""));

        var ex = await Assert.ThrowsAsync<SolapiException>(() => client.GetBalanceAsync());

        Assert.Equal(SolapiErrorKind.InvalidCredentials, ex.Kind);
        Assert.Equal("InvalidApiKey", ex.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Get_retries_on_server_error_with_new_signature()
    {
        var (client, handler) = Create((_, n) => n < 3
            ? FakeHandler.Json(HttpStatusCode.ServiceUnavailable, """{"errorCode":"ServerError","errorMessage":"x"}""")
            : FakeHandler.Json(HttpStatusCode.OK, """{"balance":1,"point":0}"""));

        await client.GetBalanceAsync();

        Assert.Equal(3, handler.Requests.Count);
        var auths = handler.Requests.Select(r => r.Request.Headers.GetValues("Authorization").Single()).ToHashSet();
        Assert.Equal(3, auths.Count);
    }

    [Fact]
    public async Task Send_is_not_retried_on_server_error()
    {
        var (client, handler) = Create((_, _) => FakeHandler.Json(HttpStatusCode.InternalServerError,
            """{"errorCode":"InternalError","errorMessage":"x"}"""));

        var req = new SendManyRequest { Messages = [new SendMessageItem { To = "01011112222", From = "0212345678", Text = "t" }] };
        var ex = await Assert.ThrowsAsync<SolapiException>(() => client.SendManyDetailAsync(req));

        Assert.Equal(SolapiErrorKind.Server, ex.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Send_serializes_expected_schema()
    {
        var (client, handler) = Create((_, _) => FakeHandler.Json(HttpStatusCode.OK, """
            {"groupInfo":{"groupId":"G1","count":{"total":1,"registeredSuccess":1},"dateCreated":"2024-01-01T00:00:00.000Z","scheduledDate":""},
             "failedMessageList":[],
             "messageList":[{"messageId":"M1","statusCode":"2000","statusMessage":"ok","customFields":{"rid":"7"}}]}
            """));

        var res = await client.SendManyDetailAsync(new SendManyRequest
        {
            Messages = [new SendMessageItem { To = "01011112222", From = "0212345678", Text = "안녕", Type = "SMS", CustomFields = new() { ["rid"] = "7" } }],
            ScheduledDate = "2024-01-02T00:00:00.000Z",
        });

        Assert.Equal("G1", res.GroupInfo!.GroupId);
        Assert.Null(res.GroupInfo.ScheduledDate);
        Assert.Equal("M1", res.MessageList![0].MessageId);

        var (request, body) = handler.Requests.Single();
        Assert.Equal("https://api.solapi.com/messages/v4/send-many/detail", request.RequestUri!.ToString());
        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.True(root.GetProperty("showMessageList").GetBoolean());
        Assert.Equal("2024-01-02T00:00:00.000Z", root.GetProperty("scheduledDate").GetString());
        var msg = root.GetProperty("messages")[0];
        Assert.Equal("01011112222", msg.GetProperty("to").GetString());
        Assert.Equal("SMS", msg.GetProperty("type").GetString());
        Assert.False(msg.TryGetProperty("imageId", out _)); // null bị bỏ qua
    }

    [Fact]
    public async Task Message_list_parses_keyed_object_and_query()
    {
        var (client, handler) = Create((_, _) => FakeHandler.Json(HttpStatusCode.OK, """
            {"limit":2,"nextKey":"N","messageList":{"M1":{"messageId":"M1","to":"01011112222","statusCode":"4000","status":"COMPLETE"}}}
            """));

        var res = await client.GetMessagesAsync(new MessageListQuery
        {
            StartDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Limit = 2,
        });

        Assert.Equal("N", res.NextKey);
        Assert.Equal("4000", res.MessageList["M1"].StatusCode);
        Assert.Equal("/messages/v4/list?startDate=2024-01-01T00%3A00%3A00.000Z&limit=2",
            handler.Requests.Single().Request.RequestUri!.PathAndQuery);
    }

    [Theory]
    [InlineData("""["01012345678","02-123-4567"]""")]
    [InlineData("""[{"phoneNumber":"01012345678"},{"phoneNumber":"021234567"}]""")]
    [InlineData("""{"numbers":["01012345678","021234567"]}""")]
    public void Sender_numbers_parse_flexibly(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = SolapiClient.ParseSenderNumbers(doc.RootElement);
        Assert.Equal(2, list.Count);
        Assert.Equal("01012345678", list[0]);
    }

    [Fact]
    public async Task Missing_credentials_throws_before_network()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException());
        var client = new SolapiClient(new HttpClient(handler), new StaticCredentials(null));

        var ex = await Assert.ThrowsAsync<SolapiException>(() => client.GetBalanceAsync());
        Assert.Equal(SolapiErrorKind.InvalidCredentials, ex.Kind);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "NotEnoughBalance", SolapiErrorKind.InsufficientBalance)]
    [InlineData(HttpStatusCode.BadRequest, "InvalidSenderNumber", SolapiErrorKind.SenderNotRegistered)]
    [InlineData(HttpStatusCode.Unauthorized, "SignatureDoesNotMatch", SolapiErrorKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.TooManyRequests, null, SolapiErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, "ValidationError", SolapiErrorKind.InvalidRequest)]
    public void Classifier(HttpStatusCode status, string? code, SolapiErrorKind expected) =>
        Assert.Equal(expected, SolapiErrorClassifier.Classify(status, code, null));
}
