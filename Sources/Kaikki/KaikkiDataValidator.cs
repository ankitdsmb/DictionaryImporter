using System;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Sources.Kaikki
{
    public static class KaikkiDataValidator
    {
        public static void ValidateKaikkiData(string connectionString)
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();

            // Check if we have Kaikki entries
            var kaikkiCount = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM dbo.DictionaryEntry WHERE SourceCode = 'KAIKKI'");

            Console.WriteLine($"Total Kaikki entries: {kaikkiCount}");

            // Check for entries with definitions
            var withDefinitions = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM dbo.DictionaryEntry WHERE SourceCode = 'KAIKKI' AND LEN(Definition) > 10");

            Console.WriteLine($"Kaikki entries with definitions: {withDefinitions}");

            // Check parsed definitions
            var parsedCount = conn.ExecuteScalar<int>(
                @"SELECT COUNT(*)
                  FROM dbo.DictionaryEntryParsed p
                  JOIN dbo.DictionaryEntry e ON e.DictionaryEntryId = p.DictionaryEntryId
                  WHERE e.SourceCode = 'KAIKKI'");

            Console.WriteLine($"Parsed Kaikki entries: {parsedCount}");

            // Sample some entries to see what's being stored
            var sampleEntries = conn.Query<dynamic>(
                @"SELECT TOP 10 e.Word, e.Definition, p.RawFragment
                  FROM dbo.DictionaryEntry e
                  LEFT JOIN dbo.DictionaryEntryParsed p ON e.DictionaryEntryId = p.DictionaryEntryId
                  WHERE e.SourceCode = 'KAIKKI'
                  ORDER BY e.Word");

            foreach (var entry in sampleEntries)
            {
                Console.WriteLine($"Word: {entry.Word}");

                var def = entry.Definition as string;
                if (!string.IsNullOrEmpty(def))
                {
                    Console.WriteLine($"Definition: {def.Substring(0, Math.Min(100, def.Length))}");
                }
                else
                {
                    Console.WriteLine("Definition: <null/empty>");
                }

                Console.WriteLine($"RawFragment length: {((string?)entry.RawFragment)?.Length ?? 0}");
                Console.WriteLine("---");
            }
        }
    }
}