using Common.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NetCoreFilterSample.CustomFilter;

#region 异常过滤器
/// <summary>
/// 自定义全局异常过滤器
/// </summary>
public class CustomExceptionFilter : IExceptionFilter
{
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly ILogger<CustomExceptionFilter> _logger;

    public CustomExceptionFilter(ILogger<CustomExceptionFilter> logger,
        IWebHostEnvironment hostEnvironment)
    {
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    public void OnException(ExceptionContext context)
    {
        //如果异常没有处理
        if (context.ExceptionHandled)
            return;
        var result = new ResultModel
        {
            Code = "500",
            IsSuccess = false,
            Message = "系统异常，请联系管理员",
        };
        _logger.LogError($"异常 path:{context.Result} message:{context.Exception.Message} StackTrace:{context.Exception.StackTrace}");
        if (_hostEnvironment.IsDevelopment())
        {
            result.Message += "," + context.Exception.Message;
        }

        context.Result = new JsonResult(result);
        //异常已处理
        context.ExceptionHandled = true;
    }
} 
#endregion