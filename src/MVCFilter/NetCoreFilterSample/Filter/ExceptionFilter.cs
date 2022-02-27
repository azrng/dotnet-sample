using Common.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json;

namespace NetCoreFilterSample.Filter;

/// <summary>
/// 异常过滤器(同步)
/// </summary>
public class Exception_01Filter : Attribute, IExceptionFilter
{
    private readonly ILogger<Exception_01Filter> _logger;
    private readonly IModelMetadataProvider _modelMetadataProvider;

    public Exception_01Filter(ILogger<Exception_01Filter> logger,
        IModelMetadataProvider modelMetadataProvider)
    {
        _logger = logger;
        _modelMetadataProvider = modelMetadataProvider;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.ExceptionHandled)
            return;

        //日志收集
        _logger.LogError(context.Exception, "出错" + context?.Exception?.Message ?? "异常");

        var response = new ResultModel<string>()
        {
            Message = $"处理失败 {context.Exception.Message}",
            IsSuccess = false,
            Code = "500"
        };
        //或者
        context.Result = new JsonResult(response);

        //如果是mvc使用，那么就可以返回错误界面
        //var result = new ViewResult { ViewName = "~/Views/Shared/Error.cshtml" };
        //result.ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(_modelMetadataProvider, context.ModelState);
        //context.Result = result;//断路器只要一对result赋值就不继续往后赋值了

        context.ExceptionHandled = true;
    }
}

/// <summary>
/// 异常过滤器(异步)
/// </summary>
public class Exception_02Filter : Attribute, IAsyncExceptionFilter
{
    private readonly ILogger<Exception_02Filter> _logger;

    //构造注入日志组件
    public Exception_02Filter(ILogger<Exception_02Filter> logger)
    {
        _logger = logger;
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.ExceptionHandled)
            return;

        //日志收集
        _logger.LogError(context.Exception, context?.Exception?.Message ?? "异常");

        var response = new ResultModel<string>()
        {
            Message = $"处理失败 {context.Exception.Message}",
            IsSuccess = false,
            Code = "500"
        };
        var setting = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()//指定序列化方式为驼峰式
        };
        await context.HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(response, setting));
        //或者
        //context.Result = new JsonResult(response);//断路器只要一对result赋值就不继续往后赋值了
        context.ExceptionHandled = true;
    }
}

/// <summary>
/// 异常过滤器(异步)
/// </summary>
public class Exception_03Filter : ExceptionFilterAttribute
{
    private readonly ILogger<Exception_03Filter> _logger;

    public Exception_03Filter(ILogger<Exception_03Filter> logger)
    {
        _logger = logger;
    }

    public override void OnException(ExceptionContext context)
    {
        //后到达
        _logger.LogInformation("到达 OnException");
        base.OnException(context);
    }

    public override Task OnExceptionAsync(ExceptionContext context)
    {
        //先到达
        _logger.LogInformation("到达 OnExceptionAsync");
        return base.OnExceptionAsync(context);
    }
}