namespace TestService1Console
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
             .ConfigureLogging(logging =>
             {
                 logging.ClearProviders();              // remove the default Console provider
                 logging.AddSimpleConsole(options =>
                 {
                     options.SingleLine = true;                   // <-- all on one line
                     options.TimestampFormat = "MM/dd/yyyy HH:mm:ss "; // optional
                     options.IncludeScopes = false;                  // optional
                 });
             })
             .ConfigureServices((hostCtx, services) =>
             {
                 services.AddHostedService<Worker>();
             });

            var host = builder.Build();
            host.Run();
        }
    }
}