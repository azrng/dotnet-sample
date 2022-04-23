using RazorLight;

namespace RazorLightWebApp.Utils;

/// <summary>
/// 文档生成
/// </summary>
public interface IDocumentGeneration
{
    Task<string> CompileRenderStringAsync<TModel>(string key, string template, TModel inputContent);
}

public class DocumentGeneration : IDocumentGeneration
{
    private readonly IRazorLightEngine _razorLightEngine;

    public DocumentGeneration(IRazorLightEngine razorLightEngine)
    {
        _razorLightEngine = razorLightEngine;
    }

    /// <summary>
    /// 将TModel实体内容填充到Razor模板中渲染 输出模板填充内容后的字符串
    /// </summary>
    /// <typeparam name="TModel"></typeparam>
    /// <param name="template">模板</param>
    /// <param name="inputContent"></param>
    /// <returns></returns>
    public async Task<string> CompileRenderStringAsync<TModel>(string key, string template, TModel inputContent)
    {
        return await _razorLightEngine.CompileRenderStringAsync(key, template, inputContent);
    }
}