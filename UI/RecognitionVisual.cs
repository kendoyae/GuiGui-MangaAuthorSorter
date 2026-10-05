using System;
using System.Collections.Generic;
using System.Drawing;

namespace MangaAuthorSorter
{
    internal sealed class RecognitionVisual
    {
        public string Mark = "";
        public string Code = "";
        public string Name = "";
        public Color Color = Color.DimGray;
    }

    internal sealed class RecognitionLegendItem
    {
        public string Mark = "";
        public string Code = "";
        public string Name = "";
        public Color Color = Color.DimGray;
    }

    internal static class RecognitionVisualResolver
    {
        private static readonly Color DirectColor =
            Color.FromArgb(21, 128, 61);      // #15803D
        private static readonly Color NormalizeColor =
            Color.FromArgb(37, 99, 235);      // #2563EB
        private static readonly Color AliasColor =
            Color.FromArgb(147, 51, 234);     // #9333EA
        private static readonly Color EntityColor =
            Color.FromArgb(15, 118, 110);     // #0F766E
        private static readonly Color SocietyColor =
            Color.FromArgb(8, 145, 178);      // #0891B2
        private static readonly Color AuthorColor =
            Color.FromArgb(67, 56, 202);      // #4338CA
        private static readonly Color NewAuthorColor =
            Color.FromArgb(0, 137, 123);      // #00897B
        private static readonly Color NewReuseColor =
            Color.FromArgb(100, 116, 139);    // #64748B
        private static readonly Color AmbiguousColor =
            Color.FromArgb(234, 88, 12);      // #EA580C
        private static readonly Color ManualColor =
            Color.FromArgb(161, 98, 7);       // #A16207
        private static readonly Color ErrorColor =
            Color.FromArgb(220, 38, 38);      // #DC2626

        public static RecognitionVisual Resolve(PlanItem item)
        {
            if (item == null)
                return Make("", "", "", Color.DimGray);

            string why = item.MatchWhy ?? "";
            string status = item.Status ?? "";

            // Manual assignment has the highest priority.
            if (Contains(why, "手动指定作者文件夹") ||
                Contains(status, "手动指定"))
            {
                return Make("[★]", "manual", "手动指定", ManualColor);
            }

            if (Contains(status, "无法识别作者"))
            {
                return Make("[×]", "unrecognized", "未识别", ErrorColor);
            }

            if (Contains(status, "歧义") ||
                Contains(status, "需要选择") ||
                Contains(why, "多个") ||
                Contains(why, "分别匹配到不同") ||
                Contains(why, "请双击“识别依据”选择") ||
                Contains(why, "候选"))
            {
                return Make("[!]", "ambiguous", "歧义", AmbiguousColor);
            }

            // New-author states are determined by the plan status, not by the
            // matching explanation. This prevents a new author normalized via
            // the alias library from being displayed as an existing alias match.
            if (String.Equals(status, "本轮新作者（复用目录）", StringComparison.Ordinal))
            {
                return Make("[+]", "new-reuse", "新作者复用", NewReuseColor);
            }

            if (status.StartsWith("新作者，计划放入 ", StringComparison.Ordinal))
            {
                return Make("[+]", "new", "新作者", NewAuthorColor);
            }

            if (why.StartsWith("作者实体库：", StringComparison.Ordinal))
            {
                return Make("[ID]", "entity", "实体匹配", EntityColor);
            }

            if (why.StartsWith("作者别名库：", StringComparison.Ordinal) ||
                why.StartsWith("别名库统一为：", StringComparison.Ordinal))
            {
                return Make("[↔]", "alias", "别名匹配", AliasColor);
            }

            if (why.StartsWith("仅社团名", StringComparison.Ordinal) ||
                why.StartsWith("社团名称段", StringComparison.Ordinal))
            {
                return Make("[◆]", "society", "社团匹配", SocietyColor);
            }

            if (why.StartsWith("仅作者名", StringComparison.Ordinal))
            {
                return Make("[●]", "author", "作者匹配", AuthorColor);
            }

            if (String.Equals(why, "作者核心名称完全一致", StringComparison.Ordinal) ||
                String.Equals(why, "作者名标准化后完全一致", StringComparison.Ordinal) ||
                Contains(why, "均指向同一作者目录"))
            {
                return Make("[✓]", "direct", "直接匹配", DirectColor);
            }

            if (Contains(why, "标准化匹配"))
            {
                return Make("[≈]", "normalized", "标准化", NormalizeColor);
            }

            return Make("", "", "", Color.DimGray);
        }

        public static List<RecognitionLegendItem> GetLegendItems()
        {
            return new List<RecognitionLegendItem>
            {
                Legend("[✓]", "direct", "直接匹配", DirectColor),
                Legend("[≈]", "normalized", "标准化", NormalizeColor),
                Legend("[↔]", "alias", "别名匹配", AliasColor),
                Legend("[ID]", "entity", "实体匹配", EntityColor),
                Legend("[◆]", "society", "社团匹配", SocietyColor),
                Legend("[●]", "author", "作者匹配", AuthorColor),
                Legend("[+]", "new", "新作者", NewAuthorColor),
                Legend("[+]", "new-reuse", "新作者复用", NewReuseColor),
                Legend("[!]", "ambiguous", "歧义", AmbiguousColor),
                Legend("[★]", "manual", "手动指定", ManualColor),
                Legend("[×]", "unrecognized", "未识别", ErrorColor)
            };
        }

        private static RecognitionVisual Make(string mark, string code, string name, Color color)
        {
            RecognitionVisual result = new RecognitionVisual();
            result.Mark = mark;
            result.Code = code;
            result.Name = name;
            result.Color = color;
            return result;
        }

        private static RecognitionLegendItem Legend(string mark, string code, string name, Color color)
        {
            RecognitionLegendItem result = new RecognitionLegendItem();
            result.Mark = mark;
            result.Code = code;
            result.Name = name;
            result.Color = color;
            return result;
        }

        private static bool Contains(string text, string value)
        {
            return text != null && value != null &&
                text.IndexOf(value, StringComparison.Ordinal) >= 0;
        }
    }
}
