namespace DictionaryImporter.Bootstrap
{
    public static class BootstrapLogging
    {
        public static void Configure()
        {
            Log.Logger =
                new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Console()
                    .WriteTo.File(
                        "logs/dictionary-importer-.log",
                        rollingInterval: RollingInterval.Day)
                    .CreateLogger();
        }

        public static void Register(IServiceCollection services)
        {
            services.AddLogging(b =>
            {
                b.ClearProviders();
                b.AddSerilog(dispose: true);
            });
        }
    }
}