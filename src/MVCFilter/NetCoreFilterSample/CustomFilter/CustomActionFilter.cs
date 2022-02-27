using Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using System.Net;

namespace NetCoreFilterSample.CustomFilter;

#region 入参模型验证

/// <summary>
/// 对模型验证过滤器
/// </summary>
public class ModelValidationFilter : ActionFilterAttribute
{
    //实现目的：比如接口中的常用参数有患者ID，我们可以写过滤器统一校验患者ID是否有效
    private readonly ILogger<ModelValidationFilter> _logger;

    public ModelValidationFilter(ILogger<ModelValidationFilter> logger)
    {
        _logger = logger;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(context.ModelState);
        }

        if (context.HttpContext.Request.Query.TryGetValue("patientId", out StringValues patientIdValue))
        {
            if (int.TryParse(patientIdValue.FirstOrDefault(), out int patientId))
            {
                if (patientId == 0)
                {
                    _logger.LogWarning($"{context.HttpContext.Request.Path} 患者标识无效");
                    context.Result = new BadRequestObjectResult("患者标识无效");
                }
            }
        }

        if (context.HttpContext.Request.Method == "POST" || context.HttpContext.Request.Method == "PUT")
        {
            context.HttpContext.Request.Body.Seek(0, SeekOrigin.Begin);//读取到Body后，重新设置Stream到起始位置
            var stream = new StreamReader(context.HttpContext.Request.Body);
            string body = stream.ReadToEndAsync().GetAwaiter().GetResult();
            JObject jobject = JObject.Parse(body);
            if (int.TryParse(jobject["patientId"].ToString(), out int patientId))
            {
                if (patientId == 0)
                {
                    _logger.LogWarning($"{context.HttpContext.Request.Path} 患者标识无效");
                    context.Result = new BadRequestObjectResult("患者标识无效");
                }
            }
            context.HttpContext.Request.Body.Seek(0, SeekOrigin.Begin);//读取到Body后，重新设置Stream到起始位置
        }
    }
}

#endregion

#region 日志记录

/// <summary>
/// 日志记录
/// </summary>
public class RequestParamRecordFilter : ActionFilterAttribute
{
    //目的：记录请求的消息
    private readonly ILogger<ModelValidationFilter> _logger;

    public RequestParamRecordFilter(ILogger<ModelValidationFilter> logger)
    {
        _logger = logger;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        //设置可以多次读取
        context.HttpContext.Request.Body.Seek(0, SeekOrigin.Begin);//读取到Body后，重新设置Stream到起始位置
        var sr = new StreamReader(context.HttpContext.Request.Body);
        var data = sr.ReadToEndAsync().GetAwaiter().GetResult();
        _logger.LogInformation(
            $"Time:{DateTime.Now:yyyy-MM-dd HH:mm:ss} requestUrl:{context.HttpContext.Request.Path}  Method:{context.HttpContext.Request.Method}  requestBodyData: " +
            data);
        //读取到Body后，重新设置Stream到起始位置
        context.HttpContext.Request.Body.Seek(0, SeekOrigin.Begin);
        _logger.LogInformation($"Host: {context.HttpContext.Request.Host.Host}");
        _logger.LogInformation($"Client IP: {context.HttpContext.Connection.RemoteIpAddress}");
    }
}

#endregion

#region 幂等性处理

/// <summary>
/// 接口幂等性处理
/// </summary>
public class IdempotentAttributeFilter : Attribute, IActionFilter
{
    private readonly IMemoryCache _cache;

    public IdempotentAttributeFilter(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        //可以根据用户ID或者请求地址标识当前用户
        var path = context.HttpContext.Request.Path;
        var userId = "123456";//这个可以从上下文中获取

        var key = "IdempotencyKey" + userId + path.ToString();

        var method = context.HttpContext.Request.Method;
        if (method == "POST" || method == "put")
        {
            //直接限制了该接口不允许一个用户在2秒内请求多次
            var cacheData = _cache.Get<string>(key);
            if (cacheData != null)
            {
                throw new ParameterException("不允许重复提交");
            }

            _cache.Set(key, "1", TimeSpan.FromSeconds(20));
        }
    }
}

#endregion

#region 根据ip接口请求限制

/// <summary>
/// 根据ip接口请求限制
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class RequestLimitFilter : ActionFilterAttribute
{
    private string Name { get; }
    private int LimitRequestNum { get; set; }
    private int Seconds { get; set; }

    private MemoryCache _cache { get; } = new MemoryCache(new MemoryCacheOptions());

    /// <summary>
    /// 请求限制属性
    /// </summary>
    /// <param name="name">key</param>
    /// <param name="limitRequestNum">限制的次数</param>
    /// <param name="seconds">限制时间</param>
    public RequestLimitFilter(string name, int limitRequestNum = 5, int seconds = 10)
    {
        Name = name;
        LimitRequestNum = limitRequestNum;
        Seconds = seconds;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var ipAddress = context.HttpContext.Request.HttpContext.Connection.RemoteIpAddress;
        var key = $"{Name}-{ipAddress}";

        var prevReqCount = _cache.Get<int>(key);
        if (prevReqCount >= LimitRequestNum)
        {
            context.Result = new ContentResult
            {
                Content = $"Request limit is exceeded. Try again in {Seconds} seconds.",
            };
            context.HttpContext.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        }
        else
        {
            _cache.Set(key, (prevReqCount + 1), TimeSpan.FromSeconds(Seconds));
        }
    }
}

#endregion