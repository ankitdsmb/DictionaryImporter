using Dapper;
using DictionaryImporter.Core.Rewrite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Common
{
    public static class SqlRepositoryHelper
    {
        public const string DefaultSourceCode = "UNKNOWN";
        public const string DefaultProvider = "RuleBased";
        public const string DefaultModel = "DictionaryRewriteV1";
        public const string DefaultPromotedBy = "SYSTEM";

        public static readonly DateTime SqlMinDateUtc = new(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static string NormalizeSourceCode(string? sourceCode)
        {
            sourceCode = NormalizeString(sourceCode, DefaultSourceCode);
            return Truncate(sourceCode, 50);
        }

        public static string NormalizeString(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        public static string? NormalizeNullableString(string? value, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var t = value.Trim();

            if (maxLen > 0 && t.Length > maxLen)
                t = t.Substring(0, maxLen).Trim();

            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static string Truncate(string? value, int maxLen)
        {
            var t = (value ?? string.Empty).Trim();
            if (maxLen <= 0) return t;
            return t.Length > maxLen ? t.Substring(0, maxLen) : t;
        }

        public static string? SafeTruncateOrNull(string? text, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var t = text.Trim();
            if (maxLen <= 0)
                return t;

            return t.Length <= maxLen ? t : t.Substring(0, maxLen).Trim();
        }

        public static string SafeTruncateOrEmpty(string? text, int maxLen)
        {
            var t = SafeTruncateOrNull(text, maxLen);
            return t ?? string.Empty;
        }

        public static DateTime EnsureUtc(DateTime dt)
        {
            return dt.Kind == DateTimeKind.Utc
                ? dt
                : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        public static DateTime FixSqlMinDateUtc(DateTime dt, DateTime fallbackUtc)
        {
            var utc = EnsureUtc(dt);
            return utc < SqlMinDateUtc ? EnsureUtc(fallbackUtc) : utc;
        }

        public static string NormalizeModeCode(string? mode)
        {
            mode = NormalizeString(mode, string.Empty);

            if (string.IsNullOrWhiteSpace(mode))
                return "English";

            if (IsValidModeCode(mode))
                return mode;

            return "English";
        }

        public static bool IsValidModeCode(string modeCode)
        {
            return modeCode.Equals("Academic", StringComparison.Ordinal)
                   || modeCode.Equals("Casual", StringComparison.Ordinal)
                   || modeCode.Equals("Educational", StringComparison.Ordinal)
                   || modeCode.Equals("Email", StringComparison.Ordinal)
                   || modeCode.Equals("English", StringComparison.Ordinal)
                   || modeCode.Equals("Formal", StringComparison.Ordinal)
                   || modeCode.Equals("GrammarFix", StringComparison.Ordinal)
                   || modeCode.Equals("Legal", StringComparison.Ordinal)
                   || modeCode.Equals("Medical", StringComparison.Ordinal)
                   || modeCode.Equals("Neutral", StringComparison.Ordinal)
                   || modeCode.Equals("Professional", StringComparison.Ordinal)
                   || modeCode.Equals("Simplify", StringComparison.Ordinal)
                   || modeCode.Equals("Technical", StringComparison.Ordinal);
        }

        public static string BuildPromotionNotes(string promotedBy, string sourceCode)
        {
            promotedBy = NormalizeString(promotedBy, DefaultPromotedBy);
            sourceCode = NormalizeSourceCode(sourceCode);

            var notes = $"PROMOTED_BY={promotedBy};SRC={sourceCode};UTC={DateTime.UtcNow:yyyy-MM-dd}";
            if (notes.Length > 200)
                notes = notes.Substring(0, 200);

            return notes;
        }

        public static int ComputePriority(int suggestedCount, decimal avgConfidence)
        {
            if (suggestedCount <= 0) suggestedCount = 1;
            if (avgConfidence < 0) avgConfidence = 0;
            if (avgConfidence > 1) avgConfidence = 1;

            var boost = 0;

            if (suggestedCount >= 50) boost += 30;
            else if (suggestedCount >= 10) boost += 20;
            else if (suggestedCount >= 3) boost += 10;

            if (avgConfidence >= 0.9m) boost += 30;
            else if (avgConfidence >= 0.75m) boost += 20;
            else if (avgConfidence >= 0.6m) boost += 10;

            var basePriority = 500;
            var priority = basePriority - boost;

            if (priority < 50) priority = 50;
            if (priority > 1000) priority = 1000;

            return priority;
        }

        // NEW METHOD (added)
        public static decimal NormalizeConfidence01(decimal confidence)
        {
            if (confidence < 0) return 0;
            if (confidence > 1) return 1;
            return confidence;
        }

        public static long[] NormalizeDistinctIds(IEnumerable<long>? ids)
        {
            if (ids is null)
                return Array.Empty<long>();

            return ids
                .Where(x => x > 0)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
        }

        public static object ToBigIntIdListTvp(IEnumerable<long> ids)
        {
            var dt = new DataTable();
            dt.Columns.Add("Id", typeof(long));

            foreach (var id in ids)
            {
                if (id > 0)
                    dt.Rows.Add(id);
            }

            return dt.AsTableValuedParameter("dbo.BigIntIdList");
        }

        public static string? NormalizeLocaleCodeOrNull(string? localeCode)
        {
            if (string.IsNullOrWhiteSpace(localeCode))
                return null;

            localeCode = Helper.NormalizeLocaleCode(localeCode);
            return string.IsNullOrWhiteSpace(localeCode) ? null : localeCode;
        }

        public static string? NormalizeIpaOrNull(string? ipa)
        {
            ipa = Helper.NormalizeIpa(ipa);
            return string.IsNullOrWhiteSpace(ipa) ? null : ipa;
        }

        public static string NormalizeAliasOrEmpty(string? alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return string.Empty;

            var t = alias.Trim();
            t = Regex.Replace(t, @"\s+", " ").Trim();

            if (t.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (t.Length > 150)
                t = t.Substring(0, 150).Trim();

            return t;
        }

        public static string NormalizeCrossReferenceTargetOrEmpty(string? targetWord)
        {
            if (string.IsNullOrWhiteSpace(targetWord))
                return string.Empty;

            var t = targetWord.Trim();

            t = t.Replace("[[", "").Replace("]]", "");
            t = t.Replace("{{", "").Replace("}}", "");
            t = t.Replace("|", " ");

            t = Regex.Replace(t, @"\s+", " ").Trim();

            t = t.Trim('\"', '\'', '“', '”', '‘', '’', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}');

            if (!Regex.IsMatch(t, @"[A-Za-z]"))
                return string.Empty;

            if (t.Length > 80)
                t = t.Substring(0, 80).Trim();

            return t.ToLowerInvariant();
        }

        public static string NormalizeCrossReferenceTypeOrEmpty(string? referenceType)
        {
            if (string.IsNullOrWhiteSpace(referenceType))
                return string.Empty;

            var t = referenceType.Trim();
            t = Regex.Replace(t, @"\s+", " ").Trim();

            if (t.Length > 50)
                t = t.Substring(0, 50).Trim();

            return t.ToLowerInvariant();
        }

        public static string NormalizeEtymologyTextOrEmpty(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var t = text.Trim();
            t = Regex.Replace(t, @"\s+", " ").Trim();

            if (t.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (t.Length < 3)
                return string.Empty;

            if (t.Length > 4000)
                t = t.Substring(0, 4000).Trim();

            return t;
        }

        public static bool IsPlaceholderExample(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var t = text.Trim();

            return t.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase)
                || t.Equals("[BILINGUAL_EXAMPLE]", StringComparison.OrdinalIgnoreCase)
                || t.Equals("NON_ENGLISH", StringComparison.OrdinalIgnoreCase)
                || t.Equals("BILINGUAL_EXAMPLE", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ContainsBlockedExamplePlaceholder(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.Contains("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("[BILINGUAL_EXAMPLE]", StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeExampleForDedupeOrEmpty(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            var t = example.Trim();
            t = Regex.Replace(t, @"\s+", " ").Trim();

            if (t.Length > 800)
                t = t.Substring(0, 800).Trim();

            return t;
        }

        public static async Task<long?> StoreNonEnglishTextAsync(
            ISqlStoredProcedureExecutor sp,
            string originalText,
            string sourceCode,
            string fieldType,
            CancellationToken ct,
            int timeoutSeconds = 30)
        {
            if (sp is null)
                return null;

            originalText = (originalText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(originalText))
                return null;

            sourceCode = NormalizeSourceCode(sourceCode);
            fieldType = NormalizeString(fieldType, "Unknown");

            if (fieldType.Length > 50)
                fieldType = fieldType.Substring(0, 50);

            var languageCode = Helper.LanguageDetector.DetectLanguageCode(originalText);
            if (string.IsNullOrWhiteSpace(languageCode))
                languageCode = "und";

            if (languageCode.Length > 32)
                languageCode = languageCode.Substring(0, 32);

            try
            {
                return await sp.ExecuteScalarAsync<long?>(
                    "sp_DictionaryNonEnglishText_Insert",
                    new
                    {
                        OriginalText = originalText,
                        DetectedLanguage = languageCode,
                        CharacterCount = originalText.Length,
                        SourceCode = sourceCode,
                        FieldType = fieldType
                    },
                    ct,
                    timeoutSeconds: timeoutSeconds);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        public static string NormalizePosOrEmpty(string? pos)
        {
            if (string.IsNullOrWhiteSpace(pos))
                return string.Empty;

            return pos.Trim().ToLowerInvariant();
        }

        public static int NormalizeConfidence(int confidence)
        {
            if (confidence < 0) return 0;
            if (confidence > 100) return 100;
            return confidence;
        }

        public static int NormalizeAiConfidenceOrDefault(int confidence, int defaultValue = 80)
        {
            if (defaultValue < 0) defaultValue = 0;
            if (defaultValue > 100) defaultValue = 100;

            if (confidence <= 0) return defaultValue;
            if (confidence > 100) return 100;
            return confidence;
        }
        public static RewriteMapRule? NormalizeRewriteRuleOrNull(RewriteMapRule? r)
        {
            if (r is null)
                return null;

            r.FromText = (r.FromText ?? string.Empty).Trim();
            r.ToText = (r.ToText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(r.FromText))
                return null;

            // deterministic safe defaults
            if (r.Priority <= 0)
                r.Priority = 100;

            return r;
        }
        public static async Task SafeExecuteAsync(
            Func<CancellationToken, Task> action,
            CancellationToken ct)
        {
            if (action is null)
                return;

            try
            {
                await action(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // never throw
            }
        }
    }
}
