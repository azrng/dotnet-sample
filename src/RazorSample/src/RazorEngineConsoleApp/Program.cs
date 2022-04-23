using System;

namespace RazorEngineConsoleApp
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var service = new Service();

            service.GetUserGrade1();
            //service.GetUserGrade2();
            //service.GetUserGrade3();

            Console.WriteLine("success");
        }
    }
}