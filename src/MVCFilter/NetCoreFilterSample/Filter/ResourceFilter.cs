using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace NetCoreFilterSample.Filter;

/// <summary>
/// 资源过滤器(同步)  在创建控制器实例之前被调用
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class Resource_01Filter : Attribute, IResourceFilter
{
    /*
     使用场景：可以做缓存：比如说第一次请求先到OnResourceExecuting，根据请求地址或者参数去判断是否已经保存数据，没有发现往下走创建Action实例，
    然后在OnResourceExecuted进行存储，然后再一次访问这个接口时候，OnResourceExecuting直接就赋值Result，所以就不再创建控制器实例

    具体示例：根据请求地址做接口缓存、根据请求参数做缓存处理
     */

    /// <summary>
    /// 模拟数据源
    /// </summary>
    private static readonly Dictionary<string, object> _dictionaryCache = new Dictionary<string, object>();

    private readonly ILogger<Result_01Filter> _logger;

    public Resource_01Filter(ILogger<Result_01Filter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 在接口被调用前触发
    /// </summary>
    /// <param name="context"></param>
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        _logger.LogInformation("同步 OnResourceExecuting");

        if (context.HttpContext.Request.Method == "Get")
        {
            //定义一个key存储缓存
            string key = context.HttpContext.Request.Path.ToString();
            if (_dictionaryCache.Any(t => t.Key == key))
            {
                //如果缓存有内容就直接返回
                context.Result = _dictionaryCache[key] as IActionResult;    //Result 短路器
            }
            //如果没有缓存就接着运行，然后再executed里面设置缓存
        }
        else
        {
            context.HttpContext.Request.EnableBuffering();//可以实现多次读取Body
            var sr = new StreamReader(context.HttpContext.Request.Body);
            string data = sr.ReadToEndAsync().GetAwaiter().GetResult();//不允许同步读取
            if (data == null)//body取不到数据直接跳过，一般情况下不会出现该情况
                return;

            //获取到body的请求字符串
            _logger.LogInformation("data=" + data);
            //根据请求字符串去做处理解析是否做缓存，本次示例是获取到请求的boyd里面的ID，如果存在id，那么就做资源缓存(id作为key)，
            var jobject = JObject.Parse(data);
            if (jobject["id"]?.ToString() != null)
            {
                string key = context.HttpContext.Request.Path.ToString() + jobject["id"].ToString();
                if (_dictionaryCache.Any(t => t.Key == key))
                {
                    //如果缓存有内容就直接返回
                    context.Result = _dictionaryCache[key] as IActionResult;    //Result 短路器
                }
            }

            context.HttpContext.Request.Body.Seek(0, SeekOrigin.Begin);//读取到Body后，重新设置Stream到起始位置
            context.HttpContext.Request.Body.Position = 0;
        }
    }

    /// <summary>
    /// 在接口调用结束时候触发
    /// </summary>
    /// <param name="context"></param>
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        _logger.LogInformation("异步 OnResourceExecuted");
        //把数据存储缓存 Key---path;  实际情况这里缓存应该加上过期时间
        if (context.HttpContext.Request.Method == "Get")
        {
            string key = context.HttpContext.Request.Path.ToString();//将请求路径作为缓存的key
            _dictionaryCache[key] = context.Result;//刚才接口返回的值
        }
        else
        {
            context.HttpContext.Request.Body.Seek(0, SeekOrigin.Begin);//读取到Body后，重新设置Stream到起始位置
            context.HttpContext.Request.Body.Position = 0;
            var sr = new StreamReader(context.HttpContext.Request.Body);
            string data = sr.ReadToEndAsync().GetAwaiter().GetResult();//不允许同步读取
            if (data == null)
                return;

            var jobject = JObject.Parse(data);
            if (jobject["id"]?.ToString() != null)
            {
                string key = context.HttpContext.Request.Path.ToString() + jobject["id"].ToString();
                _dictionaryCache[key] = context.Result;//刚才接口返回的值
            }
        }
    }
}

/// <summary>
/// 资源过滤器(异步)  在创建控制器实例之前被调用
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class Resource_02Filter : Attribute, IAsyncResourceFilter
{
    /*
     使用场景：可以做缓存：比如说第一次请求先到OnResourceExecuting，根据请求地址或者参数去判断是否已经保存数据，没有发现往下走创建Action实例，
    然后在OnResourceExecuted进行存储，然后再一次访问这个接口时候，OnResourceExecuting直接就赋值Result，所以就不再创建控制器实例

    具体示例：根据请求地址做接口缓存、根据请求参数做缓存处理
     */

    /// <summary>
    /// 模拟数据源
    /// </summary>
    private static readonly Dictionary<string, object> _dictionaryCache = new Dictionary<string, object>();

    private readonly ILogger<Result_01Filter> _logger;

    public Resource_02Filter(ILogger<Result_01Filter> logger)
    {
        _logger = logger;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        _logger.LogInformation("异步 OnResourceExecuting");

        var path = context.HttpContext.Request.Path.ToString();
        if (context.HttpContext.Request.Method == "Get")
        {
            //定义一个key存储缓存
            if (_dictionaryCache.Any(t => t.Key == path))
            {
                //如果缓存有内容就直接返回
                context.Result = _dictionaryCache[path] as IActionResult;    //Result 短路器
            }
            //如果没有缓存就接着运行，然后再executed里面设置缓存
        }
        else
        {
            context.HttpContext.Request.EnableBuffering();//可以实现多次读取Body
            var sr = new StreamReader(context.HttpContext.Request.Body);
            string data = sr.ReadToEndAsync().GetAwaiter().GetResult();//不允许同步读取
            if (data == null)//body取不到数据直接跳过，一般情况下不会出现该情况
                return;

            //获取到body的请求字符串
            _logger.LogInformation("data=" + data);
            //根据请求字符串去做处理解析是否做缓存，本次示例是获取到请求的boyd里面的ID，如果存在id，那么就做资源缓存(id作为key)，
            var jobject = JObject.Parse(data);
            if (jobject["id"]?.ToString() != null)
            {
                string key = path + jobject["id"].ToString();
                if (_dictionaryCache.Any(t => t.Key == key))
                {
                    //如果缓存有内容就直接返回
                    context.Result = _dictionaryCache[key] as IActionResult;    //Result 短路器
                }
            }

            context.HttpContext.Request.Body.Seek(0, SeekOrigin.Begin);//读取到Body后，重新设置Stream到起始位置
            context.HttpContext.Request.Body.Position = 0;
        }

        await next();

        _logger.LogInformation("异步 OnResourceExecuted");
        //把数据存储缓存 Key---path;  实际情况这里缓存应该加上过期时间
        if (context.HttpContext.Request.Method == "Get")
        {
            //将请求路径作为缓存的key
            _dictionaryCache[path] = context.Result;//刚才接口返回的值
        }
        else
        {
            context.HttpContext.Request.Body.Seek(0, SeekOrigin.Begin);//读取到Body后，重新设置Stream到起始位置
            context.HttpContext.Request.Body.Position = 0;
            var sr = new StreamReader(context.HttpContext.Request.Body);
            string data = await sr.ReadToEndAsync();//不允许同步读取
            if (data == null)
                return;

            var jobject = JObject.Parse(data);
            if (jobject["id"]?.ToString() != null)
            {
                string key = path + jobject["id"].ToString();
                _dictionaryCache[key] = context.Result;//刚才接口返回的值
            }
        }
    }
}