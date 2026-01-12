// Global using directives

global using Dapper;
global using DictionaryImporter.Core.Abstractions;
global using DictionaryImporter.Core.Canonical;
global using DictionaryImporter.Core.Linguistics;
global using DictionaryImporter.Core.Parsing;
global using DictionaryImporter.Core.Persistence;
global using DictionaryImporter.Core.PreProcessing;
global using DictionaryImporter.Core.Validation;
global using DictionaryImporter.Domain.Models;
global using DictionaryImporter.Infrastructure.Graph;
global using DictionaryImporter.Infrastructure.Persistence;
global using DictionaryImporter.Infrastructure.PostProcessing.Enrichment;
global using DictionaryImporter.Orchestration;
global using DictionaryImporter.Sources.Collins.Models;
global using DictionaryImporter.Sources.EnglishChinese.Models;
global using DictionaryImporter.Sources.Gutenberg.Models;
global using DictionaryImporter.Sources.Oxford.Models;
global using DictionaryImporter.Sources.StructuredJson.Models;
global using Microsoft.Data.SqlClient;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using System.Runtime.CompilerServices;
global using System.Text;
global using System.Text.RegularExpressions;