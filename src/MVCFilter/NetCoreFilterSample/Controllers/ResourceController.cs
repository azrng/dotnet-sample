using Microsoft.AspNetCore.Mvc;
using NetCoreFilterSample.Filter;

namespace NetCoreFilterSample.Controllers;

/// <summary>
/// 资源过滤器
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public class ResourceController : ControllerBase
{
    /*
       Resource是第二优先，会在Authorization之后，模型绑定(Model Binding)之前执行。通常会是需要对Model加工处理才用。
    */

    private readonly ILogger<ResourceController> _logger;

    public ResourceController(ILogger<ResourceController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///获取时间（同步验证IResourceFilter）
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [ServiceFilter(typeof(Resource_01Filter))]
    public string Get01()
    {
        Console.WriteLine("业务逻辑开始处理");
        Console.WriteLine("处...");
        Console.WriteLine("理...");
        Console.WriteLine("中...");

        var result = "成功" + DateTime.Now;

        Console.WriteLine("业务逻辑结束处理");

        //因为在资源过滤器里面使用了缓存，所以请求这个东西的返回值是不变的
        return result;
    }

    /// <summary>
    /// 根据ID获取指定信息（同步验证IResourceFilter）
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPost]
    [ServiceFilter(typeof(Resource_01Filter))]
    public string Get02(Get2Request request)
    {
        Console.WriteLine("业务逻辑开始处理");
        Console.WriteLine("处...");
        Console.WriteLine("理...");
        Console.WriteLine("中...");

        var result = request.Id + DateTime.Now;
        Console.WriteLine("业务逻辑结束处理");
        return result;
    }

    /// <summary>
    /// 根据ID获取指定信息（异步验证IAsyncResourceFilter）
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPost]
    [ServiceFilter(typeof(Resource_02Filter))]
    public string Get03(Get2Request request)
    {
        Console.WriteLine("业务逻辑开始处理");
        Console.WriteLine("处...");
        Console.WriteLine("理...");
        Console.WriteLine("中...");

        var result = request.Id + DateTime.Now;
        Console.WriteLine("业务逻辑结束处理");
        return result;
    }
}

public class Get2Request
{
    public Get2Request()
    {
        Console.WriteLine("Get2Request被初始化");
    }

    /// <summary>
    /// 标识
    /// </summary>
    public string Id { get; set; }
}