using System;
using System.Globalization;
using System.Windows;

namespace AuralDesk
{
    /// <summary>界面语言：0=自动 1=中文 2=English。文本资源在 Strings.zh.xaml / Strings.en.xaml（x:Key 相同），
    /// XAML 用 {DynamicResource key} 引用，代码用 Lang.T/Lang.F 读取；切换语言时替换资源字典即时生效。</summary>
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

        /// <summary>取界面文本；key 即中文原文（English 时查英文资源，找不到回退原文）。</summary>
        public static string T(string zh)
        {
            if (!IsEnglish) return zh;
            var r = Application.Current?.TryFindResource(zh);
            return r as string ?? zh;
        }

        /// <summary>同 T，占位符 {0}/{1}…（或 @@0@@…）替换为实参，避免 string.Format 的花括号问题。</summary>
        public static string F(string zhTemplate, params object[] args)
        {
            var t = T(zhTemplate);
            if (args == null) return t;
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i]?.ToString() ?? "";
                t = t.Replace("{" + i + "}", a).Replace("@@" + i + "@@", a);
            }
            return t;
        }

        /// <summary>把指定语言字典设为全局第一个资源（zh/en 值归一到文件名）。</summary>
        public static void Apply(string lang, App app)
        {
            var name = lang == "zh" ? "Strings.zh.xaml" : "Strings.en.xaml";
            var uri = new Uri("/" + name, UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };
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
