using System;
using System.Threading.Tasks;

namespace RazorLightWebApp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var service = new Service();
            //await service.FillStringAsync();
            await service.FillTemplateAsync();

            Console.WriteLine("Hello World!");
        }
    }
}
