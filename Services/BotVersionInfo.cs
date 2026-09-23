namespace PragmaticBot.Server.Services;

/// <summary>
/// 봇과 짝을 이루는 "현재" 봇 버전. ServerConfig.RequiredBotVersion 의 시드 기본값으로 쓴다.
/// 실제 게이트 값은 관리자 화면에서 정하며, 봇의 AppVersion.Current 와 이 값을 함께 올린다.
/// </summary>
public static class BotVersionInfo
{
    public const string Current = "1.3.0";
}
