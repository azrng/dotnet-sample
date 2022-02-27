using Common.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NetCoreFilterSample.Filter;

/// <summary>
/// 返回过滤器
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class Result_01Filter : Attribute, IResultFilter
{
    private readonly IModelMetadataProvider _modelMetadataProvider;

    public Result_01Filter(IModelMetadataProvider modelMetadataProvider)
    {
        _modelMetadataProvider = modelMetadataProvider;
    }

    /// <summary>
    /// 在result执行前发生(在view 呈现前)，使用场景：设置客户端缓存，服务器端压缩
    /// </summary>
    /// <param name="context"></param>
    public void OnResultExecuting(ResultExecutingContext context)
    {
        // 在结果执行之前调用的一系列操作  mvc中使用根据不同的参数等返回不同的页面
        //var param = context.HttpContext.Request.Query["View"];
        //if (param == "1")//显示中文系统
        //{
        //    var result = new ViewResult { ViewName = "~/Views/Test/Chinese.cshtml" };
        //    result.ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(_modelMetadataProvider, context.ModelState);
        //    context.Result = result;
        //}

        //设置响应头
        //context.HttpContext.Response.Headers.Add("", new string[] { "" });

        Console.WriteLine("OnResultExecuting");
        context.Result = new JsonResult(ResultModel<object>.Success((ObjectResult)context.Result));
    }

    /// <summary>
    /// 渲染视图后执行,当Action完成后
    /// </summary>
    /// <param name="context"></param>
    public void OnResultExecuted(ResultExecutedContext context)
    {
        var path = context.HttpContext.Request.Path;
        // 在结果执行之后调用的操作...
        Console.WriteLine($"OnResultExecuted  Path：{path}");

        //注意：目前我并不知道找个方法适合做什么，并且context.Result方法也是只读的。
    }
}

/// <summary>
///异步方法
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class Result_02Filter : Attribute, IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        context.Result = new JsonResult(ResultModel<object>.Success((ObjectResult)context.Result));
        Console.WriteLine("之前");
        await next();
        Console.WriteLine("之后");
    }
}

/// <summary>
/// 异步方法
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class Result_03Filter : ResultFilterAttribute
{
    public override void OnResultExecuted(ResultExecutedContext context)
    {
        Console.WriteLine("OnResultExecuted");
        base.OnResultExecuted(context);
    }

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        Console.WriteLine("OnResultExecuting");
        base.OnResultExecuting(context);
    }

    public override Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        Console.WriteLine("OnResultExecutionAsync");
        return base.OnResultExecutionAsync(context, next);
    }
}