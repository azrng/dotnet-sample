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
        /// ���htmlģ��
        /// </summary>
        [HttpGet]
        public async Task<string> FillTemplateAsync()
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "View", "usergrade1.cshtml");
            if (!System.IO.File.Exists(filePath))
            {
                return "ģ���ļ�������";
            }
            //�򿪲��Ҷ�ȡģ��
            string template = System.IO.File.ReadAllText(filePath);

            //����
            string result = await _documentGenerationRazorEngion.CompileRenderStringAsync("templateKey", template, UserGradeDto.GetInfo());
            return result;
        }
    }
}