using Azrng.ConsoleApp.DependencyInjection;
using DuckDbConsoleApp.AzrngQuacks;

var server = new ConsoleAppServer(args);

var service = server.Build<QueryStarted>();
await service.RunAsync();