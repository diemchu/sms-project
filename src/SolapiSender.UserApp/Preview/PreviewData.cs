using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Models.Firebase;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.Preview;

// Chế độ xem trước giao diện (chỉ bật được ở bản Debug với tham số --preview):
// thay mạng bằng dữ liệu mẫu để xem toàn bộ màn hình mà không cần Firebase/SOLAPI thật.
// Mọi code ViewModel/Core vẫn chạy thật; chỉ tầng HTTP bị thay.

public static class PreviewData
{
    public const string Uid = "preview-user";

    public static void StartSession(SessionService session, FirebaseAuthService _) =>
        session.Start(new UserProfile
        {
            Uid = Uid,
            Email = "demo@shop.kr",
            StoreName = "행복폰 강남점",
            Phone = "01012345678",
            Status = UserStatus.Active,
            ExpiresAt = DateTimeOffset.Now.AddDays(45),
            MaxDevices = 2,
            DeviceIds = ["preview-pc"],
        });
}

public sealed class PreviewTokenProvider : IIdTokenProvider
{
    public Task<string> GetIdTokenAsync(CancellationToken ct = default) => Task.FromResult("preview-token");
}

public sealed class PreviewHttpHandler : HttpMessageHandler
{
    private static readonly Random Rng = new(7);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(350, ct);
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri.Query;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

        if (request.RequestUri.Host == "api.solapi.com")
            return await Solapi(request.Method, path, query, body, ct);
        if (request.RequestUri.Host == "firestore.googleapis.com")
            return Firestore(request.Method, path);
        return Json(HttpStatusCode.NotFound, "{}");
    }

    private static async Task<HttpResponseMessage> Solapi(HttpMethod method, string path, string query, string? body, CancellationToken ct)
    {
        switch (path)
        {
            case "/cash/v1/balance":
                return Json(HttpStatusCode.OK, """{"balance":8250,"point":1200,"deposit":0}""");

            case "/senderid/v1/numbers/active":
                return Json(HttpStatusCode.OK, """["0215881234","01098765432","07012345678"]""");

            case "/messages/v4/statistics":
            {
                // Khoảng ngắn hơn 2 ngày = thống kê "hôm nay", ngược lại = "tháng này".
                var q = System.Web.HttpUtility.ParseQueryString(query);
                var isToday = DateTimeOffset.TryParse(q["startDate"], out var from)
                              && DateTimeOffset.TryParse(q["endDate"], out var to)
                              && to - from < TimeSpan.FromDays(2);
                return Json(HttpStatusCode.OK, isToday
                    ? """{"balance":0,"point":0,"total":{"total":128,"sms":90,"lms":38,"mms":0},"successed":{"total":124},"failed":{"total":4}}"""
                    : """{"balance":0,"point":0,"total":{"total":3420,"sms":2100,"lms":1200,"mms":120},"successed":{"total":3351},"failed":{"total":69}}""");
            }

            case "/messages/v4/groups":
            {
                var groups = new JsonObject();
                string[] statuses = ["COMPLETE", "COMPLETE", "SCHEDULED", "COMPLETE", "COMPLETE", "FAILED"];
                for (var i = 0; i < 6; i++)
                {
                    var total = new[] { 320, 58, 1200, 15, 410, 3 }[i];
                    var failed = i == 5 ? 3 : Rng.Next(0, 6);
                    var id = $"G4V2025PREVIEW{i:D2}";
                    groups[id] = new JsonObject
                    {
                        ["groupId"] = id,
                        ["status"] = statuses[i],
                        ["count"] = new JsonObject { ["total"] = total, ["sentSuccess"] = total - failed, ["sentFailed"] = failed },
                        ["balance"] = new JsonObject { ["sum"] = total * 18 },
                        ["dateCreated"] = DateTime.UtcNow.AddHours(-i * 19).ToString("o"),
                        ["scheduledDate"] = statuses[i] == "SCHEDULED" ? DateTime.UtcNow.AddHours(20).ToString("o") : null,
                    };
                }
                return Json(HttpStatusCode.OK, new JsonObject { ["limit"] = 50, ["groupList"] = groups }.ToJsonString());
            }

            case "/messages/v4/list":
            {
                var list = new JsonObject();
                string[] names = ["김민준", "이서연", "박지호", "최수아", "정우진"];
                for (var i = 0; i < 40; i++)
                {
                    var code = i % 9 == 4 ? "3059" : i % 13 == 7 ? "3000" : "4000";
                    var id = $"M4V2025PREVIEW{i:D3}";
                    list[id] = new JsonObject
                    {
                        ["messageId"] = id,
                        ["groupId"] = $"G4V2025PREVIEW{i % 6:D2}",
                        ["to"] = $"010{Rng.Next(10000000, 99999999)}",
                        ["from"] = "0215881234",
                        ["type"] = i % 3 == 0 ? "LMS" : "SMS",
                        ["text"] = $"{names[i % 5]}님, 행복폰 강남점입니다. 갤럭시 신모델 입고 안내드립니다.",
                        ["status"] = code == "3000" ? "SENDING" : "COMPLETE",
                        ["statusCode"] = code,
                        ["reason"] = code == "3059" ? "변작된 발신번호" : null,
                        ["dateCreated"] = DateTime.UtcNow.AddMinutes(-i * 47).ToString("o"),
                    };
                }
                return Json(HttpStatusCode.OK, new JsonObject { ["limit"] = 100, ["messageList"] = list }.ToJsonString());
            }

            case "/storage/v1/files":
                return Json(HttpStatusCode.OK, """{"fileId":"ST01PREVIEWIMAGE","type":"MMS"}""");

            case "/messages/v4/send-many/detail" when method == HttpMethod.Post:
            {
                await Task.Delay(900, ct);
                using var doc = JsonDocument.Parse(body ?? "{}");
                var failed = new JsonArray();
                var accepted = new JsonArray();
                foreach (var m in doc.RootElement.GetProperty("messages").EnumerateArray())
                {
                    var to = m.GetProperty("to").GetString() ?? "";
                    var cf = JsonNode.Parse(m.GetProperty("customFields").GetRawText());
                    if (to.EndsWith("0000"))
                        failed.Add(new JsonObject { ["to"] = to, ["statusCode"] = "1062", ["statusMessage"] = "수신거부 번호", ["customFields"] = cf });
                    else
                        accepted.Add(new JsonObject { ["messageId"] = "M" + Guid.NewGuid().ToString("N")[..12], ["statusCode"] = "2000", ["statusMessage"] = "정상 접수", ["customFields"] = cf });
                }
                var res = new JsonObject
                {
                    ["groupInfo"] = new JsonObject { ["groupId"] = "G" + Guid.NewGuid().ToString("N")[..14], ["count"] = new JsonObject { ["total"] = accepted.Count + failed.Count } },
                    ["failedMessageList"] = failed,
                    ["messageList"] = accepted,
                };
                return Json(HttpStatusCode.OK, res.ToJsonString());
            }
        }
        return Json(HttpStatusCode.NotFound, """{"errorCode":"NotFound","errorMessage":"preview"}""");
    }

    private static HttpResponseMessage Firestore(HttpMethod method, string path)
    {
        if (method == HttpMethod.Get && path.EndsWith("/templates"))
        {
            return Json(HttpStatusCode.OK, $$"""
                {"documents":[
                  {{Template("t1", "신모델 입고 안내", "#{이름}님, 행복폰 강남점입니다.\n갤럭시 신모델이 입고되었습니다. 방문 시 사은품 증정!", true)}},
                  {{Template("t2", "약정 만료 안내", "#{이름}님, 고객님의 약정이 #{만료일}에 종료됩니다. 기기변경 상담 환영합니다.", false)}}
                ]}
                """);
        }
        if (method == HttpMethod.Get) return Json(HttpStatusCode.NotFound, """{"error":{"code":404,"status":"NOT_FOUND"}}""");
        if (method == HttpMethod.Post && !path.EndsWith(":commit"))
            return Json(HttpStatusCode.OK, $$"""{"name":"{{path}}/{{Guid.NewGuid():N}}"}""");
        return Json(HttpStatusCode.OK, "{}");
    }

    private static string Template(string id, string name, string content, bool isAd)
    {
        var fields = FirestoreMapper.ToFields(new MessageTemplate
        {
            Name = name, Content = content, IsAd = isAd,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        return new JsonObject { ["name"] = $"projects/p/databases/(default)/documents/users/{PreviewData.Uid}/templates/{id}", ["fields"] = fields }.ToJsonString();
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
