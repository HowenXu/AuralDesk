using System;
using System.Globalization;
using System.Windows;

namespace AuralDesk
{
    /// <summary>界面语言：0=自动 1=中文 2=English。文本资源在 Strings.zh.xaml / Strings.en.xaml（x:Key 相同），
    /// XAML 用 {DynamicResource key} 引用，代码用 Lang.T(key) 读取；切换语言时替换资源字典即时生效。</summary>
    public static class Lang
    {
        public static bool IsEnglish { get; private set; }

        /// <summary>解析当前界面语言（设置项优先，自动则跟随系统）。</summary>
        public static string Resolve(AppSettings s)
        {
            if (s.Language == 1) { IsEnglish = false; return "zh"; }
            if (s.Language == 2) { IsEnglish = true; return "en"; }
            var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var sys = string.Equals(ui, "zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
            IsEnglish = sys == "en";
            return sys;
        }

        /// <summary>从当前语言资源取文本；未命中返回 key 本身。</summary>
        public static string T(string key)
        {
            var r = Application.Current?.TryFindResource(key);
            return r as string ?? key;
        }

        /// <summary>把指定语言字典设为全局第一个资源（zh/en 任意值都归一到文件名）。</summary>
        public static void Apply(string lang, App app)
        {
            var name = lang == "zh" ? "Strings.zh.xaml" : "Strings.en.xaml";
            var uri = new Uri("/" + name, UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };
            // 移除旧语言字典（保留其它全局资源）
            var merged = app.Resources.MergedDictionaries;
            for (var i = merged.Count - 1; i >= 0; i--)
            {
                if (merged[i].Source != null && merged[i].Source.OriginalString.Contains("Strings."))
                    merged.RemoveAt(i);
            }
            merged.Insert(0, dict);
            IsEnglish = lang != "zh";
        }
    }
}
