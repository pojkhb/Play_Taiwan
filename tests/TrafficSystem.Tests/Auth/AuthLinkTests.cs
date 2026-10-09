// 驗證信、忘記密碼信裡的連結網址：設定 PublicBaseUrl 優先，否則用 App 呼叫後端時的網址
using Allure.Net.Commons.Attributes;
using backend.Services;

namespace TrafficSystem.Tests.Auth;

[AllureSuiteHierarchy("帳號與身分驗證", "單元測試")]
[AllureBddHierarchy("帳號與身分驗證", "單元測試")]
public class AuthLinkTests
{
    [Theory]
    [InlineData("https://playtaiwan.example.com", "http://192.168.1.5:5501", "https://playtaiwan.example.com")]
    [InlineData("https://playtaiwan.example.com/", "http://192.168.1.5:5501", "https://playtaiwan.example.com")]
    [InlineData(null, "http://192.168.1.5:5501", "http://192.168.1.5:5501")]
    [InlineData("  ", "http://192.168.1.5:5501", "http://192.168.1.5:5501")]
    [InlineData(null, null, "http://localhost:5501")]
    public void 信件連結網址_設定的網址優先_否則用App連後端的網址(string configured, string request, string expected)
    {
        Assert.Equal(expected, AuthService.ResolveBaseUrl(configured, request));
    }
}
