using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Parsing;

namespace DictionaryImporter.Sources.Gutenberg
{
    public static class GutenbergParsingHelper
    {
        #region Regex (Compiled)

        private const RegexOptions RxC = RegexOptions.Compiled;
        private const RegexOptions RxCI = RegexOptions.Compiled | RegexOptions.IgnoreCase;
        private const RegexOptions RxCM = RegexOptions.Compiled | RegexOptions.Multiline;
        private const RegexOptions RxCIM = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline;

        private static readonly Regex RxHeadwordSimple = new(@"^([A-Za-z\-']+)(?:\s+\d+)?$", RxC);
        private static readonly Regex RxHeadwordWithPron = new(@"^([A-Za-z\-']+)\s+\([^)]+\)", RxC);

        private static readonly Regex RxSenseNumberLine = new(@"^(?<num>\d+)\.\s*(?<content>.+)$", RxC);

        private static readonly Regex RxQuotedText = new(@"[""'][^""']*?[""']", RxC);
        private static readonly Regex RxEgExample = new(@"\b[Ee]\.?g\.?(?:\s*\.|,).*?(?:;|\.|$)", RxC);
        private static readonly Regex RxAsExample = new(@"\b[Aa]s(?:,|:).*?(?:;|\.|$)", RxC);
        private static readonly Regex RxSeeInBrackets = new(@"\[[^\]]*[Ss]ee[^\]]*\]", RxC);
        private static readonly Regex RxNoteSection = new(@"\b[Nn]ote:\s+.*?(?:\.|$)", RxC);
        private static readonly Regex RxFormerly = new(@"\b[Ff]ormerly\b.*?(?:\.|$)", RxC);
        private static readonly Regex RxFormally = new(@"\b[Ff]ormally\b.*?(?:\.|$)", RxC);

        private static readonly Regex RxSpaces = new(@"\s+", RxC);
        private static readonly Regex RxLeadingPeriods = new(@"^\.+", RxC);

        private static readonly Regex RxNumberOnlyLine = new(@"^\d+\.\s*$", RxC);

        private static readonly Regex RxPronNamed = new(@"named\s+(?<pron>[a-züäö]+)\s+(?:in the|in other)", RxCI);
        private static readonly Regex RxPronounced = new(@"pronounced\s+(?<pron>[^\.\n]+)", RxCI);
        private static readonly Regex RxIpaSlashes = new(@"[\/\\]([^\/\\]+)[\/\\]", RxC);

        private static readonly Regex RxParenDomain = new(@"\((?<domain>[A-Za-z]+)\.\)", RxC);

        private static readonly Regex RxSeeRef = new(@"\b[Ss]ee\s+(?:also\s+)?(?<target>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\b", RxC);
        private static readonly Regex RxAbbrevFor = new(@"\b[A-Z]\.?\s+for\s+(?<target>[^\.\n,]+)", RxC);
        private static readonly Regex RxSynRef = new(@"\b[Ss]yn\.\s+(?<target>[^\.\n;]+)", RxC);

        private static readonly Regex RxVariantMarkers = new(@"(?:also written|also spelled|variant of|var\. of|abbrev\. of)\s+(?<variant>[^\.\n,;]+)", RxCI);

        private static readonly Regex RxUsageLabelPattern1 = new(@"\b(archaic|obsolete|rare|poetic|dialect|regional|colloquial|slang|vulgar|offensive|humorous|ironic|euphemistic)\b", RxCI);
        private static readonly Regex RxUsageLabelPattern2 = new(@"\b(old|ancient|medieval|middle|early|late)\s+(?:form|spelling|usage)\b", RxCI);
        private static readonly Regex RxUsageLabelPattern3 = new(@"\b(no longer|formerly|once|previously|historically)\s+(?:used|common)\b", RxCI);

        private static readonly Regex RxSynonyms = new(@"[Ss]yn\.\s+(?<synonyms>[^\.\n;]+)", RxC);
        private static readonly Regex RxSameAs = new(@"[Ss]ame\s+as\s+(?<synonym>[A-Z][a-z]+)", RxC);

        private static readonly Regex RxExampleQuotes = new(@"[""']([^""']+?)[""']", RxC);
        private static readonly Regex RxExampleAs = new(@"\b[Aa]s(?:,|:)\s*(.+?)(?:;|\.|$)", RxC | RegexOptions.Singleline);

        // Cleaning definition text
        private static readonly Regex RxCleanNoteInline = new(@"\s*Note:\s+[^\.]*\.?", RxCI);
        private static readonly Regex RxCleanNumberedPrefixesAll = new(@"^\d+\.\s*", RxCM);
        private static readonly Regex RxCleanDefnMarkersAll = new(@"^Defn:\s*", RxCIM);
        private static readonly Regex RxCleanDefinitionMarkersAll = new(@"^Definition:\s*", RxCIM);
        private static readonly Regex RxCleanEtymBrackets = new(@"\bEtym:\s*\[[^\]]+\]\s*\.?", RxCI);
        private static readonly Regex RxCleanSeeGeneric = new(@"\b[Ss]ee\s+[A-Za-z\s]+\b", RxC);

        private static readonly Regex RxCleanSynGeneric = new(@"\b[Ss]yn\.\s+[^\.]+", RxC);
        private static readonly Regex RxCleanArtifactsAnWhich = new(@"\banwhich\b", RxCI);
        private static readonly Regex RxCleanArtifactsDupScientific = new(@"\b([A-Z][a-z]+)\s+\1\s+([a-z][a-z]+)\)", RxCI);
        private static readonly Regex RxCleanArtifactsDoubleDefn = new(@"Defn:\s+Defn:", RxCI);
        private static readonly Regex RxCleanArtifactsMissingSpaceParen = new(@"([a-z])\(([A-Z])", RxC);

        // POS patterns
        private static readonly Regex RxPosNounWord = new(@"\snoun\b", RxC);
        private static readonly Regex RxPosVerbWord = new(@"\bverb\b", RxC);
        private static readonly Regex RxPosAdjectiveWord = new(@"\badjective\b", RxC);
        private static readonly Regex RxPosAdverbWord = new(@"\badverb\b", RxC);
        private static readonly Regex RxPosPrepositionWord = new(@"\bpreposition\b", RxC);
        private static readonly Regex RxPosPronounWord = new(@"\bpronoun\b", RxC);
        private static readonly Regex RxPosConjunctionWord = new(@"\bconjunction\b", RxC);
        private static readonly Regex RxPosInterjectionWord = new(@"\binterjection\b", RxC);
        private static readonly Regex RxPosArticleWord = new(@"\barticle\b", RxC);
        private static readonly Regex RxPosDeterminerWord = new(@"\bdeterminer\b", RxC);

        #endregion

        #region Constants

        private static readonly string[] NonEnglishPatterns =
        {
            @"[\u4e00-\u9fff]", // Chinese
            @"[\u0400-\u04FF]", // Cyrillic
            @"[\u0600-\u06FF]", // Arabic
            @"[\u0900-\u097F]", // Devanagari
            @"[\u0E00-\u0E7F]", // Thai
            @"[\uAC00-\uD7AF]", // Hangul
            @"[\u3040-\u309F]", // Hiragana
            @"[\u30A0-\u30FF]", // Katakana
        };

        private static readonly string[] SpecialCategoryTerms =
        {
            "Mus.", "Music", "Arch.", "Architecture", "Bot.", "Botany",
            "Zoöl.", "Zoology", "Her.", "Heraldry", "Law", "Geom.",
            "Math.", "Anat.", "Physiol.", "Chem.", "Mech.", "Naut."
        };

        #endregion

        #region Core Parsing Methods

        public static GutenbergParsedData ParseGutenbergEntry(string definition)
        {
            var data = new GutenbergParsedData();

            if (string.IsNullOrWhiteSpace(definition))
                return data;

            // Split into lines for processing
            var lines = definition.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            if (lines.Count == 0)
                return data;

            // Extract headword from first line if it looks like a word
            data.Headword = ExtractHeadword(lines.FirstOrDefault() ?? "");

            // If headword is invalid, clear it
            if (!IsValidMeaningTitle(data.Headword))
            {
                data.Headword = string.Empty;
            }

            // Parse all definition blocks
            ParseDefinitionBlocks(lines, data);

            // Extract etymology if present
            data.Etymology = ExtractEtymology(definition);

            return data;
        }
        public static bool IsValidMeaningTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            var trimmed = title.Trim();

            // Reject numeric tokens like "2.", "1.", etc.
            if (Regex.IsMatch(trimmed, @"^\d+\.?$"))
                return false;

            // Reject stop words
            var stopWords = new HashSet<string> { "a", "an", "in", "to", "at", "by", "for", "of", "on", "up" };
            if (stopWords.Contains(trimmed.ToLowerInvariant()))
                return false;

            // Accept single letters only if they're legitimate headwords (A, I)
            if (trimmed.Length == 1)
            {
                var validSingleLetters = new HashSet<string> { "A", "I", "O", "a", "i", "o" };
                return validSingleLetters.Contains(trimmed);
            }

            // Minimum length 2 for multi-character words
            if (trimmed.Length < 2)
                return false;

            // Must contain at least one letter
            if (!trimmed.Any(char.IsLetter))
                return false;

            return true;
        }


        public static string ExtractPartOfSpeech(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var lowerDef = definition.ToLowerInvariant();

            // determiner context
            if ((lowerDef.Contains("placed before") && lowerDef.Contains("noun")) ||
                (lowerDef.Contains("used before") && lowerDef.Contains("noun")) ||
                (lowerDef.Contains("precedes") && lowerDef.Contains("noun")) ||
                lowerDef.Contains("indefinite article") ||
                lowerDef.Contains("definite article") ||
                lowerDef.Contains("article used"))
            {
                return "determiner";
            }

            if (lowerDef.Contains("[mass noun]"))
                return "noun";

            if (lowerDef.Contains("used with") && lowerDef.Contains("verbal substantives"))
                return "preposition";

            if (lowerDef.Contains("connects") || lowerDef.Contains("joining"))
                return "conjunction";

            if (lowerDef.Contains("exclamatory") || lowerDef.Contains("expresses emotion"))
                return "interjection";

            // abbreviations quick scan
            if (lowerDef.Contains(" adj.") || lowerDef.StartsWith("adj."))
                return "adjective";
            if (lowerDef.Contains(" n.") || lowerDef.StartsWith("n."))
                return "noun";
            if (lowerDef.Contains(" v.") || lowerDef.StartsWith("v."))
                return "verb";
            if (lowerDef.Contains(" adv.") || lowerDef.StartsWith("adv."))
                return "adverb";
            if (lowerDef.Contains(" prep.") || lowerDef.StartsWith("prep."))
                return "preposition";
            if (lowerDef.Contains(" pron.") || lowerDef.StartsWith("pron."))
                return "pronoun";
            if (lowerDef.Contains(" conj.") || lowerDef.StartsWith("conj."))
                return "conjunction";
            if (lowerDef.Contains(" interj.") || lowerDef.StartsWith("interj."))
                return "interjection";
            if (lowerDef.Contains(" art.") || lowerDef.StartsWith("art."))
                return "article";
            if (lowerDef.Contains(" det.") || lowerDef.StartsWith("det."))
                return "determiner";

            // full POS words
            if (RxPosNounWord.IsMatch(lowerDef)) return "noun";
            if (RxPosVerbWord.IsMatch(lowerDef)) return "verb";
            if (RxPosAdjectiveWord.IsMatch(lowerDef)) return "adjective";
            if (RxPosAdverbWord.IsMatch(lowerDef)) return "adverb";
            if (RxPosPrepositionWord.IsMatch(lowerDef)) return "preposition";
            if (RxPosPronounWord.IsMatch(lowerDef)) return "pronoun";
            if (RxPosConjunctionWord.IsMatch(lowerDef)) return "conjunction";
            if (RxPosInterjectionWord.IsMatch(lowerDef)) return "interjection";
            if (RxPosArticleWord.IsMatch(lowerDef)) return "article";
            if (RxPosDeterminerWord.IsMatch(lowerDef)) return "determiner";

            return string.Empty;
        }

        private static string ExtractHeadword(string firstLine)
        {
            if (string.IsNullOrWhiteSpace(firstLine))
                return string.Empty;

            // Pattern 1: Single letter headword
            if (firstLine.Length == 1 && char.IsLetter(firstLine[0]))
                return firstLine.ToUpperInvariant();

            // Pattern 2: Word with possible superscript number
            var match = Regex.Match(firstLine, @"^([A-Za-z\-']+)(?:\s+\d+)?$");
            if (match.Success)
            {
                var headword = match.Groups[1].Value.Trim();
                // Convert to proper case: first letter uppercase, rest lowercase
                if (headword.Length > 0)
                {
                    return char.ToUpperInvariant(headword[0]) +
                           headword.Substring(1).ToLowerInvariant();
                }
            }

            // Pattern 3: Word followed by parenthetical pronunciation
            match = Regex.Match(firstLine, @"^([A-Za-z\-']+)\s+\([^)]+\)");
            if (match.Success)
            {
                var headword = match.Groups[1].Value.Trim();
                if (headword.Length > 0)
                {
                    return char.ToUpperInvariant(headword[0]) +
                           headword.Substring(1).ToLowerInvariant();
                }
            }

            // Fallback: Take first word and clean it
            var firstWord = firstLine.Split(' ')[0].Trim();
            return CleanWordForTitle(firstWord);
        }
        private static string CleanWordForTitle(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            // Remove rating symbols
            var cleaned = Regex.Replace(word, @"[●○◇□■▲►▼◄◊◦¤∙•▪▫◘◙☺☻♀♂♠♣♥♦♪♫♯]", "").Trim();

            // Remove trailing punctuation
            cleaned = Regex.Replace(cleaned, @"[^\w\-]+$", "");

            // Return in proper case (first letter uppercase, rest lowercase)
            if (cleaned.Length > 0)
            {
                return char.ToUpperInvariant(cleaned[0]) +
                       cleaned.Substring(1).ToLowerInvariant();
            }

            return cleaned;

        }


        private static void ParseDefinitionBlocks(List<string> lines, GutenbergParsedData data)
        {
            var rawDefinitions = new List<RawDefinitionBlock>();
            var currentBlock = new RawDefinitionBlock();
            var inDefinition = false;
            var senseNumber = 1;
            var lastSenseNumber = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var trimmedLine = line.Trim();

                // SKIP ETYMOLOGY LINES - CRITICAL FIX
                if (trimmedLine.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(trimmedLine, @"^\d+\.\s+Etym:", RegexOptions.IgnoreCase))
                {
                    // If we were in a definition, finalize it before skipping
                    if (inDefinition && !string.IsNullOrWhiteSpace(currentBlock.DefinitionText))
                    {
                        currentBlock.DefinitionText = CleanDefinitionText(currentBlock.DefinitionText);
                        if (!string.IsNullOrWhiteSpace(currentBlock.DefinitionText))
                        {
                            rawDefinitions.Add(currentBlock);
                        }
                        currentBlock = new RawDefinitionBlock();
                    }
                    inDefinition = false;
                    continue;
                }

                // Check for sense number markers
                var senseMatch = Regex.Match(trimmedLine, @"^(?<num>\d+)\.\s+(?<content>.+)$");
                if (senseMatch.Success)
                {
                    if (inDefinition && !string.IsNullOrWhiteSpace(currentBlock.DefinitionText))
                    {
                        currentBlock.DefinitionText = CleanDefinitionText(currentBlock.DefinitionText);
                        if (!string.IsNullOrWhiteSpace(currentBlock.DefinitionText))
                        {
                            rawDefinitions.Add(currentBlock);
                        }
                        currentBlock = new RawDefinitionBlock();
                    }

                    var num = int.Parse(senseMatch.Groups["num"].Value);
                    var content = senseMatch.Groups["content"].Value.Trim();

                    // Ensure sense numbers are sequential
                    if (num <= lastSenseNumber)
                    {
                        num = lastSenseNumber + 1;
                    }
                    lastSenseNumber = num;

                    currentBlock.SenseNumber = num;
                    currentBlock.DefinitionText = content;
                    inDefinition = true;
                    continue;
                }

                // Check for definition markers
                if (trimmedLine.StartsWith("Defn:", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("Definition:", StringComparison.OrdinalIgnoreCase))
                {
                    if (inDefinition && !string.IsNullOrWhiteSpace(currentBlock.DefinitionText))
                    {
                        currentBlock.DefinitionText = CleanDefinitionText(currentBlock.DefinitionText);
                        if (!string.IsNullOrWhiteSpace(currentBlock.DefinitionText))
                        {
                            rawDefinitions.Add(currentBlock);
                        }
                        currentBlock = new RawDefinitionBlock();
                    }

                    lastSenseNumber++;
                    currentBlock.SenseNumber = lastSenseNumber;
                    currentBlock.DefinitionText = trimmedLine.Substring(trimmedLine.IndexOf(':') + 1).Trim();
                    currentBlock.HasDefnMarker = true;
                    inDefinition = true;
                    continue;
                }

                // If we're in a definition, append continuation lines
                if (inDefinition)
                {
                    currentBlock.DefinitionText += " " + trimmedLine;
                }
                else if (IsDefinitionContent(trimmedLine))
                {
                    // Start new definition for content that looks like a definition
                    lastSenseNumber++;
                    currentBlock.SenseNumber = lastSenseNumber;
                    currentBlock.DefinitionText = trimmedLine;
                    inDefinition = true;
                }
            }

            // Add the last block if valid
            if (inDefinition && !string.IsNullOrWhiteSpace(currentBlock.DefinitionText))
            {
                currentBlock.DefinitionText = CleanDefinitionText(currentBlock.DefinitionText);
                if (!string.IsNullOrWhiteSpace(currentBlock.DefinitionText))
                {
                    rawDefinitions.Add(currentBlock);
                }
            }

            data.RawDefinitions = rawDefinitions;
        }
        private static string CleanDefinitionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var cleaned = text;

            // Remove "Defn:" markers
            cleaned = Regex.Replace(cleaned, @"^Defn:\s*", "", RegexOptions.IgnoreCase);

            // Remove "Definition:" markers
            cleaned = Regex.Replace(cleaned, @"^Definition:\s*", "", RegexOptions.IgnoreCase);

            // Remove numbered prefixes
            cleaned = Regex.Replace(cleaned, @"^\d+\.\s*", "");

            // Remove "Note:" sections
            cleaned = Regex.Replace(cleaned, @"\s*Note:\s+.*?(?=\.|$)", "", RegexOptions.IgnoreCase);

            // Remove "Syn." sections
            cleaned = Regex.Replace(cleaned, @"\s*Syn\.\s+.*?(?=\.|$)", "", RegexOptions.IgnoreCase);

            // Remove "See" references
            cleaned = Regex.Replace(cleaned, @"\s*See\s+[^\.]+\b\.?", "", RegexOptions.IgnoreCase);

            // Fix double punctuation (". ." -> ".")
            cleaned = Regex.Replace(cleaned, @"\.\s*\.", ".");

            // Fix missing space after period before capital letter
            cleaned = Regex.Replace(cleaned, @"([a-z])\.([A-Z])", "$1. $2");

            // Fix "doing.He" -> "doing. He"
            cleaned = Regex.Replace(cleaned, @"([a-z])([A-Z])", "$1 $2");

            // Clean up whitespace
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            // Ensure it ends with a period if it has content
            if (!string.IsNullOrWhiteSpace(cleaned) &&
                !cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
            {
                cleaned += ".";
            }

            return cleaned;
        }



        public static string ExtractParsedPartOfSpeech(GutenbergParsedData data, RawDefinitionBlock rawBlock, DictionaryEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(data.PartOfSpeech))
                return data.PartOfSpeech;

            if (rawBlock != null && !string.IsNullOrWhiteSpace(rawBlock.DefinitionText))
            {
                var posFromText = ExtractPartOfSpeech(rawBlock.DefinitionText);
                if (!string.IsNullOrWhiteSpace(posFromText))
                    return posFromText;
            }

            if (!string.IsNullOrWhiteSpace(entry.Definition))
            {
                var posFromDefinition = ExtractPartOfSpeech(entry.Definition);
                if (!string.IsNullOrWhiteSpace(posFromDefinition))
                    return posFromDefinition;
            }

            return ExtractFallbackPartOfSpeech(entry);
        }

        public static string BuildFullDefinition(string cleanDefinition, GutenbergParsedData data, RawDefinitionBlock rawBlock)
        {
            var parts = new List<string>(6);

            var coreDefinition = ExtractCoreDefinitionText(cleanDefinition);
            if (!string.IsNullOrWhiteSpace(coreDefinition))
                parts.Add(coreDefinition);

            if (!string.IsNullOrWhiteSpace(data.Pronunciation))
                parts.Add($"【Pronunciation】{data.Pronunciation}");

            if (!string.IsNullOrWhiteSpace(data.Etymology))
                parts.Add($"【Etymology】{data.Etymology}");

            if (rawBlock != null && !string.IsNullOrWhiteSpace(rawBlock.Domain))
                parts.Add($"【Category】{rawBlock.Domain.ToLowerInvariant()}");
            else if (data.Categories.Count > 0)
                parts.Add($"【Categories】{string.Join(", ", data.Categories)}");

            if (data.UsageLabels.Count > 0)
                parts.Add($"【Usage】{string.Join(", ", data.UsageLabels)}");

            if (data.Variants.Count > 0)
                parts.Add($"【Variants】{string.Join(", ", data.Variants)}");

            return string.Join("\n", parts).Trim();
        }

        private static string ExtractCoreDefinitionText(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var text = CleanGutenbergDefinition(definition);
            text = RxLeadingPeriods.Replace(text, "").Trim();

            return text;
        }

        public static string GetMeaningTitle(GutenbergParsedData data, DictionaryEntry entry, string cleanDef)
        {
            if (!string.IsNullOrWhiteSpace(data.Headword))
                return data.Headword.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(entry.Word))
            {
                var cleanedWord = RemoveRatingSymbols(entry.Word);
                if (!string.IsNullOrWhiteSpace(cleanedWord))
                    return cleanedWord.ToLowerInvariant();
            }

            var firstWords = ExtractFirstWords(cleanDef, 3);
            return !string.IsNullOrWhiteSpace(firstWords) ? firstWords.ToLowerInvariant() : "unnamed sense";
        }

        public static int GetAdjustedSenseNumber(GutenbergParsedData data, int index)
        {
            var validCount = 0;

            for (int i = 0; i <= index; i++)
            {
                var cleanDef = data.CleanDefinitions[i];
                var rawBlock = data.RawDefinitions.Count > i ? data.RawDefinitions[i] : null;

                if (!ShouldSkipDefinition(cleanDef, rawBlock))
                    validCount++;
            }

            return validCount;
        }

        public static bool ShouldSkipDefinition(string cleanDef, RawDefinitionBlock rawBlock)
        {
            if (string.IsNullOrWhiteSpace(cleanDef))
                return true;

            if (cleanDef.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(cleanDef, @"\bEtym:\s*\[", RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (Regex.IsMatch(cleanDef, @"^\s*\d+\.?\s*$") ||
                (cleanDef.Length < 20 &&
                 (cleanDef.Contains("Note:") || cleanDef.Contains("Usage:") || cleanDef.Contains("Syn."))))
            {
                return true;
            }

            return false;
        }

        public static string ExtractFirstWords(string text, int wordCount)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= wordCount)
                return text;

            return string.Join(" ", words.Take(wordCount)) + "...";
        }

        public static string GetDomain(GutenbergParsedData data, RawDefinitionBlock rawBlock)
        {
            if (rawBlock != null && !string.IsNullOrWhiteSpace(rawBlock.Domain))
                return rawBlock.Domain.ToLowerInvariant();

            return data.Categories.FirstOrDefault() ?? string.Empty;
        }

        public static string GetUsageLabel(GutenbergParsedData data, RawDefinitionBlock rawBlock)
        {
            var labels = new List<string>(4);

            if (data.UsageLabels.Count > 0)
                labels.AddRange(data.UsageLabels.Take(2));

            if (rawBlock?.DefinitionText != null)
            {
                if (rawBlock.DefinitionText.Contains("archaic", StringComparison.OrdinalIgnoreCase))
                    labels.Add("archaic");
                if (rawBlock.DefinitionText.Contains("obsolete", StringComparison.OrdinalIgnoreCase))
                    labels.Add("obsolete");
                if (rawBlock.DefinitionText.Contains("rare", StringComparison.OrdinalIgnoreCase))
                    labels.Add("rare");
            }

            return labels.Count > 0 ? string.Join(", ", labels.Distinct()) : null;
        }

        public static ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
        {
            var cleanedDefinition = CleanGutenbergArtifacts(entry.Definition ?? string.Empty);
            var finalDefinition = CleanGutenbergDefinition(cleanedDefinition);
            var coreDefinition = ExtractCoreDefinitionText(finalDefinition);

            return new ParsedDefinition
            {
                MeaningTitle = GetFallbackMeaningTitle(entry, coreDefinition),
                Definition = coreDefinition,
                RawFragment = entry.Definition ?? string.Empty,
                SenseNumber = entry.SenseNumber,
                PartOfSpeech = ExtractFallbackPartOfSpeech(entry),
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }

        private static string GetFallbackMeaningTitle(DictionaryEntry entry, string definition)
        {
            if (!string.IsNullOrWhiteSpace(entry.Word))
            {
                var cleanedWord = RemoveRatingSymbols(entry.Word);
                if (!string.IsNullOrWhiteSpace(cleanedWord))
                    return cleanedWord.ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(definition))
            {
                var firstWords = ExtractFirstWords(definition, 2);
                if (!string.IsNullOrWhiteSpace(firstWords))
                    return firstWords.ToLowerInvariant();
            }

            return "unnamed sense";
        }

        public static string ExtractFallbackPartOfSpeech(DictionaryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Definition))
                return string.Empty;

            var word = entry.Word?.Trim()?.ToLowerInvariant() ?? "";
            var definition = entry.Definition.ToLowerInvariant();

            if (word.Length == 1 && char.IsLetter(word[0]))
            {
                if (word is "a" or "i")
                    return "determiner";
                if (word == "o")
                    return "interjection";
            }

            if (definition.Contains("indefinite article") ||
                definition.Contains("definite article") ||
                definition.Contains(" article ") ||
                definition.Contains("placed before nouns") ||
                definition.Contains("used before nouns"))
            {
                return "determiner";
            }

            if (definition.Contains("used with") &&
                definition.Contains("verbal substantives"))
            {
                return "preposition";
            }

            if (Regex.IsMatch(definition, @"\b(the|a|an)\s+[a-z]+\b", RegexOptions.IgnoreCase) &&
                definition.Length > 30)
            {
                return "noun";
            }

            if (definition.StartsWith("to ") ||
                Regex.IsMatch(definition, @"\bto\s+[a-z]+\s+[a-z]+", RegexOptions.IgnoreCase))
            {
                return "verb";
            }

            return "noun";
        }

        public static string ExtractCoreDefinition(string definitionText)
        {
            if (string.IsNullOrWhiteSpace(definitionText))
                return string.Empty;

            var text = definitionText;

            text = RxQuotedText.Replace(text, "");
            text = RxEgExample.Replace(text, "");
            text = RxAsExample.Replace(text, "");
            text = RxSeeInBrackets.Replace(text, "");
            text = RxNoteSection.Replace(text, "");
            text = RxFormerly.Replace(text, "");
            text = RxFormally.Replace(text, "");

            text = NormalizeSpace(text).TrimEnd(',', ';', ':');

            if (!string.IsNullOrEmpty(text) && !EndsWithSentencePunctuation(text))
                text += ".";

            return text;
        }

        private static bool IsDefinitionContent(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            // Lines that are NOT definition content
            if (line.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Syn.", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Note:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(line, @"^\d+\.\s*$"))
            {
                return false;
            }

            // Lines that ARE definition content
            return line.Length > 10 &&
                   char.IsLetter(line[0]) &&
                   !line.StartsWith("See ");
        }

        #endregion

        #region Etymology

        public static string ExtractEtymology(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var etymologies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in Regex.Matches(definition, @"Etym:\s*\[(?<etym>[^\]]+)\]", RegexOptions.IgnoreCase))
            {
                var etym = CleanEtymologyText(match.Groups["etym"].Value);
                if (!string.IsNullOrEmpty(etym))
                    etymologies.Add(etym);
            }

            foreach (Match match in Regex.Matches(definition, @"^From\s+(?<content>[^\n\.]+)",
                         RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var etym = CleanEtymologyText(match.Groups["content"].Value);
                if (!string.IsNullOrEmpty(etym))
                    etymologies.Add(etym);
            }

            foreach (Match match in Regex.Matches(definition,
                         @"\b(Latin|Greek|L\.|Gr\.|AS\.|OE\.|OF\.|F\.|LL\.)\s+([a-z][a-z\-']+)",
                         RegexOptions.IgnoreCase))
            {
                var lang = match.Groups[1].Value.TrimEnd('.');
                var word = match.Groups[2].Value.TrimEnd('.');
                etymologies.Add($"{lang}. {word}");
            }

            return string.Join("; ", etymologies);
        }

        private static string CleanEtymologyText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = text.Trim().TrimEnd('.', ',', ';', ':');
            return NormalizeSpace(text);
        }

        public static (string CleanedDefinition, string ExtractedEtymology) ExtractEtymologyFromDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return (definition, string.Empty);

            var etymology = new StringBuilder();
            var result = new StringBuilder();
            var lines = definition.Split('\n', StringSplitOptions.None);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase))
                {
                    var start = trimmed.IndexOf('[');
                    var end = trimmed.LastIndexOf(']');

                    if (start >= 0 && end > start)
                    {
                        var etymText = trimmed.Substring(start + 1, end - start - 1).Trim();
                        if (!string.IsNullOrEmpty(etymText))
                        {
                            if (etymology.Length > 0)
                                etymology.Append("; ");
                            etymology.Append(etymText);
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(trimmed))
                {
                    if (result.Length > 0)
                        result.Append('\n');
                    result.Append(trimmed);
                }
            }

            return (result.ToString(), etymology.ToString());
        }

        #endregion

        #region Pronunciation

        public static string ExtractPronunciation(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var pronMatch = RxPronNamed.Match(definition);
            if (pronMatch.Success)
                return pronMatch.Groups["pron"].Value.Trim();

            pronMatch = RxPronounced.Match(definition);
            if (pronMatch.Success)
                return pronMatch.Groups["pron"].Value.Trim();

            var ipaMatches = RxIpaSlashes.Matches(definition);
            if (ipaMatches.Count > 0)
                return ipaMatches[0].Groups[1].Value.Trim();

            return string.Empty;
        }

        #endregion

        #region Categories / CrossRefs / Variants / Usage / Synonyms / Examples

        public static IReadOnlyList<string> ExtractCategories(string definition)
        {
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(definition))
                return categories.ToList();

            var domainMatches = RxParenDomain.Matches(definition);
            foreach (Match match in domainMatches)
            {
                var domain = match.Groups["domain"].Value.Trim();
                if (domain.Length > 1)
                    categories.Add(domain.ToLowerInvariant());
            }

            foreach (var term in SpecialCategoryTerms)
            {
                if (definition.Contains(term))
                    categories.Add(term.Replace(".", "").ToLowerInvariant());
            }

            return categories.ToList();
        }

        public static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
        {
            var crossRefs = new List<CrossReference>();

            if (string.IsNullOrWhiteSpace(definition))
                return crossRefs;

            foreach (Match match in RxSeeRef.Matches(definition))
            {
                var target = match.Groups["target"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(target))
                {
                    crossRefs.Add(new CrossReference
                    {
                        TargetWord = target,
                        ReferenceType = "See"
                    });
                }
            }

            foreach (Match match in RxAbbrevFor.Matches(definition))
            {
                var target = match.Groups["target"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(target))
                {
                    crossRefs.Add(new CrossReference
                    {
                        TargetWord = target.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim(),
                        ReferenceType = "AbbreviationFor"
                    });
                }
            }

            foreach (Match match in RxSynRef.Matches(definition))
            {
                var targets = match.Groups["target"].Value.Split(',', ';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var target in targets)
                {
                    var cleanTarget = target.Trim();
                    if (!string.IsNullOrWhiteSpace(cleanTarget))
                    {
                        crossRefs.Add(new CrossReference
                        {
                            TargetWord = cleanTarget,
                            ReferenceType = "Synonym"
                        });
                    }
                }
            }

            return crossRefs;
        }

        public static IReadOnlyList<string> ExtractVariants(string definition)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(definition))
                return variants.ToList();

            foreach (Match match in RxVariantMarkers.Matches(definition))
            {
                var variantText = match.Groups["variant"].Value.Trim();
                var variantParts = variantText.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in variantParts)
                {
                    var cleanPart = part.Trim().Trim('"', '\'');
                    if (cleanPart.Length > 1 && char.IsLetter(cleanPart[0]))
                        variants.Add(cleanPart);
                }
            }

            return variants.ToList();
        }

        public static IReadOnlyList<string> ExtractUsageLabels(string definition)
        {
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(definition))
                return labels.ToList();

            foreach (Match match in RxUsageLabelPattern1.Matches(definition))
                labels.Add(match.Value.ToLowerInvariant());

            foreach (Match match in RxUsageLabelPattern2.Matches(definition))
                labels.Add(match.Value.ToLowerInvariant());

            foreach (Match match in RxUsageLabelPattern3.Matches(definition))
                labels.Add(match.Value.ToLowerInvariant());

            return labels.ToList();
        }

        /// <summary>
        /// Extract synonyms
        /// </summary>
        public static IReadOnlyList<string> ExtractSynonyms(string definition)
        {
            var synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(definition))
                return synonyms.ToList();

            // Pattern 1: Syn. markers (case insensitive)
            var synMatches = Regex.Matches(definition,
                @"[Ss]yn\.\s+(?<synonyms>[^\.\n;]+)");

            foreach (Match match in synMatches)
            {
                var synText = match.Groups["synonyms"].Value;
                // Split by commas, semicolons, or "and"
                var synList = Regex.Split(synText, @"\s*[,;]\s*|\s+and\s+");

                foreach (var syn in synList)
                {
                    var cleanSyn = syn.Trim().Trim('"', '\'', '.');
                    if (!string.IsNullOrWhiteSpace(cleanSyn) &&
                        cleanSyn.Length > 1 &&
                        !cleanSyn.Equals("etc", StringComparison.OrdinalIgnoreCase))
                    {
                        synonyms.Add(cleanSyn.ToLowerInvariant());
                    }
                }
            }

            // Pattern 2: "Same as" references
            var sameMatches = Regex.Matches(definition,
                @"[Ss]ame\s+as\s+(?<synonym>[A-Za-z][a-z]+(?:-[A-Za-z][a-z]+)*)");

            foreach (Match match in sameMatches)
            {
                var synonym = match.Groups["synonym"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(synonym))
                {
                    synonyms.Add(synonym.ToLowerInvariant());
                }
            }

            return synonyms.ToList();
        }

        public static IReadOnlyList<string> ExtractExamples(string definition)
        {
            var examples = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return examples;

            foreach (Match match in RxExampleQuotes.Matches(definition))
            {
                var example = match.Groups[1].Value.Trim();
                if (example.Length > 5 &&
                    !example.Contains("Defn", StringComparison.OrdinalIgnoreCase) &&
                    !example.Contains("Etym", StringComparison.OrdinalIgnoreCase) &&
                    !example.Contains("Syn.", StringComparison.OrdinalIgnoreCase))
                {
                    examples.Add(CleanExampleText(example));
                }
            }

            foreach (Match match in RxExampleAs.Matches(definition))
            {
                var exampleText = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(exampleText))
                {
                    examples.AddRange(exampleText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(static e => e.Trim())
                        .Where(static e => e.Length > 3));
                }
            }

            return examples.Distinct().ToList();
        }

        #endregion

        #region Cleaning

        public static IReadOnlyList<string> BuildCleanDefinitions(IReadOnlyList<RawDefinitionBlock> rawBlocks)
        {
            var cleanDefs = new List<string>();

            if (rawBlocks == null || rawBlocks.Count == 0)
                return cleanDefs;

            foreach (var block in rawBlocks)
            {
                if (string.IsNullOrWhiteSpace(block.DefinitionText))
                    continue;

                var cleanDef = CleanDefinitionText(block.DefinitionText);
                if (!string.IsNullOrWhiteSpace(cleanDef))
                {
                    cleanDefs.Add(cleanDef);
                }
            }

            return cleanDefs;
        }
        public static string BuildCleanDefinition(
            string cleanDef,
            GutenbergParsingHelper.GutenbergParsedData parsedData,
            GutenbergParsingHelper.RawDefinitionBlock rawBlock)
        {
            // If you already have BuildFullDefinition in helper, call that.
            // This method exists only to match your code.
            return GutenbergParsingHelper.BuildFullDefinition(cleanDef, parsedData, rawBlock);
        }


        public static string CleanGutenbergDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return ".";

            var cleaned = definition;

            cleaned = RxCleanNoteInline.Replace(cleaned, "");
            cleaned = RxCleanNumberedPrefixesAll.Replace(cleaned, "");
            cleaned = RxCleanDefnMarkersAll.Replace(cleaned, "");
            cleaned = RxCleanDefinitionMarkersAll.Replace(cleaned, "");
            cleaned = RxCleanSeeGeneric.Replace(cleaned, "");
            cleaned = RxCleanSynGeneric.Replace(cleaned, "");
            cleaned = RxCleanEtymBrackets.Replace(cleaned, "");

            cleaned = NormalizeSpace(cleaned).TrimEnd(',', ';', ':');

            if (string.IsNullOrWhiteSpace(cleaned))
                return ".";

            if (!EndsWithSentencePunctuation(cleaned))
                cleaned += ".";

            return cleaned;
        }

        public static string CleanGutenbergArtifacts(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var cleaned = text;

            cleaned = RxCleanArtifactsAnWhich.Replace(cleaned, "an (which");
            cleaned = RxCleanArtifactsDupScientific.Replace(cleaned, "$1 $2)");
            cleaned = RxCleanArtifactsDoubleDefn.Replace(cleaned, "Defn:");
            cleaned = RxCleanEtymBrackets.Replace(cleaned, "");
            cleaned = RxCleanArtifactsMissingSpaceParen.Replace(cleaned, "$1 ($2");

            return cleaned;
        }

        public static string CleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return example;

            var cleaned = example.Trim()
                .TrimStart('"', '\'', '`', ' ')
                .TrimEnd('"', '\'', '`', ' ');

            if (cleaned.Length > 0 && char.IsLower(cleaned[0]))
                cleaned = char.ToUpper(cleaned[0]) + cleaned[1..];

            if (!EndsWithSentencePunctuation(cleaned))
                cleaned += ".";

            return cleaned;
        }

        #endregion

        #region Language Detection

        public static bool ContainsNonEnglishText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var pattern in NonEnglishPatterns)
            {
                if (Regex.IsMatch(text, pattern))
                    return true;
            }

            return false;
        }

        public static (string PartOfSpeech, double Confidence) ExtractPartOfSpeechWithConfidence(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return (string.Empty, 0.0);

            var lowerDef = definition.ToLowerInvariant();

            var highConfidenceMarkers = new Dictionary<string, (string pos, double confidence)>
            {
                { @"\bn\.\b|\[mass noun\]", ("noun", 0.95) },
                { @"\bv\.\b|\bverb\b", ("verb", 0.95) },
                { @"\badj\.\b|\badjective\b", ("adjective", 0.95) },
                { @"\badv\.\b|\badverb\b", ("adverb", 0.95) },
                { @"\bprep\.\b|\bpreposition\b", ("preposition", 0.95) },
                { @"\bpron\.\b|\bpronoun\b", ("pronoun", 0.90) },
                { @"\bconj\.\b|\bconjunction\b", ("conjunction", 0.90) },
                { @"\binterj\.\b|\binterjection\b", ("interjection", 0.90) },
                { @"\bart\.\b|\barticle\b", ("article", 0.90) },
                { @"\bdet\.\b|\bdeterminer\b", ("determiner", 0.90) }
            };

            foreach (var marker in highConfidenceMarkers)
            {
                if (Regex.IsMatch(lowerDef, marker.Key))
                    return (marker.Value.pos, marker.Value.confidence);
            }

            if (lowerDef.Contains("indefinite article") || lowerDef.Contains("placed before nouns"))
                return ("determiner", 0.80);

            if (lowerDef.Contains("used with") && lowerDef.Contains("verbal substantives"))
                return ("preposition", 0.75);

            if (lowerDef.Contains("connects words") || lowerDef.Contains("joining clauses"))
                return ("conjunction", 0.75);

            if (Regex.IsMatch(lowerDef, @"\b(the|a|an)\s+[a-z]+\b") && lowerDef.Length > 30)
                return ("noun", 0.60);

            if (lowerDef.StartsWith("to ") || Regex.IsMatch(lowerDef, @"\bto\s+[a-z]+\s+[a-z]+"))
                return ("verb", 0.60);

            return ("unk", 0.10);
        }

        #endregion

        #region Small Utils (shared)

        private static bool EndsWithSentencePunctuation(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            var last = text[^1];
            return last is '.' or '!' or '?';
        }

        private static string NormalizeSpace(string text)
            => string.IsNullOrWhiteSpace(text) ? string.Empty : RxSpaces.Replace(text, " ").Trim();

        private static string RemoveRatingSymbols(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            // Same logic as before (strip rating symbols)
            return Regex.Replace(word, @"[●○◇□■▲►▼◄◊◦¤∙•▪▫◘◙☺☻♀♂♠♣♥♦♪♫♯]", "").Trim();
        }

        #endregion

        #region Data Structures

        public class RawDefinitionBlock
        {
            public int SenseNumber { get; set; } = 1;
            public string Domain { get; set; } = string.Empty;
            public string DefinitionText { get; set; } = string.Empty;
            public bool HasDefnMarker { get; set; }
        }

        public class GutenbergParsedData
        {
            public string Headword { get; set; } = string.Empty;
            public string PartOfSpeech { get; set; } = string.Empty;
            public string Etymology { get; set; } = string.Empty;
            public string Pronunciation { get; set; } = string.Empty;

            public IReadOnlyList<RawDefinitionBlock> RawDefinitions { get; set; } = new List<RawDefinitionBlock>();
            public IReadOnlyList<string> CleanDefinitions { get; set; } = new List<string>();
            public IReadOnlyList<string> Categories { get; set; } = new List<string>();
            public IReadOnlyList<CrossReference> CrossReferences { get; set; } = new List<CrossReference>();
            public IReadOnlyList<string> Variants { get; set; } = new List<string>();
            public IReadOnlyList<string> UsageLabels { get; set; } = new List<string>();
            public IReadOnlyList<string> Synonyms { get; set; } = new List<string>();
            public IReadOnlyList<string> Examples { get; set; } = new List<string>();

            public string PrimaryDomain => Categories?.FirstOrDefault() ?? string.Empty;
            public string PrimaryUsageLabel => UsageLabels?.FirstOrDefault() ?? string.Empty;
        }

        #endregion
    }
}
