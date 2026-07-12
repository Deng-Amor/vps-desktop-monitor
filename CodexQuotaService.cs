using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KomariDeskWidget;

public static class CodexQuotaService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(18) };

    public static async Task<CodexQuotaSnapshot> ReadAsync()
    {
        try
        {
            var authPath = Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "auth.json");
            using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(authPath));
            var tokens = auth.RootElement.TryGetProperty("tokens", out var nested) ? nested : auth.RootElement;
            var accessToken = GetString(tokens, "access_token") ?? GetString(tokens, "accessToken")
                ?? throw new InvalidOperationException("Codex 登录已过期，请重新登录。");
            var accountId = GetString(tokens, "account_id") ?? GetString(tokens, "accountId") ?? AccountIdFromJwt(accessToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("originator", "Codex Desktop");
            request.Headers.Add("OAI-Product-Sku", "CODEX");
            if (!string.IsNullOrWhiteSpace(accountId)) request.Headers.Add("ChatGPT-Account-Id", accountId);

            using var response = await Http.SendAsync(request);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return CodexQuotaSnapshot.Failure("Codex 登录已过期，请在 Codex Desktop 中重新登录。");
            response.EnsureSuccessStatusCode();
            using var usage = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return ParseSnapshot(usage.RootElement);
        }
        catch (TaskCanceledException)
        {
            return CodexQuotaSnapshot.Failure("Codex 额度读取超时，请稍后刷新。");
        }
        catch (FileNotFoundException) { return CodexQuotaSnapshot.Failure("未找到 Codex 登录，请先登录 Codex Desktop。"); }
        catch (Exception ex)
        {
            return CodexQuotaSnapshot.Failure($"Codex 读取失败：{ex.Message}");
        }
    }

    private static string? AccountIdFromJwt(string token)
    {
        try
        {
            var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
            using var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part)));
            return GetString(payload.RootElement, "https://api.openai.com/auth.chatgpt_account_id")
                ?? GetString(payload.RootElement, "chatgpt_account_id");
        }
        catch { return null; }
    }

    private static CodexQuotaSnapshot ParseSnapshot(JsonElement usage)
    {
        var rateLimits = GetObject(usage, "rate_limit") ?? GetObject(usage, "rateLimit") ?? usage;

        var primary = ParseWindow(GetObject(rateLimits, "primary_window") ?? GetObject(rateLimits, "primaryWindow") ?? GetObject(rateLimits, "primary"));
        var secondary = ParseWindow(GetObject(rateLimits, "secondary_window") ?? GetObject(rateLimits, "secondaryWindow") ?? GetObject(rateLimits, "secondary"));
        var plan = GetString(usage, "plan_type") ?? GetString(usage, "planType");
        var resetCredits = ParseResetCredits(usage);
        var title = string.IsNullOrWhiteSpace(plan) ? "Codex" : $"Codex {plan.ToUpperInvariant()}";

        return new CodexQuotaSnapshot
        {
            Status = "ok",
            Title = title,
            Primary = primary,
            Secondary = secondary,
            ResetCredits = resetCredits,
            UpdatedAt = DateTime.Now,
            Message = primary is null ? "没有读取到 5 小时额度窗口。" : null
        };
    }

    private static CodexQuotaWindow? ParseWindow(JsonElement? window)
    {
        if (window is null) return null;
        var used = GetInt(window.Value, "usedPercent") ?? GetInt(window.Value, "used_percent");
        var resetsAt = GetLong(window.Value, "resetsAt") ?? GetLong(window.Value, "resets_at") ?? GetLong(window.Value, "reset_at");
        var durationSeconds = GetLong(window.Value, "limit_window_seconds") ?? GetLong(window.Value, "windowSeconds");
        var duration = GetLong(window.Value, "windowDurationMins") ?? GetLong(window.Value, "window_duration_mins") ?? durationSeconds / 60;
        if (used is null && resetsAt is null && duration is null) return null;

        return new CodexQuotaWindow
        {
            UsedPercent = Math.Clamp(used ?? 0, 0, 100),
            ResetsAtUnix = resetsAt,
            WindowDurationMins = duration
        };
    }

    private static int? ParseResetCredits(JsonElement limitsResult)
    {
        var credits = GetObject(limitsResult, "rateLimitResetCredits") ?? GetObject(limitsResult, "rate_limit_reset_credits");
        if (credits is null) return null;
        return GetInt(credits.Value, "availableCount") ?? GetInt(credits.Value, "available_count");
    }

    private static JsonElement? GetObject(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            _ => null
        };
    }

    private static long? GetLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            _ => null
        };
    }

}

public sealed class CodexQuotaSnapshot
{
    public string Status { get; init; } = "loading";
    public string Title { get; init; } = "Codex";
    public CodexQuotaWindow? Primary { get; init; }
    public CodexQuotaWindow? Secondary { get; init; }
    public int? ResetCredits { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
    public string? Message { get; init; }
    public bool IsOk => Status == "ok" && Primary is not null;
    public string PrimaryPercentText => Primary is null ? "--%" : $"{Primary.RemainingPercent}%";
    public string PrimaryUsedText => Primary is null ? "5h --" : $"5h 已用 {Primary.UsedPercent}%";
    public string PrimaryResetText => Primary?.ResetText ?? "重置时间未知";
    public string SecondaryPercentText => Secondary is null ? "--%" : $"{Secondary.RemainingPercent}%";
    public string SecondaryText => Secondary is null ? "周额度 --" : $"周额度剩余 {Secondary.RemainingPercent}%";
    public string SecondaryResetText => Secondary?.ResetText ?? "周重置时间未知";
    public string ResetCreditsText => ResetCredits is null ? "重置券 --" : $"重置券 {ResetCredits}";
    public string UpdatedText => $"更新于 {UpdatedAt:HH:mm:ss}";
    public string MessageText => Message ?? "Codex 状态正常";
    public string StateColor => IsOk ? "#22C55E" : "#F59E0B";
    public double PrimaryRemaining => Primary?.RemainingPercent ?? 0;
    public double SecondaryRemaining => Secondary?.RemainingPercent ?? 0;

    public static CodexQuotaSnapshot Loading() => new() { Status = "loading", Message = "正在读取 Codex 状态…" };
    public static CodexQuotaSnapshot Failure(string message) => new() { Status = "unavailable", Message = message };
}

public sealed class CodexQuotaWindow
{
    public int UsedPercent { get; init; }
    public int RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
    public long? ResetsAtUnix { get; init; }
    public long? WindowDurationMins { get; init; }
    public string ResetText
    {
        get
        {
            if (ResetsAtUnix is null) return "重置时间未知";
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(ResetsAtUnix.Value).LocalDateTime;
            return resetAt.Date == DateTime.Today
                ? $"今天 {resetAt:HH:mm} 重置"
                : $"{resetAt:M月d日 HH:mm} 重置";
        }
    }
}
