using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SettingConfig
{
    /// <summary>
    /// 配置UI控制器
    /// </summary>
    public class SettingUIMiddleware
    {
        private const string EmbeddedFileNamespace = "SettingConfig.wwwroot";
        private readonly StaticFileMiddleware _staticFileMiddleware;
        private readonly SettingUIOptions _settingUiOptions = ServiceCollectionExtensions.injectUIOptions;

        public SettingUIMiddleware(RequestDelegate next,
            IWebHostEnvironment hostingEnv,
            ILoggerFactory loggerFactory)
        {
            _staticFileMiddleware = CreateStaticFileMiddleware(next, hostingEnv, loggerFactory, _settingUiOptions);
        }

        public async Task Invoke(HttpContext httpContext)
        {
            var httpMethod = httpContext.Request.Method;
            var path = httpContext.Request.Path.Value;

            // If the RoutePrefix is requested (with or without trailing slash), redirect to index URL
            if (httpMethod == "GET" &&
                Regex.IsMatch(path, $"^/?{Regex.Escape(_settingUiOptions.RoutePrefix)}/?$", RegexOptions.IgnoreCase))
            {
                // Use relative redirect to support proxy environments
                var relativeIndexUrl = string.IsNullOrEmpty(path) || path.EndsWith("/")
                    ? "index.html"
                    : $"{path.Split('/').Last()}/index.html";

                RespondWithRedirect(httpContext.Response, relativeIndexUrl);
                return;
            }

            // 如果是get请求并且是指定前缀的，那么就 展示页面
            if (httpMethod == "GET" && Regex.IsMatch(path,
                    $"^/{Regex.Escape(_settingUiOptions.RoutePrefix)}/?index.html$", RegexOptions.IgnoreCase))
            {
                await RespondWithIndexHtml(httpContext.Response);
                return;
            }

            await _staticFileMiddleware.Invoke(httpContext);
        }

        private StaticFileMiddleware CreateStaticFileMiddleware(
            RequestDelegate next,
            IWebHostEnvironment hostingEnv,
            ILoggerFactory loggerFactory,
            SettingUIOptions options)
        {
            var staticFileOptions = new StaticFileOptions
            {
                RequestPath = string.IsNullOrEmpty(options.RoutePrefix) ? string.Empty : $"/{options.RoutePrefix}",
                FileProvider = new EmbeddedFileProvider(typeof(SettingUIOptions).GetTypeInfo().Assembly,
                    EmbeddedFileNamespace),
            };

            return new StaticFileMiddleware(next, hostingEnv, Options.Create(staticFileOptions), loggerFactory);
        }

        /// <summary>
        /// 响应跳转
        /// </summary>
        /// <param name="response"></param>
        /// <param name="location"></param>
        private void RespondWithRedirect(HttpResponse response, string location)
        {
            response.StatusCode = 301;
            response.Headers["Location"] = location;
        }

    /// <summary>
    /// 响应Html内容
    /// </summary>
    /// <param name="response"></param>
    private async Task RespondWithIndexHtml(HttpResponse response)
    {
        response.StatusCode = 200;
        response.ContentType = "text/html;charset=utf-8";

        await using var stream = _settingUiOptions.IndexStream();
        using var reader = new StreamReader(stream);

        // Inject arguments before writing to response
        var htmlBuilder = new StringBuilder(await reader.ReadToEndAsync());
        foreach (var entry in GetIndexArguments())
        {
            htmlBuilder.Replace(entry.Key, entry.Value);
        }

        await response.WriteAsync(htmlBuilder.ToString(), Encoding.UTF8);
    }

        private IDictionary<string, string> GetIndexArguments()
        {
            return new Dictionary<string, string>()
            {
                { "%(PageTitle)%", _settingUiOptions.PageTitle },
                { "%(PageDescription)%", _settingUiOptions.PageDescription },
            };
        }
    }
}