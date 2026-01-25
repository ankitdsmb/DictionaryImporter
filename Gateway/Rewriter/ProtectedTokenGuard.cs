namespace DictionaryImporter.Gateway.Rewriter
{
    public static class ProtectedTokenGuard
    {
        private const string PlaceholderPrefix = "⟦PT";
        private const string PlaceholderSuffix = "⟧";
        private const int MaxTokens = 200;
        private const int MaxTokenLength = 80;

        private static readonly RegexOptions Opt =
            RegexOptions.Compiled | RegexOptions.CultureInvariant;

        // Priority order matters: longer/more specific first
        private static readonly Regex ProtectedTokenRegex = new Regex(
            string.Join("|", new[]
            {
                // 1. Programming Languages & Frameworks (most specific first)
                @"\b(?:C\#|F\#|\.NET|ASP\.NET|C\+\+|Node\.js|React\.js|Angular\.js|Vue\.js|TypeScript|JavaScript|Java|Python|Ruby|Rust|GoLang|Kotlin|Swift|Scala|Perl|PHP|SQL|NoSQL|HTML5|CSS3|XML|JSON|YAML|TOML|GraphQL|REST|SOAP|gRPC)\b",
                
                // 2. Tech/Acronyms with numbers/dots
                @"\b(?:\.NET Core|\.NET Framework|ASP\.NET Core|ASP\.NET MVC|Windows 10|Windows 11|macOS|iOS|Android|Linux|Ubuntu|Debian|Fedora|CentOS|AWS|GCP|Azure|Kubernetes|Docker|Terraform|Ansible|GitHub|GitLab|BitBucket|JIRA|Confluence|Slack|Teams|Zoom|WebRTC|WebSocket|HTTP/1\.1|HTTP/2|HTTP/3|IPv4|IPv6|Wi-Fi|Bluetooth|USB-C|Thunderbolt|HDMI|DisplayPort|VGA|DVI|SATA|NVMe|SSD|HDD|RAM|ROM|BIOS|UEFI|CPU|GPU|TPU|FPGA|ASIC|IoT|AI|ML|DL|NLP|CV|AR|VR|XR|UI|UX|CI/CD|TDD|BDD|DDD|OOP|SOLID|DRY|KISS|YAGNI)\b",
                
                // 3. Date/Time formats
                @"\b(?:\d{1,2}:\d{2}(?::\d{2})?(?:\s?[AP]M)?|\d{1,2}/\d{1,2}/\d{2,4}|\d{2,4}-\d{1,2}-\d{1,2})\b",
                
                // 4. File extensions (with dots)
                @"\.(?:cs|fs|js|ts|java|py|rb|rs|go|kt|swift|scala|pl|php|sql|md|txt|json|xml|yaml|yml|toml|csv|tsv|html|htm|css|scss|less|sass|exe|dll|so|dylib|jar|war|ear|zip|tar|gz|7z|rar|pdf|doc|docx|xls|xlsx|ppt|pptx|jpg|jpeg|png|gif|svg|mp3|mp4|avi|mov|mkv)\b",
                
                // 5. Common abbreviations with dots
                @"\b(?:e\.g\.|i\.e\.|etc\.|vs\.|viz\.|cf\.|et al\.|p\.s\.|a\.m\.|p\.m\.|Dr\.|Mr\.|Mrs\.|Ms\.|Prof\.|Rev\.|Gen\.|Col\.|Maj\.|Capt\.|Lt\.|Sgt\.|Cpl\.|Pvt\.|Jr\.|Sr\.|Ph\.D\.|M\.D\.|B\.A\.|B\.S\.|M\.A\.|M\.S\.|M\.B\.A\.|J\.D\.|LL\.M\.|D\.D\.S\.|D\.V\.M\.|R\.N\.|C\.P\.A\.|C\.F\.A\.|P\.E\.|Esq\.|Inc\.|Ltd\.|Co\.|Corp\.|Dept\.|Univ\.|Assoc\.|Bros\.)\b",
                
                // 6. Measurement units - FIXED: removed degree symbols
                @"\b\d+(?:\.\d+)?\s*(?:mm|cm|m|km|in|ft|yd|mi|mg|g|kg|lb|oz|ml|l|gal|pt|qt|°C|°F|K|Pa|psi|bar|atm|Hz|kHz|MHz|GHz|THz|bps|kbps|Mbps|Gbps|Tbps|B|KB|MB|GB|TB|PB|EB|ZB|YB|px|em|rem|pt|pc|dpi|ppi|lx|cd|lm|W|kW|MW|GW|TW|J|kJ|MJ|GJ|TJ|eV|keV|MeV|GeV|TeV|N|kN|MN|GN|TN|m/s|km/h|mph|kph|rpm|RPM|g-force|G)\b",
                
                // 7. Currency
                @"\$?\s?\d+(?:,\d{3})*(?:\.\d{1,2})?\b|\b\d+(?:,\d{3})*(?:\.\d{1,2})?\s*(?:USD|EUR|GBP|JPY|CNY|INR|CAD|AUD|CHF|RUB|BRL|MXN|KRW|TRY|ZAR|SEK|NOK|DKK|PLN|HKD|SGD|THB|IDR|MYR|PHP|VND|AED|SAR|QAR|KWD|OMR|BHD|EGP|NGN|GHS|KES|UGX|TZS|ZMW)\b",
                
                // 8. Version numbers
                @"\b(?:v?\d+(?:\.\d+){1,3}(?:-[a-zA-Z0-9.-]+)?|v?\d+(?:\.\d+)*\s*(?:Alpha|Beta|RC|Release Candidate|Preview|RTM|GA|LTS|Stable|Nightly|Canary|Dev|Debug|Release))\b",
                
                // 9. IP addresses, MAC addresses, URLs
                @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
                @"\b(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}\b",
                @"\b(?:https?|ftp|file|ws|wss)://[^\s/$.?#].[^\s]*\b",
                
                // 10. Scientific/Mathematical notations
                @"\b\d+(?:\.\d+)?[eE][+-]?\d+\b",  // Scientific notation
                
                // 11. Chemical formulas
                @"\b(?:[A-Z][a-z]?\d*)+[A-Za-z0-9]*\b",  // Extended chemical formulas
                @"\b(?:H2O|CO2|NaCl|HCl|H2SO4|NaOH|CH4|C6H12O6|NH3|O2|N2|CO|NO2|SO2|H2|Cl2|Br2|I2|F2|He|Ne|Ar|Kr|Xe|Rn|Uuo|Fe2O3|Al2O3|SiO2|CaCO3|MgO|KCl|AgNO3|PbS|CuSO4|ZnO|HgO|MnO2|TiO2|VO2|CrO3|MoS2|WS2|ReS2|OsO4|IrCl3|PtCl2|AuCl3|Hg2Cl2|TlCl|PbCl2|Bi2O3|PoO2|At2|Rn222|FrCl|RaCl2|AcCl3|ThO2|PaCl5|UO2|NpO2|PuO2|AmO2|CmO2|BkO2|CfO2|EsO2|FmO2|MdO2|NoO2|LrCl3)\b",
                
                // 12. Biological/Medical terms
                @"\b(?:DNA|RNA|mRNA|tRNA|rRNA|cDNA|siRNA|miRNA|PCR|RT-PCR|qPCR|ELISA|Western Blot|Northern Blot|Southern Blot|SDS-PAGE|2D-PAGE|CRISPR-Cas9|TALEN|ZFN|HIV|AIDS|COVID-19|SARS-CoV-2|H1N1|H5N1|EBV|HPV|HSV-1|HSV-2|CMV|HBV|HCV|HDV|HEV|HPV-16|HPV-18|MRSA|VRE|ESBL|CRE|MDR-TB|XDR-TB|CT scan|MRI|PET scan|X-ray|ECG|EEG|EMG|EKG|ICU|CCU|NICU|PICU|ER|OR|GP|PA|NP|RN|LPN|CNA|DNR|DNR/DNI|POLST|MOLST)\b",
                
                // 13. Phone numbers (international formats)
                @"\b(?:\+\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b",
                @"\b\d{3}[-.\s]?\d{3}[-.\s]?\d{4}\b",
                
                // 14. Email addresses
                @"\b[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b",
                
                // 15. Hashtags and mentions
                @"\B#\w+\b",
                @"\B@\w+\b",
                
                // 16. Product codes, serial numbers, ISBN
                @"\b(?:ISBN[- ]?(?:10|13)?:?\s*)?(?=[0-9X]{10}|[0-9X]{13})[0-9X]+\b",
                @"\b[A-Z0-9]{4,}-[A-Z0-9]{4,}(?:-[A-Z0-9]{4,})*\b",
                
                // 17. Coordinates - FIXED: Simplified patterns
                @"\b\d{1,3}°\s?\d{1,2}['′]\s?\d{1,2}(?:\.\d+)?['""]?\s?[NS]\b",
                @"\b\d{1,3}°\s?\d{1,2}['′]\s?\d{1,2}(?:\.\d+)?['""]?\s?[EW]\b",
                @"\b[-+]?\d{1,3}\.\d+\s*,\s*[-+]?\d{1,3}\.\d+\b",
                
                // 18. Vehicle identifiers
                @"\b[A-HJ-NPR-Z0-9]{17}\b",  // VIN numbers
                
                // 19. Common acronyms (2-6 uppercase letters)
                @"\b(?:[A-Z]{2,6}|[A-Z][A-Z0-9]{1,5})\b",
                
                // 20. Roman numerals (for senses, chapters, etc.)
                @"\b(?:[IVXLCDM]{2,}|[ivxlcdm]{2,})\b",
                
                // 21. Fractions and ratios
                @"\b\d+/\d+\b",
                @"\b\d+(?:\.\d+)?:\d+(?:\.\d+)?\b",  // Ratios
                
                // 22. Percentages and degrees - FIXED: removed problematic symbols
                @"\b\d+(?:\.\d+)?%\b",
                @"\b\d+(?:\.\d+)?°[CFK]?\b",
                
                // 23. Ordinal numbers
                @"\b\d+(?:st|nd|rd|th)\b",
                
                // 24. Common abbreviations without dots
                @"\b(?:aka|asap|btw|fyi|imo|imho|tbh|lol|rofl|wtf|omg|idk|brb|ttyl|np|yw|ty|plz|thx|gg|glhf|hf|nsfw|sfw|tl;dr|eli5|ama|til|diy|faq|ftp|http|ssh|ssl|tls|udp|tcp|ip|mac|lan|wan|vpn|api|sdk|ide|cli|gui|oop|sql|nosql|ram|rom|cpu|gpu|ssd|hdd|usb|hdmi|vga|dvi|wifi|bt|nfc|rfid|gps|gis|cad|cam|ai|ml|dl|nlp|cv|ar|vr|iot|ui|ux|qa|dev|ops|it|hr|ceo|cfo|cto|cio|coo|cmo|cso)\b",
                
                // 25. Legal citations
                @"\b\d+\s+[A-Z]+\s+\d+\b",  // e.g., "42 USC 1983"
                
                // 26. Bible verses
                @"\b(?:Gen|Exod|Lev|Num|Deut|Josh|Judg|Ruth|1\s?Sam|2\s?Sam|1\s?Kgs|2\s?Kgs|1\s?Chr|2\s?Chr|Ezra|Neh|Esth|Job|Ps|Prov|Eccl|Song|Isa|Jer|Lam|Ezek|Dan|Hos|Joel|Amos|Obad|Jonah|Mic|Nah|Hab|Zeph|Hag|Zech|Mal|Matt|Mark|Luke|John|Acts|Rom|1\s?Cor|2\s?Cor|Gal|Eph|Phil|Col|1\s?Thess|2\s?Thess|1\s?Tim|2\s?Tim|Titus|Phlm|Heb|Jas|1\s?Pet|2\s?Pet|1\s?John|2\s?John|3\s?John|Jude|Rev)\s+\d+:\d+(?:-\d+)?\b",
                
                // 27. Mathematical constants
                @"\b(?:e|i|j|pi|tau|phi|gamma|zeta|beta|alpha|omega|Omega|infinity|NaN|Inf)\b",
                
                // 28. Programming symbols
                @"\b(?:=>|->|<-|==|!=|<=|>=|&&|\|\||\+=|-=|\*=|\/=|%=|\^=|&=|\|=|<<=|>>=|++|--|<<|>>|&|\||\^|~|::|\.\.\.|\.\.|@|#|\$|%|\?|:)\b",
                
                // 29. Mathematical operators - FIXED: simplified to ASCII
                @"[+\-*/=<>!&|^~%@#]",

                // 29. Unicode/math symbols
                @"[←→↑↓↔↕↨↻↺⇄⇅⇆⇌⇋⇔⇎⇏∀∃∄∅∆∇∈∉∋∌∏∑∓∕∗∘∙√∛∜∝∞∟∠∥∦∧∨∩∪∫∬∭∮∯∰∱∲∳∴∵∶∷∸∹∺∻∼∽∾∿≀≁≂≃≄≅≆≇≈≉≊≋≌≍≎≏≐≑≒≓≔≕≖≗≘≙≚≛≜≝≞≟≠≡≢≣≤≥≦≧≨≩≪≫≬≭≮≯≰≱≲≳≴≵≶≷≸≹≺≻≼≽≾≿⊀⊁⊂⊃⊄⊅⊆⊇⊈⊉⊊⊋⊌⊍⊎⊏⊐⊑⊒⊓⊔⊕⊖⊗⊘⊙⊚⊛⊜⊝⊞⊟⊠⊡⊢⊣⊤⊥⊦⊧⊨⊩⊪⊫⊬⊭⊮⊯⊰⊱⊲⊳⊴⊵⊶⊷⊸⊹⊺⊻⊼⊽⊾⊿⋀⋁⋂⋃⋄⋅⋆⋇⋈⋉⋊⋋⋌⋍⋎⋏⋐⋑⋒⋓⋔⋕⋖⋗⋘⋙⋚⋛⋜⋝⋞⋟⋠⋡⋢⋣⋤⋥⋦⋧⋨⋩⋪⋫⋬⋭⋮⋯⋰⋱]",
        
                // 30. Emoji/symbols (basic support)
                @"[©®™℠℡℗℀℁℅℆℈℉℔№℗℘ℙℚℛℜℝ℞℟℠℡™℣ℤ℥Ω℧ℨ℩KÅℬℭ℮ℯℰℱℲℳℴℵℶℷℸℹ℺℻ℼℽℾℿ⅀⅁⅂⅃⅄ⅅⅆⅇⅈⅉ⅊⅋⅌⅍ⅎ⅏]"
            }),
            Opt);


        public sealed class ProtectedTokenResult
        {
            public ProtectedTokenResult(string protectedText, IReadOnlyDictionary<string, string> map)
            {
                ProtectedText = protectedText;
                Map = map;
            }

            public string ProtectedText { get; }
            public IReadOnlyDictionary<string, string> Map { get; }
            public bool HasTokens => Map.Count > 0;
        }

        public static ProtectedTokenResult Protect(string input)
        {
            if (string.IsNullOrEmpty(input))
                return new ProtectedTokenResult(input, new Dictionary<string, string>(0));

            try
            {
                var matches = ProtectedTokenRegex.Matches(input);
                if (matches.Count == 0)
                    return new ProtectedTokenResult(input, new Dictionary<string, string>(0));

                // Sort by start asc, length desc to avoid overlapping issues
                var ordered = matches
                    .Cast<Match>()
                    .Where(m => m.Success && m.Length > 0)
                    .OrderBy(m => m.Index)
                    .ThenByDescending(m => m.Length)
                    .ToList();

                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                var usedRanges = new List<(int Start, int End)>();

                var working = input;
                var offset = 0;
                var tokenIndex = 1;

                foreach (var m in ordered)
                {
                    if (tokenIndex > MaxTokens)
                        break;

                    var start = m.Index;
                    var end = m.Index + m.Length;

                    // Skip overlaps (based on original indices)
                    if (IsOverlapping(usedRanges, start, end))
                        continue;

                    var token = m.Value;
                    if (string.IsNullOrWhiteSpace(token))
                        continue;

                    token = token.Trim();

                    if (token.Length > MaxTokenLength)
                        continue;

                    var placeholder = BuildPlaceholder(tokenIndex);

                    map[placeholder] = token;
                    usedRanges.Add((start, end));

                    // Apply replacement on the current string using adjusted offset
                    var adjustedStart = start + offset;

                    if (adjustedStart < 0 || adjustedStart > working.Length)
                        continue;

                    if (adjustedStart + token.Length > working.Length)
                        continue;

                    working = working.Remove(adjustedStart, token.Length)
                                     .Insert(adjustedStart, placeholder);

                    offset += placeholder.Length - token.Length;
                    tokenIndex++;
                }

                if (map.Count == 0)
                    return new ProtectedTokenResult(input, new Dictionary<string, string>(0));

                return new ProtectedTokenResult(working, map);
            }
            catch
            {
                // Never crash pipeline
                return new ProtectedTokenResult(input, new Dictionary<string, string>(0));
            }
        }

        public static string Restore(string text, IReadOnlyDictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (map is null || map.Count == 0)
                return text;

            try
            {
                var result = text;

                // Deterministic restore: placeholder order by numeric id
                foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    if (string.IsNullOrEmpty(kv.Key))
                        continue;

                    if (kv.Value is null)
                        continue;

                    result = result.Replace(kv.Key, kv.Value, StringComparison.Ordinal);
                }

                return result;
            }
            catch
            {
                return text;
            }
        }

        private static bool IsOverlapping(List<(int Start, int End)> usedRanges, int start, int end)
        {
            for (int i = 0; i < usedRanges.Count; i++)
            {
                var r = usedRanges[i];
                if (start < r.End && end > r.Start)
                    return true;
            }

            return false;
        }

        private static string BuildPlaceholder(int index)
        {
            // Fixed-width numeric = stable and sortable
            return $"{PlaceholderPrefix}{index:000000}{PlaceholderSuffix}";
        }
    }
}
