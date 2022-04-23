using Microsoft.AspNetCore.Mvc;
using RazorLightWebApp.Models;
using RazorLightWebApp.Utils;

namespace RazorLightWebApp.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class HomeController : ControllerBase
    {
        private readonly IDocumentGeneration _documentGenerationRazorEngion;

        public HomeController(IDocumentGeneration documentGenerationRazorEngion)
        {
            _documentGenerationRazorEngion = documentGenerationRazorEngion;
        }

        /// <summary>
        /// 填充html模板
        /// </summary>
        [HttpGet]
        public async Task<string> FillTemplateAsync()
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "View", "usergrade1.cshtml");
            if (!System.IO.File.Exists(filePath))
            {
                return "模板文件不存在";
            }
            //打开并且读取模板
            string template = System.IO.File.ReadAllText(filePath);

            //运行
            string result = await _documentGenerationRazorEngion.CompileRenderStringAsync("templateKey", template, UserGradeDto.GetInfo());
            return result;
        }
    }
}