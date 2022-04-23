using RazorLight;
using RazorLightWebApp.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace RazorLightWebApp;

public class Service
{
    /// <summary>
    /// 填充字符串模板
    /// </summary>
    public async Task FillStringAsync()
    {
        //动态编译razor
        var engine = new RazorLightEngineBuilder()
            .UseEmbeddedResourcesProject(typeof(Program))//必须有一个模板的类型
            .SetOperatingAssembly(typeof(Program).Assembly)
            .UseMemoryCachingProvider()
            .DisableEncoding()//禁用编码，否则会把中文字符串编码成Unicode
            .Build();

        var template = "你好, @Model.Name. 欢迎使用RazorLight";

        //运行
        string result = await engine.CompileRenderStringAsync("templateKey", template, new { Name = "张三" });
        Console.WriteLine(result);
    }

    /// <summary>
    /// 填充html模板
    /// </summary>
    public async Task FillTemplateAsync()
    {
        //动态编译razor
        var engine = new RazorLightEngineBuilder()
            .UseEmbeddedResourcesProject(typeof(Program))//必须有一个模板的类型
            .SetOperatingAssembly(typeof(Program).Assembly)
            .UseMemoryCachingProvider()
            .DisableEncoding()//禁用编码，否则会把中文字符串编码成Unicode
            .Build();

        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "View", "usergrade1.cshtml");
        if (!File.Exists(filePath))
        {
            Console.WriteLine("模板文件不存在");
            return;
        }
        //打开并且读取模板
        string template = File.ReadAllText(filePath);

        //运行
        string result = await engine.CompileRenderStringAsync("templateKey", template, UserGradeDto.GetInfo());
        Console.WriteLine(result);
    }
}