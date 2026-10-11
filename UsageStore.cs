using System;
using System.IO;
using System.Text.Json;

namespace CmjLimit
{
    internal sealed class UsageState
    {
        public string Date { get; set; }
        public int UsedSeconds { get; set; }
    }

    internal static class UsageStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static string TodayKey()
        {
            return DateTime.Today.ToString("yyyy-MM-dd");
        }

        public static UsageState Load()
        {
            var today = TodayKey();
            try
            {
                if (File.Exists(AppPaths.UsageFile))
                {
                    var state = JsonSerializer.Deserialize<UsageState>(File.ReadAllText(AppPaths.UsageFile), JsonOpts);
                    // 只累计“当天”的使用时间，跨天视为新的一天
                    if (state != null && state.Date == today && state.UsedSeconds > 0)
                        return state;
                }
            }
            catch
            {
                // 忽略损坏的状态文件
            }

            return new UsageState { Date = today, UsedSeconds = 0 };
        }

        public static void Save(UsageState state)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Dir);
                File.WriteAllText(AppPaths.UsageFile, JsonSerializer.Serialize(state, JsonOpts));
            }
            catch
            {
                // 忽略写入失败
            }
        }
    }
}
