using System;

namespace MangaAuthorSorter
{
    internal enum ScanModeKind
    {
        Global = 0,
        Exact = 1,
        NewAuthor = 2,
        Ambiguous = 3,
        Unrecognized = 4
    }

    internal static class ScanModeRules
    {
        public static string GetDisplayName(
            ScanModeKind mode)
        {
            switch (mode)
            {
                case ScanModeKind.Exact:
                    return "匹配模式";
                case ScanModeKind.NewAuthor:
                    return "新增模式";
                case ScanModeKind.Ambiguous:
                    return "歧义模式";
                case ScanModeKind.Unrecognized:
                    return "未识别模式";
                default:
                    return "全局模式";
            }
        }

        public static string GetShortDescription(
            ScanModeKind mode)
        {
            switch (mode)
            {
                case ScanModeKind.Exact:
                    return "已匹配已有作者";
                case ScanModeKind.NewAuthor:
                    return "新作者";
                case ScanModeKind.Ambiguous:
                    return "存在多个匹配结果";
                case ScanModeKind.Unrecognized:
                    return "无法识别作者";
                default:
                    return "全部文件";
            }
        }

        public static bool Matches(
            PlanItem item,
            ScanModeKind mode)
        {
            if (mode == ScanModeKind.Global)
                return true;

            RecognitionVisual visual =
                RecognitionVisualResolver.Resolve(
                    item);

            string mark =
                visual != null
                    ? visual.Mark
                    : "";

            switch (mode)
            {
                case ScanModeKind.Exact:
                    return
                        String.Equals(
                            mark,
                            "[✓]",
                            StringComparison.Ordinal) ||
                        String.Equals(
                            mark,
                            "[≈]",
                            StringComparison.Ordinal) ||
                        String.Equals(
                            mark,
                            "[↔]",
                            StringComparison.Ordinal) ||
                        String.Equals(
                            mark,
                            "[ID]",
                            StringComparison.Ordinal) ||
                        String.Equals(
                            mark,
                            "[◆]",
                            StringComparison.Ordinal) ||
                        String.Equals(
                            mark,
                            "[●]",
                            StringComparison.Ordinal);

                case ScanModeKind.NewAuthor:
                    return String.Equals(
                        mark,
                        "[+]",
                        StringComparison.Ordinal);

                case ScanModeKind.Ambiguous:
                    return String.Equals(
                        mark,
                        "[!]",
                        StringComparison.Ordinal);

                case ScanModeKind.Unrecognized:
                    return String.Equals(
                        mark,
                        "[×]",
                        StringComparison.Ordinal);
            }

            return false;
        }
    }
}
