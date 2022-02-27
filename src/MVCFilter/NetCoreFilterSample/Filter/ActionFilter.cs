using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NetCoreFilterSample.Filter;

/// <summary>
/// 同步Action过滤器
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class Action_01Filter : Attribute, IActionFilter
{
    private readonly ILogger<Action_01Filter> _logger;

    public Action_01Filter(ILogger<Action_01Filter> logger)
    {
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        _logger.LogInformation("action 执行前");

        //如果标记有允许所有，不做处理那么就跳过
        if (context.ActionDescriptor.EndpointMetadata.Any(t => t.GetType() == typeof(AllowAnonymousAttribute)))
        {
            return;
        }

        //记录请求来的一些参数
        var queryUrl = context.HttpContext.Request.Query;
        string path = context.HttpContext.Request.Path;
        string name = context.HttpContext.User.Identity?.Name;
        _logger.LogInformation($"Action信息  queryUrl:{queryUrl},path:{path},name:{name}");
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        _logger.LogInformation("action 执行后");
    }
}

/// <summary>
/// 异步Action过滤器
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class Action_02Filter : Attribute, IAsyncActionFilter
{
    private readonly ILogger<Action_01Filter> _logger;

    public Action_02Filter(ILogger<Action_01Filter> logger)
    {
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        _logger.LogInformation("action 执行前");

        //如果标记有允许所有，不做处理那么就跳过
        if (context.ActionDescriptor.EndpointMetadata.Any(t => t.GetType() == typeof(AllowAnonymousAttribute)))
        {
            return;
        }

        //记录请求来的一些参数
        var queryUrl = context.HttpContext.Request.Query;
        string path = context.HttpContext.Request.Path;
        string name = context.HttpContext.User.Identity?.Name;
        _logger.LogInformation($"Action信息  queryUrl:{queryUrl},path:{path},name:{name}");

        await next();

        _logger.LogInformation("action 执行后");
    }
}

/// <summary>
/// Action过滤器
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class Action_03Filter : ActionFilterAttribute
{
    private readonly ILogger<Action_01Filter> _logger;

    public Action_03Filter(ILogger<Action_01Filter> logger)
    {
        _logger = logger;
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        _logger.LogInformation("action 执行后 OnActionExecuted 1 ");

        base.OnActionExecuted(context);
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        _logger.LogInformation("action 执行前 OnActionExecuting 2 ");
        base.OnActionExecuting(context);
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        //会进该方法
        //OnActionExecuted OnActionExecuting方法我理解是应该在这里根据条件判断去调用上面的方法

        _logger.LogInformation("action 执行前 OnActionExecutionAsync 3 ");
        await next();
        _logger.LogInformation("action 执行前 OnActionExecutionAsync 4 ");
    }

    public override void OnResultExecuted(ResultExecutedContext context)
    {
        _logger.LogInformation("result 执行后 OnResultExecuted 5 ");
        base.OnResultExecuted(context);
    }

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        _logger.LogInformation("result 执行前 OnResultExecuting 6 ");
        base.OnResultExecuting(context);
    }

    public override async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        //会进该方法
        //OnResultExecuted、OnResultExecuting方法并不会直接触发，而是根据条件在当前方法中执行调用的

        _logger.LogInformation("result 执行前 OnResultExecutionAsync 7 ");

        await next();

        _logger.LogInformation("result 执行后 OnResultExecutionAsync 8 ");

        //返回
        //context.Result = new ObjectResult("");
        //await base.OnResultExecutionAsync(context, next);
    }
}